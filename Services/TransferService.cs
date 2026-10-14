using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public record TransferResult(bool Succeeded, string Message, ItemTransfer? Transfer = null)
{
    public static TransferResult Fail(string message) => new(false, message);
    public static TransferResult Ok(string message, ItemTransfer transfer) => new(true, message, transfer);
}

/// <summary>
/// Business rules for moving an item between branches. Callers decide who is
/// allowed to act for a branch; this service decides whether the move itself
/// is valid.
/// </summary>
public interface ITransferService
{
    Task<TransferResult> RequestAsync(int itemId, int toBranchId, string? reason, string requestedBy);
    Task<TransferResult> DispatchAsync(int transferId, string actor);
    Task<TransferResult> ReceiveAsync(int transferId, string actor);
    Task<TransferResult> CancelAsync(int transferId, string actor, string? note = null);
    Task<TransferResult> RejectAsync(int transferId, string actor, string? note = null);
    Task<ItemTransfer?> GetOpenTransferForItemAsync(int itemId);

    /// <summary>
    /// Admin correction of a wrongly recorded location. Not a physical move:
    /// it is applied immediately and stored in the movement history as a
    /// correction with a mandatory reason.
    /// </summary>
    Task<TransferResult> RecordCorrectionAsync(int itemId, int toBranchId, string? reason, string actor);
}

public static class ItemMovements
{
    /// <summary>
    /// Records that an item was handed back at a different branch from the one
    /// holding it, moves it to that branch, and closes any transfer request
    /// that was still waiting for it. The caller saves the changes together
    /// with the loan it is returning.
    /// </summary>
    public static async Task<ItemTransfer> RecordCrossBranchReturnAsync(LibraryContext db, Item item, Branch desk, string actor, int loanId)
    {
        var now = DateTime.Now;

        var waiting = await db.ItemTransfers.FirstOrDefaultAsync(t => t.ItemId == item.Id && t.Status == TransferStatus.Requested);
        if (waiting != null)
        {
            waiting.Status = TransferStatus.Cancelled;
            waiting.ClosedAt = now;
            waiting.ClosedBy = actor;
            waiting.ClosureNote = $"Cancelled automatically: the item was returned at {desk.Name}.";
        }

        var movement = new ItemTransfer
        {
            Kind = TransferKind.CrossBranchReturn,
            ItemId = item.Id,
            FromBranchId = item.BranchId,
            ToBranchId = desk.Id,
            Status = TransferStatus.Completed,
            Reason = $"Returned at {desk.Name} (loan #{loanId}).",
            RequestedAt = now,
            RequestedBy = actor,
            ClosedAt = now,
            ClosedBy = actor
        };
        db.ItemTransfers.Add(movement);
        item.BranchId = desk.Id;
        return movement;
    }
}

public class TransferService : ITransferService
{
    private const string ChangedElsewhere = "This transfer or item was changed by someone else just now. Please reload and try again.";

    private readonly LibraryContext _db;
    public TransferService(LibraryContext db) => _db = db;

    private const string HeldMessage =
        "This item is reserved for a borrower and cannot be transferred until the hold is collected, cancelled or expired.";

    // An Available item with a Waiting or Ready hold is being kept at this branch for a borrower.
    private Task<bool> HasActiveHoldAsync(int itemId) =>
        _db.Reservations.AnyAsync(r => r.ItemId == itemId &&
            (r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Ready));

    public Task<ItemTransfer?> GetOpenTransferForItemAsync(int itemId)
        => _db.ItemTransfers.AsNoTracking()
            .Include(t => t.FromBranch)
            .Include(t => t.ToBranch)
            .FirstOrDefaultAsync(t => t.ItemId == itemId
                && (t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit));

    public async Task<TransferResult> RequestAsync(int itemId, int toBranchId, string? reason, string requestedBy)
    {
        var item = await _db.Items.SingleOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return TransferResult.Fail("The selected item was not found.");

        var unavailable = item.Status switch
        {
            ItemStatus.Available => null,
            ItemStatus.Borrowed => "This item is on loan. It can be transferred once it has been returned.",
            ItemStatus.InTransit => "This item is already in transit between branches.",
            _ => $"This item is marked {item.Status.Label()} and cannot be transferred."
        };
        if (unavailable != null) return TransferResult.Fail(unavailable);

        // Guards against the item being marked Available while a loan is still open.
        if (await _db.Loans.AnyAsync(l => l.ItemId == itemId && l.ReturnedAt == null))
            return TransferResult.Fail("This item still has an open loan and cannot be transferred until it is returned.");

        if (await HasActiveHoldAsync(itemId))
            return TransferResult.Fail(HeldMessage);

        var fromBranch = await _db.Branches.SingleOrDefaultAsync(b => b.Id == item.BranchId);
        if (fromBranch == null) return TransferResult.Fail("The item's current branch could not be found.");

        var toBranch = await _db.Branches.SingleOrDefaultAsync(b => b.Id == toBranchId);
        if (toBranch == null) return TransferResult.Fail("The destination branch was not found.");
        if (!toBranch.IsActive) return TransferResult.Fail($"{toBranch.Name} is inactive and cannot receive transfers.");
        if (toBranch.Id == fromBranch.Id)
            return TransferResult.Fail($"This item is already held at {toBranch.Name}. Choose a different destination branch.");

        if (await HasOpenTransferAsync(itemId))
            return TransferResult.Fail("This item already has an open transfer. Complete or cancel it first.");

        var transfer = new ItemTransfer
        {
            ItemId = item.Id,
            FromBranchId = fromBranch.Id,
            ToBranchId = toBranch.Id,
            Status = TransferStatus.Requested,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            RequestedAt = DateTime.Now,
            RequestedBy = requestedBy
        };
        _db.ItemTransfers.Add(transfer);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index on open transfers rejected a duplicate created
            // by a simultaneous request.
            _db.Entry(transfer).State = EntityState.Detached;
            return TransferResult.Fail("This item already has an open transfer. Complete or cancel it first.");
        }

        return TransferResult.Ok($"Transfer requested: {item.Name} from {fromBranch.Name} to {toBranch.Name}.", transfer);
    }

    public async Task<TransferResult> DispatchAsync(int transferId, string actor)
    {
        var transfer = await LoadAsync(transferId);
        if (transfer == null) return TransferResult.Fail("Transfer was not found.");
        if (transfer.Status != TransferStatus.Requested)
            return TransferResult.Fail($"Only a requested transfer can be dispatched. This transfer is {transfer.Status.Label()}.");

        var item = transfer.Item;
        if (item.BranchId != transfer.FromBranchId)
            return TransferResult.Fail("This item is no longer held at the source branch. Cancel this transfer and create a new one.");
        if (item.Status != ItemStatus.Available)
            return TransferResult.Fail($"This item is currently {item.Status.Label()} and cannot be dispatched.");
        if (await _db.Loans.AnyAsync(l => l.ItemId == item.Id && l.ReturnedAt == null))
            return TransferResult.Fail("This item still has an open loan and cannot be dispatched.");
        // A hold placed after the transfer was requested: the item must stay for that borrower.
        if (await HasActiveHoldAsync(item.Id))
            return TransferResult.Fail(HeldMessage);

        // Item.Status is a concurrency token, so a borrow that lands between
        // the check above and this save makes the save fail instead of sending
        // a borrowed item away.
        item.Status = ItemStatus.InTransit;
        transfer.Status = TransferStatus.InTransit;
        transfer.DispatchedAt = DateTime.Now;
        transfer.DispatchedBy = actor;

        return await SaveAsync(transfer, $"{item.Name} dispatched to {transfer.ToBranch.Name}.");
    }

    public async Task<TransferResult> ReceiveAsync(int transferId, string actor)
    {
        var transfer = await LoadAsync(transferId);
        if (transfer == null) return TransferResult.Fail("Transfer was not found.");
        if (transfer.Status == TransferStatus.Completed)
            return TransferResult.Fail("This transfer has already been completed.");
        if (transfer.Status != TransferStatus.InTransit)
            return TransferResult.Fail($"Only a transfer that is in transit can be received. This transfer is {transfer.Status.Label()}.");

        var item = transfer.Item;
        if (item.Status != ItemStatus.InTransit)
            return TransferResult.Fail($"This item is marked {item.Status.Label()}, not In transit. Cancel this transfer and check the item record.");

        // The item's physical branch changes here, when the destination
        // confirms it has arrived.
        item.BranchId = transfer.ToBranchId;
        item.Status = ItemStatus.Available;
        transfer.Status = TransferStatus.Completed;
        transfer.ClosedAt = DateTime.Now;
        transfer.ClosedBy = actor;

        return await SaveAsync(transfer, $"{item.Name} received at {transfer.ToBranch.Name} and is available to borrow.");
    }

    public async Task<TransferResult> CancelAsync(int transferId, string actor, string? note = null)
    {
        var transfer = await LoadAsync(transferId);
        if (transfer == null) return TransferResult.Fail("Transfer was not found.");
        if (!transfer.IsOpen)
            return TransferResult.Fail($"This transfer is already {transfer.Status.Label()} and cannot be cancelled.");

        // A dispatched item goes back on the shelf at the source branch. Its
        // BranchId never changed, so only the status needs restoring.
        if (transfer.Status == TransferStatus.InTransit && transfer.Item.Status == ItemStatus.InTransit)
            transfer.Item.Status = ItemStatus.Available;

        Close(transfer, TransferStatus.Cancelled, actor, note);
        return await SaveAsync(transfer, $"Transfer cancelled. {transfer.Item.Name} remains at {transfer.FromBranch.Name}.");
    }

    public async Task<TransferResult> RejectAsync(int transferId, string actor, string? note = null)
    {
        var transfer = await LoadAsync(transferId);
        if (transfer == null) return TransferResult.Fail("Transfer was not found.");
        if (transfer.Status != TransferStatus.Requested)
            return TransferResult.Fail($"Only a requested transfer can be rejected. This transfer is {transfer.Status.Label()}.");

        Close(transfer, TransferStatus.Rejected, actor, note);
        return await SaveAsync(transfer, $"Transfer rejected. {transfer.Item.Name} remains at {transfer.FromBranch.Name}.");
    }

    public async Task<TransferResult> RecordCorrectionAsync(int itemId, int toBranchId, string? reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return TransferResult.Fail("A reason is required for a location correction.");

        var item = await _db.Items.SingleOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return TransferResult.Fail("The selected item was not found.");

        if (item.Status == ItemStatus.Borrowed || await _db.Loans.AnyAsync(l => l.ItemId == itemId && l.ReturnedAt == null))
            return TransferResult.Fail("This item is on loan. Its location can be corrected once it has been returned.");
        if (item.Status == ItemStatus.InTransit || await HasOpenTransferAsync(itemId))
            return TransferResult.Fail("This item has an open transfer. Complete or cancel the transfer first.");

        var fromBranch = await _db.Branches.SingleOrDefaultAsync(b => b.Id == item.BranchId);
        if (fromBranch == null) return TransferResult.Fail("The item's current branch could not be found.");
        var toBranch = await _db.Branches.SingleOrDefaultAsync(b => b.Id == toBranchId);
        if (toBranch == null) return TransferResult.Fail("The selected branch was not found.");
        if (!toBranch.IsActive) return TransferResult.Fail($"{toBranch.Name} is inactive and cannot hold items.");
        if (toBranch.Id == fromBranch.Id) return TransferResult.Fail($"This item is already recorded at {toBranch.Name}.");

        var now = DateTime.Now;
        var correction = new ItemTransfer
        {
            Kind = TransferKind.Correction,
            ItemId = item.Id,
            FromBranchId = fromBranch.Id,
            ToBranchId = toBranch.Id,
            Status = TransferStatus.Completed,
            Reason = reason.Trim(),
            RequestedAt = now,
            RequestedBy = actor,
            ClosedAt = now,
            ClosedBy = actor
        };
        _db.ItemTransfers.Add(correction);
        item.BranchId = toBranch.Id;

        correction.Item = item;
        correction.FromBranch = fromBranch;
        correction.ToBranch = toBranch;
        return await SaveAsync(correction, $"Location corrected: {item.Name} is now recorded at {toBranch.Name}.");
    }

    private static void Close(ItemTransfer transfer, TransferStatus status, string actor, string? note)
    {
        transfer.Status = status;
        transfer.ClosedAt = DateTime.Now;
        transfer.ClosedBy = actor;
        transfer.ClosureNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private Task<bool> HasOpenTransferAsync(int itemId)
        => _db.ItemTransfers.AnyAsync(t => t.ItemId == itemId
            && (t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit));

    private Task<ItemTransfer?> LoadAsync(int transferId)
        => _db.ItemTransfers
            .Include(t => t.Item)
            .Include(t => t.FromBranch)
            .Include(t => t.ToBranch)
            .SingleOrDefaultAsync(t => t.Id == transferId);

    private async Task<TransferResult> SaveAsync(ItemTransfer transfer, string successMessage)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransferResult.Fail(ChangedElsewhere);
        }
        return TransferResult.Ok(successMessage, transfer);
    }
}
