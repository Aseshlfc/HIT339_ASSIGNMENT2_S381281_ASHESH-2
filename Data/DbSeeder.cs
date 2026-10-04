using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(LibraryContext db, RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
    {
        foreach (var role in StaffRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        await AddStaffUserAsync(userManager, "admin", "Admin123!", "Admin");
        await AddStaffUserAsync(userManager, "reception", "Reception123!", "Reception");
        await AddStaffUserAsync(userManager, "manager", "Manager123!", "Manager");

        if (!await db.Items.AnyAsync())
        {
            db.Items.AddRange(
                new Book { Name = "The Hobbit", Description = "A reluctant hobbit sets out on an unexpected adventure.", LibraryCode = "B001", Status = ItemStatus.Available, Author = "J. R. R. Tolkien", Genre = "Fantasy", PublicationYear = 1937 },
                new Book { Name = "Harry Potter and the Philosopher's Stone", Description = "A young boy discovers he is a wizard.", LibraryCode = "B002", Status = ItemStatus.Available, Author = "J. K. Rowling", Genre = "Fantasy", PublicationYear = 1997 },
                new Book { Name = "Pride and Prejudice", Description = "A witty tale of manners, marriage and misjudgement.", LibraryCode = "B003", Status = ItemStatus.Available, Author = "Jane Austen", Genre = "Romance", PublicationYear = 1813 },
                new Book { Name = "1984", Description = "A dystopian vision of a totalitarian surveillance state.", LibraryCode = "B004", Status = ItemStatus.Available, Author = "George Orwell", Genre = "Dystopian", PublicationYear = 1949 },
                new Book { Name = "To Kill a Mockingbird", Description = "A young girl learns about justice and prejudice in the American South.", LibraryCode = "B005", Status = ItemStatus.Available, Author = "Harper Lee", Genre = "Classic", PublicationYear = 1960 },
                new Book { Name = "The Great Gatsby", Description = "A tale of wealth, love and tragedy in the Jazz Age.", LibraryCode = "B006", Status = ItemStatus.Damaged, Author = "F. Scott Fitzgerald", Genre = "Classic", PublicationYear = 1925 },
                new Book { Name = "Charlie and the Chocolate Factory", Description = "A poor boy wins a golden ticket to a magical chocolate factory.", LibraryCode = "B007", Status = ItemStatus.Available, Author = "Roald Dahl", Genre = "Children's", PublicationYear = 1964 },
                new Book { Name = "Dune", Description = "A desert planet, a noble family and a coming rebellion.", LibraryCode = "B008", Status = ItemStatus.Borrowed, Author = "Frank Herbert", Genre = "Science Fiction", PublicationYear = 1965 },

                new Music { Name = "Thriller", Description = "Landmark pop album with genre-defining hits.", LibraryCode = "M001", Status = ItemStatus.Available, Artist = "Michael Jackson", Genre = "Pop", ReleaseYear = 1982 },
                new Music { Name = "Abbey Road", Description = "The band's iconic eleventh studio album.", LibraryCode = "M002", Status = ItemStatus.Available, Artist = "The Beatles", Genre = "Rock", ReleaseYear = 1969 },
                new Music { Name = "Back in Black", Description = "A tribute album that became a rock classic.", LibraryCode = "M003", Status = ItemStatus.Available, Artist = "AC/DC", Genre = "Rock", ReleaseYear = 1980 },
                new Music { Name = "Rumours", Description = "A studio album born out of personal turmoil within the band.", LibraryCode = "M004", Status = ItemStatus.Borrowed, Artist = "Fleetwood Mac", Genre = "Rock", ReleaseYear = 1977 },
                new Music { Name = "The Dark Side of the Moon", Description = "A concept album exploring conflict, time and mental health.", LibraryCode = "M005", Status = ItemStatus.Available, Artist = "Pink Floyd", Genre = "Progressive Rock", ReleaseYear = 1973 },
                new Music { Name = "Songs in the Key of Life", Description = "A double album showcasing a soul legend at his peak.", LibraryCode = "M006", Status = ItemStatus.Destroy, Artist = "Stevie Wonder", Genre = "Soul", ReleaseYear = 1976 },

                new Toy { Name = "Wooden Building Blocks", Description = "Box of colourful wooden blocks.", LibraryCode = "T001", Status = ItemStatus.Available, ToyType = "Construction", MinimumAge = 3, Manufacturer = "Library Toy Collection" },
                new Toy { Name = "Jigsaw Puzzle 500pc", Description = "A challenging 500-piece landscape jigsaw puzzle.", LibraryCode = "T002", Status = ItemStatus.Available, ToyType = "Puzzle", MinimumAge = 8, Manufacturer = "Library Toy Collection" },
                new Toy { Name = "Plush Teddy Bear", Description = "A soft and cuddly teddy bear.", LibraryCode = "T003", Status = ItemStatus.Available, ToyType = "Soft Toy", MinimumAge = 0, Manufacturer = "Library Toy Collection" },
                new Toy { Name = "Remote Control Car", Description = "A fast remote-controlled racing car.", LibraryCode = "T004", Status = ItemStatus.Available, ToyType = "Electronic", MinimumAge = 6, Manufacturer = "Library Toy Collection" },
                new Toy { Name = "Chess Set", Description = "A classic wooden strategy board game.", LibraryCode = "T005", Status = ItemStatus.Borrowed, ToyType = "Strategy Game", MinimumAge = 6, Manufacturer = "Library Toy Collection" },
                new Toy { Name = "Lego Space Set", Description = "A buildable space-themed construction set.", LibraryCode = "T006", Status = ItemStatus.Damaged, ToyType = "Construction", MinimumAge = 7, Manufacturer = "Library Toy Collection" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.Borrowers.AnyAsync())
        {
            db.Borrowers.AddRange(
                new Borrower { FullName = "Alice Nguyen", Email = "alice.nguyen@example.com", Phone = "0400111222", Address = "12 Example St, Springfield" },
                new Borrower { FullName = "Ben Carter", Email = "ben.carter@example.com", Phone = "0400333444", Address = "45 Sample Ave, Springfield" },
                new Borrower { FullName = "Chloe Smith", Email = "chloe.smith@example.com", Phone = "0400555666", Address = "8 Demo Rd, Springfield" },
                new Borrower { FullName = "David Lee", Email = "david.lee@example.com", Phone = "0400777888", Address = "23 Test Ln, Springfield" },
                new Borrower { FullName = "Priya Patel", Email = "priya.patel@example.com", Phone = "0400999000", Address = "5 Trial Ct, Springfield" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.Loans.AnyAsync())
        {
            var alice = await db.Borrowers.SingleAsync(b => b.FullName == "Alice Nguyen");
            var ben = await db.Borrowers.SingleAsync(b => b.FullName == "Ben Carter");
            var chloe = await db.Borrowers.SingleAsync(b => b.FullName == "Chloe Smith");
            var david = await db.Borrowers.SingleAsync(b => b.FullName == "David Lee");
            var priya = await db.Borrowers.SingleAsync(b => b.FullName == "Priya Patel");

            var dune = await db.Items.SingleAsync(i => i.LibraryCode == "B008");
            var rumours = await db.Items.SingleAsync(i => i.LibraryCode == "M004");
            var chessSet = await db.Items.SingleAsync(i => i.LibraryCode == "T005");
            var remoteControlCar = await db.Items.SingleAsync(i => i.LibraryCode == "T004");
            var nineteenEightyFour = await db.Items.SingleAsync(i => i.LibraryCode == "B004");
            var abbeyRoad = await db.Items.SingleAsync(i => i.LibraryCode == "M002");

            var now = DateTime.Now;

            db.Loans.AddRange(
                // Active loan, not yet overdue.
                new Loan { ItemId = dune.Id, BorrowerId = alice.Id, BorrowedAt = now.AddDays(-10), DueDate = now.Date.AddDays(4) },
                // Active loan, overdue.
                new Loan { ItemId = rumours.Id, BorrowerId = ben.Id, BorrowedAt = now.AddDays(-20), DueDate = now.Date.AddDays(-6) },
                // Active loan, overdue.
                new Loan { ItemId = chessSet.Id, BorrowerId = priya.Id, BorrowedAt = now.AddDays(-16), DueDate = now.Date.AddDays(-2) },
                // Returned late, fine paid.
                new Loan { ItemId = remoteControlCar.Id, BorrowerId = chloe.Id, BorrowedAt = now.AddDays(-30), DueDate = now.Date.AddDays(-16), ReturnedAt = now.AddDays(-12), FineAmount = 4 * LibraryRules.FinePerLateDay, FinePaid = true },
                // Returned on time, no fine.
                new Loan { ItemId = nineteenEightyFour.Id, BorrowerId = david.Id, BorrowedAt = now.AddDays(-8), DueDate = now.Date.AddDays(6), ReturnedAt = now.AddDays(-2), FineAmount = 0, FinePaid = false },
                // Returned late, fine still outstanding.
                new Loan { ItemId = abbeyRoad.Id, BorrowerId = priya.Id, BorrowedAt = now.AddDays(-25), DueDate = now.Date.AddDays(-11), ReturnedAt = now.AddDays(-8), FineAmount = 3 * LibraryRules.FinePerLateDay, FinePaid = false }
            );
            await db.SaveChangesAsync();
        }
    }

    private static async Task AddStaffUserAsync(UserManager<ApplicationUser> userManager, string username, string password, string role)
    {
        var existing = await userManager.FindByNameAsync(username);
        if (existing != null) return;

        var user = new ApplicationUser { UserName = username };
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
            await userManager.AddToRoleAsync(user, role);
    }
}
