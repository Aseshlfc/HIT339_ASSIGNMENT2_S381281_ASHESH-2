using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public class LibraryContext : IdentityDbContext<ApplicationUser>
{
    public LibraryContext(DbContextOptions<LibraryContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Music> Music => Set<Music>();
    public DbSet<Toy> Toys => Set<Toy>();
    public DbSet<Borrower> Borrowers => Set<Borrower>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ItemTransfer> ItemTransfers => Set<ItemTransfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Item>()
            .HasDiscriminator<string>("ItemDiscriminator")
            .HasValue<Book>("Book")
            .HasValue<Music>("Music")
            .HasValue<Toy>("Toy");

        modelBuilder.Entity<Item>()
            .HasIndex(i => i.LibraryCode)
            .IsUnique();

        // Concurrency protection for borrowing: SQLite has no SQL Server-style
        // rowversion/timestamp column, so Status itself is used as an optimistic
        // concurrency token. EF Core adds "AND Status = @original" to every
        // UPDATE it issues against Items. If two requests both read the same
        // item as Available and race to borrow it, the first UPDATE (Available
        // -> Borrowed) succeeds and changes the stored Status; the second
        // UPDATE's WHERE clause no longer matches any row (the stored Status
        // has already changed), so EF Core reports 0 rows affected and throws
        // DbUpdateConcurrencyException instead of silently succeeding twice.
        modelBuilder.Entity<Item>()
            .Property(i => i.Status)
            .IsConcurrencyToken();

        modelBuilder.Entity<Loan>()
            .Property(l => l.FineAmount)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Loan>()
            .HasOne(l => l.Item)
            .WithMany(i => i.Loans)
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Loan>()
            .HasOne(l => l.Borrower)
            .WithMany(b => b.Loans)
            .HasForeignKey(l => l.BorrowerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Branch>()
            .HasIndex(b => b.Code)
            .IsUnique();

        // Created by the migration so that every existing item has a valid
        // branch to belong to when the BranchId column is added.
        modelBuilder.Entity<Branch>().HasData(new Branch
        {
            Id = Branch.DefaultBranchId,
            Code = "CEN",
            Name = "Central Library",
            Address = "1 Library Square, Springfield",
            Phone = "0800000001",
            IsActive = true
        });

        modelBuilder.Entity<Item>()
            .HasOne(i => i.Branch)
            .WithMany(b => b.Items)
            .HasForeignKey(i => i.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApplicationUser>()
            .HasOne(u => u.Branch)
            .WithMany(b => b.Staff)
            .HasForeignKey(u => u.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Loan>()
            .HasOne(l => l.Branch)
            .WithMany()
            .HasForeignKey(l => l.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ItemTransfer>(transfer =>
        {
            transfer.HasOne(t => t.Item).WithMany().HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne(t => t.FromBranch).WithMany().HasForeignKey(t => t.FromBranchId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne(t => t.ToBranch).WithMany().HasForeignKey(t => t.ToBranchId).OnDelete(DeleteBehavior.Restrict);

            // Same approach as Item.Status: two requests acting on the same
            // transfer cannot both succeed, so it cannot be completed twice.
            transfer.Property(t => t.Status).IsConcurrencyToken();

            // At most one open (Requested = 0 / InTransit = 1) transfer per item,
            // enforced by the database as well as by TransferService.
            transfer.HasIndex(t => t.ItemId, "IX_ItemTransfers_OpenItem")
                .IsUnique()
                .HasFilter("\"Status\" IN (0, 1)");
        });
    }
}
