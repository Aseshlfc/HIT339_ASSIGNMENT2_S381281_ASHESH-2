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


## Section 3: Reservations and Notifications

All pages below are for the **Reception** account (`reception` / `Reception123!`).

### Notifications
- **Where:** Notifications in the menu. It lists every simulated email or SMS, newest first, with a filter by type.
- **How it works:** every message goes through `Services/NotificationService.cs`. It saves a row to the `Notifications` table and writes a line to the console. It uses Email when the borrower has an email address, SMS when there is only a phone number, and skips borrowers with neither.
- **When messages are sent:**
  - **Borrowed:** after a successful borrow.
  - **FineIssued:** when a return creates a late fine.
  - **DueSoon:** when staff click **Run due-date check** on the Notifications page. It reminds borrowers whose unreturned loans are due within 2 days, and does not send the same reminder twice.
  - **ReservationReady:** when an item becomes available and a borrower is first in its queue.
  - **ReservationExpired:** when a Ready hold is not collected in time.

### Reservations
- **Where:** Reservations in the menu, then **Place Hold**.
- **Rules:** a hold can only be placed on a Borrowed or Damaged item. It is refused for Available, Destroy and In-transit items, for a borrower who already has an active hold on the item, and for a borrower who currently has the item on loan.
- **Queue:** holds are served in the order they were placed. The Reservations page shows each waiting borrower's queue position, and Waiting holds can be cancelled.
- **When an item becomes available** (returned by Reception, or set to Available by an Admin after repair), the first Waiting hold becomes **Ready** and that borrower is notified.
- **Ready holds are protected:** only that borrower can borrow the item, and the hold becomes **Fulfilled** when they do.
- **Expiry:** a Ready hold not collected within 3 days expires when staff click **Expire old holds** on the Reservations page. The borrower is told, and the next person in the queue is offered the item.

### How to try it
1. Log in as `reception`. Open **Reservations**, then **Place Hold**.
2. Place a hold on a Borrowed item (for example `B001`) for a borrower who does not have it.
3. Open **Loans** and return that item. The hold becomes **Ready**.
4. Open **Notifications** to see the **ReservationReady** message.
5. Try borrowing that item for a different borrower. It is refused. Borrow it for the borrower with the hold, and the hold disappears from the list because it is now Fulfilled.

The seed data has no loan due within 2 days, so the due-date reminder and hold expiry are demonstrated by automated tests (see below). The buttons themselves can be clicked and report that nothing needed to be sent.

### Tests
Run from the project root:

```bash
dotnet test Tests/LibraryManagementSystem.Tests.csproj
```

Section 3 tests are in `Tests/NotificationAndQueueTests.cs`, `Tests/DueDateReminderServiceTests.cs` and `Tests/ReservationExpiryTests.cs`. Screenshots of the working features are in `docs/screenshots`, and the build log is in `docs/SECTION3_NOTES.md`.

## Section 2: Public Kiosk, REST API and Data Import

Author: Susan Acharya. Full notes: `docs/SECTION2_NOTES.md`. No database migration is needed for this section.

### Kiosk (no login)
- **Where:** the **Kiosk** link in the menu, or `/kiosk/CEN` (Central), `/kiosk/NTH`, `/kiosk/RIV`.
- **How:** type or scan a library card number (the **Borrowers** page shows each one, for example `P0005` is Priya Patel). The kiosk shows loans, fines and reservations. Scan or type an item code (for example `B003`) and tap **Borrow**.
- **Rules:** same as the desk: only Available items held at that branch, items kept for another patron's hold are refused, loans are due in 14 days, and a Borrowed notification is sent. The kiosk signs out after 60 seconds idle.

### REST API (read-only, API key)
- **Endpoints:** `GET /api/v1/items`, `/api/v1/items/{libraryCode}`, `/api/v1/categories`, `/api/v1/status`.
- **Try it:**

```bash
curl -H "X-Api-Key: demo-public-api-key-change-me" "http://localhost:5190/api/v1/items?type=toy&availableOnly=true"
```

- Needs the `X-Api-Key` header (change keys in `appsettings.json`), allows 60 requests per minute per client and GET only. Full reference: `docs/SECTION2_API.md`.

### CSV import (Admin)
1. Log in as `admin` / `Admin123!` and open **Import**.
2. Click **Download template**, or use `docs/samples/items-import-valid.csv`.
3. Upload with **Check the file only** ticked to see a line-by-line report without saving.
4. Upload again without it ticked to import. Try `docs/samples/items-import-with-errors.csv` to see the validation messages.

### Documents
`docs/SECTION2_REQUIREMENTS.md`, `docs/SECTION2_USER_GUIDE.md`, `docs/SECTION2_API.md`, `docs/SECTION2_USER_JOURNEYS.md`, `docs/SECTION2_NOTES.md`.

### Tests
Section 2 tests are in `Tests/KioskServiceTests.cs`, `Tests/KioskControllerTests.cs`, `Tests/CatalogueApiTests.cs` and `Tests/ItemImportServiceTests.cs`. Run `dotnet test Tests/LibraryManagementSystem.Tests.csproj`.
