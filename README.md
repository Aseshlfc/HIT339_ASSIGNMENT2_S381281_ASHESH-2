# Simple Library Management System

ASP.NET Core MVC + Entity Framework Core + SQLite implementation of a simple library assignment.

## Requirements covered

### 1. Admin account
- CRUD for Books, Music and Toys.
- `Item` is an abstract superclass.
- `Book`, `Music` and `Toy` inherit from `Item`.
- Common fields: Name, Description, Library Code and Status.
- Statuses: Available, Borrowed, Damaged, Destroy.
- Book extra fields: Author, Genre, Publication Year.
- Music extra fields: Artist, Genre, Release Year.
- Toy extra fields: Toy Type, Minimum Age, Manufacturer.
- Staff account management: view, create, edit, disable/re-enable and delete Admin/Reception/Manager accounts, with safety guards preventing the last remaining Admin from being deleted, disabled or having their role changed away, and preventing an Admin from deleting or disabling their own account.

### 2. Reception account
- CRUD borrower details.
- Borrow an item using its library code and selected borrower.
- 14-day loan period.
- Return items and calculate late fines automatically.
- Late fine is $1.00 per overdue day.
- Returned items can be marked Available, Damaged or Destroy.
- Outstanding fine can be marked paid.

### 3. Manager account
- Borrowing statistics.
- Item status statistics.
- Item type statistics.
- Fine statistics: total, paid and outstanding.
- Top 10 most borrowed items.

### 4. Public responsive portal
- No login required.
- Search by name, description or library code.
- Filter by Books, Music or Toys.
- Optional "available only" filter.
- Responsive layout for phone and desktop.

## Demo staff accounts

| Role | Username | Password |
|---|---|---|
| Admin | `admin` | `Admin123!` |
| Reception | `reception` | `Reception123!` |
| Manager | `manager` | `Manager123!` |

These passwords are for a classroom/demo project. Change them for real use.

## Run

Install the .NET 8 SDK, then from this folder run:

```bash
dotnet restore
dotnet run
```

Open the address printed in the terminal (the included launch profile uses `http://localhost:5190`).

The SQLite database `library.db` is created via EF Core migrations on first run (`dotnet ef database update`, or automatically via `Database.Migrate()` at startup) and seeded with the three staff accounts, a 20-item catalogue (books/music/toys), 5 demo borrowers and 6 demo loans.

## Tests

Automated tests live in `Tests/` (xUnit). Most tests use the EF Core InMemory provider, but the concurrency test deliberately uses a real temporary SQLite database, since the InMemory provider does not enforce optimistic concurrency tokens. Run them from the project root with:

```bash
dotnet test Tests/LibraryManagementSystem.Tests.csproj
```

## Important files

- `Models/Item.cs` - superclass and item status enum.
- `Models/Book.cs`, `Models/Music.cs`, `Models/Toy.cs` - inherited item classes.
- `Controllers/ItemsController.cs` - Admin item CRUD.
- `Controllers/BorrowersController.cs` - Reception borrower CRUD.
- `Controllers/LoansController.cs` - borrow, return and fine workflow.
- `Controllers/ManagerController.cs` - statistics dashboard.
- `Controllers/HomeController.cs` - public search portal.
- `Data/LibraryContext.cs` - EF Core database setup using table-per-hierarchy inheritance.
- `Models/LibraryRules.cs` - loan days and fine rate.

## Database design summary

- `Items` stores all inherited item types using EF Core table-per-hierarchy inheritance and an item discriminator.
- `Borrowers` stores patron information.
- `Loans` links one borrower to one item and records borrowing, due date, return date and fine.
- Staff accounts and roles use ASP.NET Core Identity (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, etc.), created and applied via EF Core migrations.

## Suggested assignment demonstration

1. Open public search on a phone-sized browser window.
2. Login as Admin and create one Book, one Music item and one Toy.
3. Login as Reception, create a borrower and borrow an item using its library code.
4. Show that its status changes from Available to Borrowed.
5. Return it and demonstrate the item condition/status choice.
6. For a late-fine demonstration, return one of the seeded loans that is already overdue rather than modifying the database.
7. Login as Manager and show the borrowing, item status and fine statistics.
