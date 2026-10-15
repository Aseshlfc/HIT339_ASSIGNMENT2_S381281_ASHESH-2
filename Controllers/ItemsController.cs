using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public class ItemsController : Controller
{
    private readonly LibraryContext _db;

    private readonly ITransferService _transfers;
    private readonly IReservationService? _reservations;

    public ItemsController(LibraryContext db, ITransferService? transfers = null, IReservationService? reservations = null)
    {
        _db = db;
        _transfers = transfers ?? new TransferService(db);
        _reservations = reservations;
    }

    public async Task<IActionResult> Index(int? branchId = null)
    {
        var query = _db.Items.AsNoTracking();
        if (branchId.HasValue) query = query.Where(i => i.BranchId == branchId);

        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        ViewBag.Branches = branches;
        ViewBag.BranchNames = branches.ToDictionary(b => b.Id, b => b.Name);
        ViewBag.BranchId = branchId;
        return View(await query.OrderBy(i => i.LibraryCode).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var item = await _db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();

        var transfers = await _db.ItemTransfers.AsNoTracking()
            .Include(t => t.FromBranch).Include(t => t.ToBranch)
            .Where(t => t.ItemId == id)
            .OrderByDescending(t => t.RequestedAt)
            .ToListAsync();

        return View(new ItemDetailsViewModel
        {
            Item = item,
            BranchName = await _db.Branches.Where(b => b.Id == item.BranchId).Select(b => b.Name).SingleOrDefaultAsync() ?? "Unknown",
            OpenTransfer = transfers.FirstOrDefault(t => t.IsOpen),
            TransferHistory = transfers,
            LoanCount = await _db.Loans.CountAsync(l => l.ItemId == id),
            ActiveLoan = await _db.Loans.AsNoTracking().Include(l => l.Borrower)
                .FirstOrDefaultAsync(l => l.ItemId == id && l.ReturnedAt == null)
        });
    }

    [HttpGet]
    public Task<IActionResult> Create() => FormView(new ItemFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ItemFormViewModel model)
    {
        ValidateTypeSpecificFields(model);
        if (await _db.Items.AnyAsync(i => i.LibraryCode == model.LibraryCode))
            ModelState.AddModelError(nameof(model.LibraryCode), "Library code must be unique.");

        if (model.Status == ItemStatus.InTransit)
            ModelState.AddModelError(nameof(model.Status), "In transit is set by the transfer workflow and cannot be chosen here.");
        await ValidateBranchAsync(model, currentBranchId: null);

        if (!ModelState.IsValid) return await FormView(model);

        var item = CreateItemFromModel(model);
        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.Items.FindAsync(id);
        if (item == null) return NotFound();
        return await FormView(ToFormModel(item));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ItemFormViewModel model)
    {
        if (!model.Id.HasValue) return BadRequest();
        ValidateTypeSpecificFields(model);

        if (await _db.Items.AnyAsync(i => i.LibraryCode == model.LibraryCode && i.Id != model.Id.Value))
            ModelState.AddModelError(nameof(model.LibraryCode), "Library code must be unique.");

        if (!ModelState.IsValid) return await FormView(model);

        var item = await _db.Items.FindAsync(model.Id.Value);
        if (item == null) return NotFound();

        // An existing item's branch is never changed by this form: a physical
        // move goes through a transfer and a wrong record through "Correct
        // location", so every change of location leaves a history record.
        if (model.BranchId.HasValue && model.BranchId != item.BranchId)
            ModelState.AddModelError(nameof(model.BranchId), "The branch cannot be changed here. Request a transfer to move the item, or use Correct location to fix a wrong record.");
        model.BranchId = item.BranchId;

        var hasOpenTransfer = await _db.ItemTransfers.AnyAsync(t => t.ItemId == item.Id
            && (t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit));
        if (hasOpenTransfer && model.Status != item.Status)
            ModelState.AddModelError(string.Empty, "This item has an open transfer. Complete or cancel the transfer before changing its status.");
        else if (model.Status == ItemStatus.InTransit && item.Status != ItemStatus.InTransit)
            ModelState.AddModelError(nameof(model.Status), "In transit is set by the transfer workflow and cannot be chosen here.");

        if (!ModelState.IsValid) return await FormView(model);

        // For simplicity, item type cannot be changed after creation.
        if (!string.Equals(item.ItemType, model.ItemType, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.ItemType), "Item type cannot be changed after creation.");
            return await FormView(model);
        }

        var previousStatus = item.Status; 
        item.Name = model.Name;
        item.Description = model.Description;
        item.LibraryCode = model.LibraryCode;
        item.Status = model.Status;

        switch (item)
        {
            case Book b:
                b.Author = model.Author!;
                b.Genre = model.BookGenre!;
                b.PublicationYear = model.PublicationYear;
                break;
            case Music m:
                m.Artist = model.Artist!;
                m.Genre = model.MusicGenre ?? string.Empty;
                m.ReleaseYear = model.ReleaseYear;
                break;
            case Toy t:
                t.ToyType = model.ToyType!;
                t.MinimumAge = model.MinimumAge ?? 0;
                t.Manufacturer = model.Manufacturer ?? string.Empty;
                break;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Item.Status is a concurrency token (see LibraryContext). Someone
            // else (e.g. Reception borrowing it) changed this item's status
            // between our read and our write.
            ModelState.AddModelError(string.Empty, "This item was changed by someone else just now. Please reload and try again.");
            return await FormView(model);
        }

        // An Admin fixing a Damaged item makes it Available again: offer it to the first borrower in the queue.
        if (_reservations != null && previousStatus != ItemStatus.Available && item.Status == ItemStatus.Available)
            await _reservations.NotifyNextInQueueAsync(item);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
        return item == null ? NotFound() : View(item);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var item = await _db.Items.Include(i => i.Loans).SingleOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();

        if (item.Loans.Any())
        {
            TempData["Error"] = "This item has loan history and cannot be deleted. Set its status to Destroy instead.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.ItemTransfers.AnyAsync(t => t.ItemId == id))
        {
            TempData["Error"] = "This item has transfer history and cannot be deleted. Set its status to Destroy instead.";
            return RedirectToAction(nameof(Index));
        }

        _db.Items.Remove(item);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Covers a status change or a new loan/transfer made by someone
            // else between loading the item and deleting it.
            TempData["Error"] = "This item was changed by someone else just now and was not deleted. Please reload and try again.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> CorrectBranch(int id)
    {
        var item = await _db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();
        return await CorrectionView(item, new ItemBranchCorrectionViewModel { ItemId = item.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectBranch(ItemBranchCorrectionViewModel model)
    {
        var item = await _db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == model.ItemId);
        if (item == null) return NotFound();

        if (ModelState.IsValid)
        {
            var result = await _transfers.RecordCorrectionAsync(item.Id, model.ToBranchId!.Value, model.Reason, User?.Identity?.Name ?? "unknown");
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = item.Id });
            }
            ModelState.AddModelError(string.Empty, result.Message);
        }
        return await CorrectionView(item, model);
    }

    private async Task<IActionResult> CorrectionView(Item item, ItemBranchCorrectionViewModel model)
    {
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        model.ItemName = item.Name;
        model.LibraryCode = item.LibraryCode;
        model.CurrentBranchName = branches.SingleOrDefault(b => b.Id == item.BranchId)?.Name ?? "Unknown";
        ViewBag.Branches = branches.Where(b => b.IsActive && b.Id != item.BranchId).ToList();
        return View("CorrectBranch", model);
    }

    private async Task<IActionResult> FormView(ItemFormViewModel model)
    {
        // Inactive branches are not offered, except the one the item is already at.
        ViewBag.Branches = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive || b.Id == model.BranchId)
            .OrderBy(b => b.Name)
            .ToListAsync();
        return View("Form", model);
    }

    private async Task ValidateBranchAsync(ItemFormViewModel model, int? currentBranchId)
    {
        if (!model.BranchId.HasValue || model.BranchId == currentBranchId) return;

        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == model.BranchId);
        if (branch == null)
            ModelState.AddModelError(nameof(model.BranchId), "The selected branch was not found.");
        else if (!branch.IsActive)
            ModelState.AddModelError(nameof(model.BranchId), $"{branch.Name} is inactive and cannot hold items.");
    }

    private void ValidateTypeSpecificFields(ItemFormViewModel model)
    {
        switch (model.ItemType)
        {
            case "Book":
                if (string.IsNullOrWhiteSpace(model.Author))
                    ModelState.AddModelError(nameof(model.Author), "Author is required for books.");
                if (string.IsNullOrWhiteSpace(model.BookGenre))
                    ModelState.AddModelError(nameof(model.BookGenre), "Genre is required for books.");
                break;
            case "Music":
                if (string.IsNullOrWhiteSpace(model.Artist))
                    ModelState.AddModelError(nameof(model.Artist), "Artist is required for music.");
                break;
            case "Toy":
                if (string.IsNullOrWhiteSpace(model.ToyType))
                    ModelState.AddModelError(nameof(model.ToyType), "Toy type is required.");
                if (!model.MinimumAge.HasValue)
                    ModelState.AddModelError(nameof(model.MinimumAge), "Minimum age is required.");
                break;
            default:
                ModelState.AddModelError(nameof(model.ItemType), "Choose Book, Music or Toy.");
                break;
        }
    }

    private static Item CreateItemFromModel(ItemFormViewModel model)
    {
        Item item = model.ItemType switch
        {
            "Book" => new Book
            {
                Author = model.Author!,
                Genre = model.BookGenre!,
                PublicationYear = model.PublicationYear
            },
            "Music" => new Music
            {
                Artist = model.Artist!,
                Genre = model.MusicGenre ?? string.Empty,
                ReleaseYear = model.ReleaseYear
            },
            "Toy" => new Toy
            {
                ToyType = model.ToyType!,
                MinimumAge = model.MinimumAge ?? 0,
                Manufacturer = model.Manufacturer ?? string.Empty
            },
            _ => throw new ArgumentException("Unsupported item type")
        };

        item.Name = model.Name;
        item.Description = model.Description;
        item.LibraryCode = model.LibraryCode;
        item.Status = model.Status;
        // No branch chosen (e.g. created by code rather than the form): default branch.
        item.BranchId = model.BranchId ?? Branch.DefaultBranchId;
        return item;
    }

    private static ItemFormViewModel ToFormModel(Item item)
    {
        var model = new ItemFormViewModel
        {
            Id = item.Id,
            ItemType = item.ItemType,
            Name = item.Name,
            Description = item.Description,
            LibraryCode = item.LibraryCode,
            Status = item.Status,
            BranchId = item.BranchId
        };

        switch (item)
        {
            case Book b:
                model.Author = b.Author;
                model.BookGenre = b.Genre;
                model.PublicationYear = b.PublicationYear;
                break;
            case Music m:
                model.Artist = m.Artist;
                model.MusicGenre = m.Genre;
                model.ReleaseYear = m.ReleaseYear;
                break;
            case Toy t:
                model.ToyType = t.ToyType;
                model.MinimumAge = t.MinimumAge;
                model.Manufacturer = t.Manufacturer;
                break;
        }

        return model;
    }
}
