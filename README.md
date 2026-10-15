# Simple Library — Multi-Branch Library Management System

HIT339 group project. An ASP.NET Core MVC library management system, extended from a single-location Assignment 2 application into a multi-branch system with a self-service kiosk, a public REST API, bulk import, reservations and notifications.

> **Status.** This README describes the combined application: the Assignment 2 baseline plus Section 1 (multi-branch inventory, transfers, manager analytics), Section 2 (kiosk, REST API, CSV import) and Section 3 (reservations, waitlist, notifications), with the integration fixes listed under [Integration notes](#integration-notes). The project targets **.NET 10** (`net10.0`); it was upgraded from .NET 8 during integration.

## Contents

1. [Technology stack](#technology-stack)
2. [Development requirements](#development-requirements)
3. [Quick start](#quick-start)
4. [Repository structure](#repository-structure)
5. [Configuration](#configuration)
6. [Database, migrations and seeding](#database-migrations-and-seeding)
7. [Roles and demo accounts](#roles-and-demo-accounts)
8. [Baseline: Assignment 2 requirements](#baseline-assignment-2-requirements)
9. [Section 1: Multi-branch inventory and manager analytics](#section-1-multi-branch-inventory-and-manager-analytics)
10. [Section 2: Public kiosk, REST API and data import](#section-2-public-kiosk-rest-api-and-data-import)
11. [Section 3: Reservations and notifications](#section-3-reservations-and-notifications)
12. [Integration notes](#integration-notes)
13. [Testing](#testing)
14. [Suggested demonstration](#suggested-demonstration)
15. [GitHub workflow](#github-workflow)
16. [Known limitations](#known-limitations)
17. [Further documentation](#further-documentation)

## Technology stack

| Layer | Technology |
|---|---|
| Web framework | ASP.NET Core MVC, .NET 10 (`net10.0`) |
| Data access | Entity Framework Core 10.0.13 |
| Database | SQLite (`library.db`) |
| Authentication | ASP.NET Core Identity, cookie sign-in, role-based authorisation |
| Public API | ASP.NET Core controllers, API key header, built-in rate limiter |
| PDF generation | QuestPDF 2026.9.1 (Community licence) |
| Front end | Razor views, hand-written stylesheets (`wwwroot/css/site.css`, `wwwroot/css/kiosk.css`), Bootstrap Icons from a CDN |
| Tests | xUnit, EF Core InMemory, temporary SQLite files, PdfPig (reads PDFs in tests) |

No NuGet packages were added for Sections 2 and 3: the CSV reader and the rate limiting use what ships with .NET.

## Development requirements

- .NET 10 SDK (10.0.100 or later). `global.json` asks for it, so an older SDK stops with a clear message instead of a confusing build error. Check with `dotnet --list-sdks`.
- Git
- Windows or macOS
- Optional: Visual Studio 2026, or Visual Studio Code with the C# Dev Kit (Visual Studio 2022 cannot target .NET 10); `dotnet-ef` 10.x for working with migrations

## Quick start

```bash
git clone https://github.com/Aseshlfc/HIT339_ASSIGNMENT2_S381281_ASHESH-2.git
cd HIT339_ASSIGNMENT2_S381281_ASHESH-2
dotnet restore
dotnet run
```

Open the address printed in the terminal (the included launch profile uses **http://localhost:5190**). The database is migrated and seeded automatically on start-up.

### Windows (including the designated VM)

Run these in PowerShell.

1. **Install the .NET 10 SDK.** Run `winget install Microsoft.DotNet.SDK.10`, or use the installer from dotnet.microsoft.com. Open a **new** PowerShell window, then check that `dotnet --list-sdks` lists a `10.0.x` SDK. Any 10.0 SDK from 10.0.100 up works (`global.json`).
2. **Get the code and build it:**
   ```powershell
   git clone https://github.com/Aseshlfc/HIT339_ASSIGNMENT2_S381281_ASHESH-2.git
   cd HIT339_ASSIGNMENT2_S381281_ASHESH-2
   dotnet restore
   dotnet build
   ```
3. **Run the automated tests:** `dotnet test Tests\LibraryManagementSystem.Tests.csproj`. Every test should pass. The project path is required.
4. **Choose a database (optional).** To test without touching `library.db`, point the app at a new file first: `$env:ConnectionStrings__DefaultConnection="Data Source=$PWD\vm-test.db"`. Migrations run automatically at start-up. To apply them by hand instead, see [Database, migrations and seeding](#database-migrations-and-seeding).
5. **Start the site:** run `dotnet run` and open **http://localhost:5190**.

If the build says the SDK in `global.json` was not found, the .NET 10 SDK is missing, or the window was opened before it was installed.

What to check by hand on Windows is listed in [`docs/WINDOWS_VM_CHECKLIST.md`](docs/WINDOWS_VM_CHECKLIST.md).

## Repository structure

```
LibraryManagementSystem.csproj   Application project (repository root; there is no .sln file)
Program.cs                       Start-up: services, authentication, rate limiting, migration and seeding
appsettings.json                 Connection string, logging, public API and kiosk settings
Api/                             Public API options, API key filter, response shapes, kiosk options
Controllers/                     MVC controllers and the API controller
Data/                            LibraryContext (EF Core), DbSeeder, staff-branch and item-code lookups
Migrations/                      EF Core migrations and model snapshot
Models/                          Entities: Item (Book, Music, Toy), Borrower, Loan, Branch, ItemTransfer,
                                 Reservation, Notification, ApplicationUser
Services/                        Transfers, reports (CSV and PDF), kiosk, CSV import, reservations,
                                 notifications, due-date reminders
ViewComponents/                  Header chip showing the signed-in user's branch
ViewModels/                      Form and page models
Views/                           Razor views, one folder per controller
wwwroot/css/                     Stylesheets
Tests/                           xUnit test project (LibraryManagementSystem.Tests.csproj)
docs/                            Section documentation, screenshots and sample import files
PROGRAMMERS_LOG.md               Development log from Assignment 2
library.db                       SQLite database (Assignment 2 data; upgraded on first run)
```

## Configuration

Settings live in `appsettings.json` and can be overridden with environment variables (replace `:` with `__`).

| Setting | Purpose | Default |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | SQLite database file | `Data Source=library.db` |
| `PublicApi:ApiKeys` | Keys accepted by the REST API. With none configured the API is switched off | one demonstration key |
| `PublicApi:RequestsPerMinute` | API requests allowed per client address per minute | 60 |
| `PublicApi:DefaultPageSize`, `PublicApi:MaxPageSize` | API paging | 20, 100 |
| `Kiosk:RequestsPerMinute` | Kiosk card look-ups and checkouts allowed per client address per minute | 30 |
| `Kiosk:SessionIdleMinutes`, `Kiosk:SessionMaxMinutes` | A kiosk session ends on the server after this long without a request, or this long in total | 3, 20 |
| `Kiosk:SignInFormMinutes` | How long one kiosk welcome screen can be used to sign in | 60 |

The API key in `appsettings.json` is a placeholder for classroom demonstration. Replace it before any real use, for example with the environment variable `PublicApi__ApiKeys__0`. Do not commit real keys.

## Database, migrations and seeding

Migrations run automatically at start-up. To apply them by hand:

```bash
dotnet tool install --global dotnet-ef --version 10.0.13
dotnet ef database update
```

To check that the code and the migrations agree, run `dotnet ef migrations has-pending-model-changes`; it should report no changes. With the .NET 10 SDK, `dnx dotnet-ef@10.0.13 -- database update` runs the tool without installing it.

| Migration | Section | Adds |
|---|---|---|
| `AddAspNetCoreIdentity` | Baseline | Initial schema: items, borrowers, loans, Identity tables |
| `AddItemStatusConcurrencyToken` | Baseline | Concurrency check on item status (no schema change) |
| `AddBranchesAndTransfers` | 1 | `Branches`, `ItemTransfers`; `BranchId` on items, staff and loans |
| `AddItemMovementKind` | 1 | `Kind` on `ItemTransfers`; assigns existing Reception accounts to the default branch |
| `AddNotifications` | 3 | `Notifications` |
| `AddReservations` | 3 | `Reservations` |
| `AddKioskPin` | 2 (integration) | `KioskPinHash`, `KioskPinSetAtUtc`, `KioskPinFailedAttempts`, `KioskPinLockedUntilUtc` on `Borrowers`. Existing borrowers start with no PIN |

The library card number is not stored: it is derived from the borrower Id (for example `P0007`). The kiosk PIN is stored only as a salted hash.

**Database design summary**

- `Items` stores all item types using EF Core table-per-hierarchy inheritance and an item discriminator. `Item` is an abstract superclass; `Book`, `Music` and `Toy` inherit from it.
- `Borrowers` stores patron information, and the kiosk PIN hash, lockout counter and lockout time.
- `Loans` links one borrower to one item and records borrowing, due date, return date, fine and issuing branch.
- `Branches` and `ItemTransfers` record where each item is and every change of location.
- `Reservations` holds the queue for each item; `Notifications` stores every simulated email or SMS.
- Staff accounts and roles use ASP.NET Core Identity (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles` and related tables).

**Seeding.** On start-up the application creates anything missing: the three roles, one demo account per role, two demo branches beside the default Central Library, and — only when the tables are empty — 20 items, 5 borrowers and 6 loans.

**Existing data.** The `library.db` in the repository is the Assignment 2 database. On first run it is upgraded in place: all records are kept and every item is assigned to Central Library. Delete `library.db` before the first run to start from an empty database with the demo catalogue spread across three branches.

When adding a migration, merge the latest `main` first. See the [GitHub workflow](#github-workflow).

## Roles and demo accounts

| Role | Demo username | Access |
|---|---|---|
| Admin | `admin` | Items, CSV import, staff accounts, branches (full), transfers (any branch), kiosk PINs (Borrowers page; cannot add, edit or delete borrowers) |
| Reception | `reception` | Loans, borrowers (including kiosk PINs), reservations, notifications, transfers for its own branch, branches (view) |
| Manager | `manager` | Dashboard, reports and exports, branches (view) |
| Public | no login | Catalogue search, self-service kiosk (library card number and 6-digit PIN) |
| External application | API key | Read-only REST API |

The demonstration passwords are not published in this repository's documentation. They are displayed on the Staff Login page and are set in `Data/DbSeeder.cs`. They exist for classroom demonstration only; change them before any real use.

## Baseline: Assignment 2 requirements

**Admin account**
- CRUD for Books, Music and Toys.
- Common fields: Name, Description, Library Code and Status.
- Statuses: Available, Borrowed, Damaged, Destroy (and In transit, set by the transfer workflow).
- Book extra fields: Author, Genre, Publication Year. Music: Artist, Genre, Release Year. Toy: Toy Type, Minimum Age, Manufacturer.
- Staff account management: view, create, edit, disable/re-enable and delete Admin/Reception/Manager accounts, with safety guards preventing the last remaining Admin from being deleted, disabled or having their role changed away, and preventing an Admin from deleting or disabling their own account.

**Reception account**
- CRUD borrower details.
- Borrow an item using its library code and selected borrower. 14-day loan period.
- Return items and calculate late fines automatically. Late fine is $1.00 per overdue day (`Models/LibraryRules.cs`).
- Returned items can be marked Available, Damaged or Destroy.
- Outstanding fine can be marked paid.

**Manager account**
- Borrowing, item status, item type and fine statistics; top 10 most borrowed items.

**Public responsive portal**
- No login required. Search by name, description or library code; filter by Books, Music or Toys; optional "available only" filter; responsive layout for phone and desktop.

## Section 1: Multi-branch inventory and manager analytics

Full notes: [`docs/SECTION1_MULTIBRANCH_ANALYTICS.md`](docs/SECTION1_MULTIBRANCH_ANALYTICS.md).

| Module | Where | Summary |
|---|---|---|
| Public catalogue search | `HomeController` | Search by name, description or library code; filter by type, branch and availability |
| Items | `ItemsController` | Create, view, edit and delete books, music and toys. The branch is chosen when an item is created |
| Borrowers | `BorrowersController` | Create, edit and delete borrowers |
| Loans | `LoansController` | Borrow by library code (14-day loan), return, late fine of $1.00 per day, mark fine paid |
| Staff | `StaffController` | Create, edit, disable and delete staff accounts; assign role and branch |
| Branches | `BranchesController` | Create, edit, deactivate and delete branches; view each branch's inventory, staff and open transfers |
| Transfers | `TransfersController`, `TransferService` | Move items between branches |
| Manager analytics | `ManagerController`, `ReportService` | Dashboard, three reports, CSV and PDF export |

**Multi-branch functionality**

- Every item belongs to one branch. Existing items were assigned to Central Library by the migration.
- A Reception account belongs to a branch and can lend only items held there. A Reception account with no branch cannot lend or accept returns.
- Each new loan records its issuing branch. Loans made before the upgrade show "Not recorded".
- An existing item's branch is not edited directly. It changes in three recorded ways: a transfer, an Admin location correction (reason required), or a return at another branch's desk.

**Transfers**

| Step | Who | Effect |
|---|---|---|
| Request | Admin, or Reception for an item from or to their branch | Transfer created; item stays available |
| Dispatch | Source branch | Item becomes In transit and cannot be borrowed |
| Receive | Destination branch | Item's branch changes to the destination; item becomes available |
| Cancel / Reject | Either branch / source branch | Item remains at, or returns to, the source |

Only available items with no open loan, no open transfer and no active reservation can be transferred. A transfer cannot be completed twice.

**Analytics and reporting**

Manager → Statistics. Reports: Borrowing statistics (date range, branch, category), Fine revenue audit (date range, branch, category, paid/outstanding) and Inventory health (branch, category). Each downloads as CSV (UTF-8, spreadsheet-safe) or PDF using the filters on the page. PDF text outside the report font (for example Japanese, Chinese, Korean or Devanagari) is drawn with fonts installed on the machine running the application.

## Section 2: Public kiosk, REST API and data import

Author: Susan Acharya. Full notes: [`docs/SECTION2_NOTES.md`](docs/SECTION2_NOTES.md). Section 2 itself needed no migration; the kiosk PIN added during integration does (`AddKioskPin`).

### Kiosk (no login)
- **Where:** the **Kiosk** link in the menu, or `/kiosk/CEN` (Central), `/kiosk/NTH`, `/kiosk/RIV`.
- **How:** type or scan a library card number (the **Borrowers** page shows each one, for example `P0005`), then type the patron's 6-digit PIN. The kiosk shows loans, fines and reservations. Scan or type an item code (for example `B003`) and tap **Borrow**.
- **Rules:** same as the desk: only Available items held at that branch, items kept for another patron's hold are refused, loans are due in 14 days, and a Borrowed notification is sent. The kiosk signs out after 60 seconds idle.
- **Sessions:** a recognised card starts a short server-side session (`Services/KioskSessionStore.cs`). The browser only holds a random id in an HttpOnly, SameSite=Strict cookie limited to `/kiosk`. Signing out, the idle timer and the welcome screen end the session on the server. Each welcome screen carries a one-time sign-in token, so a resubmitted or replayed sign-in form is refused.
- **PINs:** a patron cannot use the kiosk until Reception or Admin sets a PIN on the **Borrowers** page. PINs are exactly 6 digits (leading zeros count) and stored only as a salted hash (ASP.NET Core Identity's password hasher). The Borrowers page shows only whether a PIN is set or locked. Five wrong PINs lock the card at the kiosk for 15 minutes; setting a new PIN unlocks it and ends any kiosk session the patron has open. There are no demo PINs: set one for a demo borrower from the Borrowers page.
- **Limits:** card and PIN attempts and checkouts are limited per client address (see [Configuration](#configuration)).

### REST API (read-only, API key)
- **Endpoints:** `GET /api/v1/items`, `/api/v1/items/{libraryCode}`, `/api/v1/categories`, `/api/v1/status`.
- **Try it** (use a key configured under `PublicApi:ApiKeys`):

```bash
curl -H "X-Api-Key: <your-api-key>" "http://localhost:5190/api/v1/items?type=toy&availableOnly=true"
```

- Needs the `X-Api-Key` header (change keys in `appsettings.json`; keys in the URL are not accepted), allows 60 requests per minute per client and GET only. `status` must be a name (Available, Borrowed, Damaged or InTransit). Items written off (Destroy) are never listed. Full reference: [`docs/SECTION2_API.md`](docs/SECTION2_API.md).

### CSV import (Admin)
1. Log in as the Admin demo account and open **Import**.
2. Click **Download template**, or use `docs/samples/items-import-valid.csv`.
3. Upload with **Check the file only** ticked to see a line-by-line report without saving.
4. Upload again without it ticked to import. Try `docs/samples/items-import-with-errors.csv` to see the validation messages.

Files are limited to 1 MB and 1000 rows, UTF-8. Import adds items only; it does not update existing ones.

### Documents
[`docs/SECTION2_REQUIREMENTS.md`](docs/SECTION2_REQUIREMENTS.md), [`docs/SECTION2_USER_GUIDE.md`](docs/SECTION2_USER_GUIDE.md), [`docs/SECTION2_API.md`](docs/SECTION2_API.md), [`docs/SECTION2_USER_JOURNEYS.md`](docs/SECTION2_USER_JOURNEYS.md), [`docs/SECTION2_NOTES.md`](docs/SECTION2_NOTES.md). Screenshots are in `docs/section2-images`.

### Tests
Section 2 tests are in `Tests/KioskServiceTests.cs`, `Tests/KioskControllerTests.cs`, `Tests/KioskSessionTests.cs`, `Tests/KioskPinTests.cs`, `Tests/CatalogueApiTests.cs` and `Tests/ItemImportServiceTests.cs`.

## Section 3: Reservations and notifications

Author: Milan. Build log: [`docs/SECTION3_NOTES.md`](docs/SECTION3_NOTES.md). All pages below are for the **Reception** demo account.

### Notifications
- **Where:** Notifications in the menu. It lists every simulated email or SMS, newest first, with a filter by type.
- **How it works:** every message goes through `Services/NotificationService.cs`. It saves a row to the `Notifications` table and writes a line to the console. It uses Email when the borrower has an email address, SMS when there is only a phone number, and skips borrowers with neither.
- **When messages are sent:**
  - **Borrowed:** after a successful borrow, at the desk or the kiosk.
  - **FineIssued:** when a return creates a late fine.
  - **DueSoon:** when staff click **Run due-date check** on the Notifications page. It reminds borrowers whose unreturned loans are due within 2 days, and does not send the same reminder twice.
  - **ReservationReady:** when an item becomes available and a borrower is first in its queue.
  - **ReservationExpired:** when a Ready hold is not collected in time.
  - **ReservationCancelled:** when an item with holds is written off (Destroy).
  - **ReservationDelayed:** when an item being kept for a borrower stops being available (for example it is marked Damaged).

### Reservations
- **Where:** Reservations in the menu, then **Place Hold**. The list shows the branch that physically holds each item.
- **Rules:** a hold can only be placed on a Borrowed or Damaged item. It is refused for Available, Destroy and In-transit items, for a borrower who already has an active hold on the item, and for a borrower who currently has the item on loan.
- **Queue:** holds are served in the order they were placed. The Reservations page shows each waiting borrower's queue position. Waiting and Ready holds can be cancelled; cancelling a Ready hold offers the item to the next borrower.
- **When an item becomes available** (returned by Reception, set to Available by an Admin after repair, or back on the shelf after a transfer is received or cancelled), the first Waiting hold becomes **Ready** and that borrower is notified.
- **Ready holds are protected:** only that borrower can borrow the item, at the desk or the kiosk, and the hold becomes **Fulfilled** when they do. A reserved item cannot be transferred to another branch.
- **Expiry:** a Ready hold not collected within 3 days expires when staff click **Expire old holds** on the Reservations page. The borrower is told, and the next person in the queue is offered the item. The 3 days only run while the item is available to collect.

### How to try it
1. Log in as the Reception demo account. Open **Reservations**, then **Place Hold**.
2. Place a hold on a Borrowed item (for example `B008`) for a borrower who does not have it.
3. Open **Loans** and return that item. The hold becomes **Ready**.
4. Open **Notifications** to see the **ReservationReady** message.
5. Try borrowing that item for a different borrower. It is refused. Borrow it for the borrower with the hold, and the hold disappears from the list because it is now Fulfilled.

The seed data has no loan due within 2 days, so the due-date reminder and hold expiry are demonstrated by automated tests. The buttons themselves can be clicked and report that nothing needed to be sent.

### Tests
Section 3 tests are in `Tests/NotificationAndQueueTests.cs`, `Tests/DueDateReminderServiceTests.cs` and `Tests/ReservationExpiryTests.cs`. Screenshots of the working features are in `docs/screenshots`.

## Integration notes

Changes made when the three sections were combined. Regression tests are in `Tests/IntegrationFixTests.cs`.

| Area | Change |
|---|---|
| Reservations and transfers | An item with an active hold cannot be requested or dispatched for transfer. Receiving or cancelling a transfer offers the item to the first waiting borrower |
| Borrowing | The desk and the kiosk share one hold rule (`IReservationService.CheckBorrowAsync`): an item with a queue can only be borrowed by the borrower it is held for |
| Written-off items | Returning or editing an item to Destroy cancels its holds and notifies those borrowers |
| Ready holds | Can be cancelled. If the item stops being available, the hold returns to the front of the queue and gets a new collection period when the item is available again |
| Library codes | The desk, the hold form and the kiosk all accept a code typed in lower case or with spaces before or after it (`Data/ItemLookupExtensions.cs`) |
| Kiosk | Card look-ups and checkouts are rate limited |
| Kiosk sign-out | After signing out, the welcome screen says so. **Back to Main Site** is on every kiosk screen where nobody is signed in (branch choice, welcome, branch not found), but never on the account screen |
| .NET 10 | Upgraded from .NET 8: `net10.0`, EF Core and Identity 10.0.13, `global.json` (SDK 10.0.100 or later). No code or migration changes were needed: `dotnet ef migrations has-pending-model-changes` reports none. EF Core 10 adds a `__EFMigrationsLock` table on first start and logs a warning that `AddBranchesAndTransfers` contains `PRAGMA foreign_keys = 0`, which SQLite cannot run inside a transaction. EF Core 8 ran the same step without the warning |
| Kiosk PIN | Sign-in needs the card number and a 6-digit PIN set by Reception or Admin (`Services/KioskPinService.cs`, migration `AddKioskPin`). Unknown card and wrong PIN give the same message; 5 wrong PINs lock the card for 15 minutes; a PIN change ends the patron's kiosk sessions. Editing a borrower can no longer touch the PIN fields. Admin can open the Borrowers page for PINs only. Tests: `Tests/KioskPinTests.cs` |
| Kiosk sessions | Sign-in now creates a server-side session (Post/Redirect/Get). Checkout takes the patron from the session, not from a hidden card-number field. Finish, the idle timer, the welcome screen and server-side expiry end the session. Sign-in forms work once, so Back, refresh, resubmission and replaying the old sign-in post cannot reopen an account. Kiosk pages are sent `no-store`, and the account page shows only the last two digits of the card. Tests: `Tests/KioskSessionTests.cs` |
| REST API | Numeric or unknown `status` values return 400 |
| CSV import | A file over 1 MB is answered with the importer's own message |
| PDF export | Names in non-Latin scripts no longer fail the export and are drawn with installed fonts |
| Layout | The menu has its own row for roles with many links and scrolls sideways on a phone; the Import form fits a 320 px screen |

## Testing

```bash
dotnet test Tests/LibraryManagementSystem.Tests.csproj
```

The project path is required; running `dotnet test` alone from the root builds the application but runs no tests.

Most tests use the EF Core InMemory provider. Tests for concurrency, unique indexes and decimal totals use a temporary SQLite file, because the InMemory provider does not reproduce that behaviour.

## Suggested demonstration

1. Open public search on a phone-sized browser window.
2. Log in as Admin and create one Book, one Music item and one Toy, or import several with **Import**.
3. Log in as Reception, create a borrower and borrow an item using its library code.
4. Show that its status changes from Available to Borrowed, and the Borrowed message under **Notifications**.
5. Place a hold on it for another borrower, then return it and show the hold become Ready.
6. For a late-fine demonstration, return one of the seeded loans that is already overdue rather than modifying the database.
7. Request, dispatch and receive a transfer between two branches.
8. Open the **Kiosk**, enter a card number and borrow an item.
9. Log in as Manager and show the dashboard, the three reports and their CSV and PDF exports.
10. Call the REST API with an API key.

## GitHub workflow

- `main` holds integrated, working code and changes only through pull requests.
- Work is done on `feature/<description>` or `fix/<description>` branches created from the latest `main`.
- Before a pull request: merge `origin/main` into the branch, build, and run the full test suite.
- Do not commit `bin/`, `obj/` or `library.db`. They are tracked from the original commit and show as modified after every build; stage named source paths instead of `git add .`.
- Migrations: merge `main` before creating one. If another migration reaches `main` first, remove yours, merge, and create it again.

## Known limitations

- The .NET 10 upgrade was built and tested on macOS only (SDK 10.0.402, runtime 10.0.13, Chrome). It has not yet been tested on Windows, on the designated virtual machine, or in Edge or Firefox. See `docs/WINDOWS_VM_CHECKLIST.md`.
- Staff sign-in does not lock an account after repeated wrong passwords (Assignment 2 behaviour).
- A borrower's address is required, although the form does not mark it as required (Assignment 2 behaviour).
- The tests use xUnit 2, which NuGet now lists as legacy in favour of xUnit v3. It still works on .NET 10.
- **Kiosk PINs are short.** A 6-digit PIN with a 15-minute lockout after 5 wrong tries is a usability compromise: it stops casual guessing, not a determined attacker who can watch a patron type it. Anyone who knows a card number can also lock that card for 15 minutes by typing wrong PINs. If the database were copied, 6-digit PINs could be recovered from their hashes by trying every combination, so the database must be protected like any credential store.
- Patrons cannot set or change their own PIN; staff set it at the desk.
- Kiosk sessions are held in memory: they suit one server instance, and a restart signs every kiosk out.
- Fines cannot be paid, items cannot be returned and holds cannot be placed at the kiosk.
- API keys live in configuration; there is no key management page. The rate limiters are in memory, which suits one server instance.
- CSV import adds items only (no update) and accepts UTF-8 only.
- Notifications are simulated: they are stored and written to the console, not sent.
- Due-date reminders and hold expiry are started by staff buttons, not by a scheduled job.
- PDF reports draw non-Latin text with fonts installed on the machine. A character no installed font contains appears as a placeholder box.
- Transfers move one item at a time. An item returned at another branch stays there until a transfer is requested.
- Fines record a paid/unpaid flag only; there are no payment dates or transactions.
- An Admin can still set an item's status by hand (baseline behaviour). The Inventory health report flags any item whose status disagrees with its loan record.
- Reports aggregate in memory, which suits a library of this size.
- An unknown record ID returns a plain 404 response.
- `bin/`, `obj/` and `library.db` are tracked in Git.

## Further documentation

| Document | Contents |
|---|---|
| [`docs/SECTION1_MULTIBRANCH_ANALYTICS.md`](docs/SECTION1_MULTIBRANCH_ANALYTICS.md) | Section 1 design, business rules, testing, integration notes for Sections 2 and 3 |
| [`docs/SECTION2_REQUIREMENTS.md`](docs/SECTION2_REQUIREMENTS.md) | Section 2 requirements |
| [`docs/SECTION2_USER_GUIDE.md`](docs/SECTION2_USER_GUIDE.md) | Kiosk, API and import user guide |
| [`docs/SECTION2_API.md`](docs/SECTION2_API.md) | REST API reference |
| [`docs/SECTION2_USER_JOURNEYS.md`](docs/SECTION2_USER_JOURNEYS.md) | Section 2 user journey maps |
| [`docs/SECTION2_NOTES.md`](docs/SECTION2_NOTES.md) | Section 2 decisions, shared files and known limitations |
| [`docs/SECTION3_NOTES.md`](docs/SECTION3_NOTES.md) | Section 3 build log |
| [`PROGRAMMERS_LOG.md`](PROGRAMMERS_LOG.md) | Assignment 2 development log |

The group's project-management documents (project plan, task allocation, repository management and installation guide) are kept on a separate documentation branch and are not part of this branch yet.
