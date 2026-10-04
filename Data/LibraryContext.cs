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
    }
}
