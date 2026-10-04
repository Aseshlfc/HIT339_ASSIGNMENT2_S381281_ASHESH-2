# Programmer's Log — Simple Library Management System

## Development approach and AI use

I scaffolded and built this project over multiple sessions. I set up the initial
project structure and architecture myself, using AI (initially ChatGPT and later
Claude Code) as a permitted programming assistant throughout the project.

AI was used for initial scaffolding and starter-code suggestions, interpreting
the assignment requirements, explaining ASP.NET Core concepts, reviewing code,
troubleshooting errors, suggesting improvements, and assisting with automated
and programmatic testing near the end of development. I did not treat
AI-generated output as a finished submission. I opened the project locally,
built and ran it, reviewed the code, made development decisions, performed
manual browser testing, accepted or rejected suggested changes, and
progressively refined the final application myself.

Where Claude Code performed automated or programmatic testing, this is stated
clearly in the relevant log entries rather than being presented as manual
testing completed by me.

Formal, contemporaneous logging (this document) began partway through that
process. Earlier work is summarised retrospectively below and clearly marked
as such.
---

### 2026-09-08 — Initial project scaffold and first working version

I began the Library Management System on
this date, creating the initial ASP.NET Core MVC project structure with AI
assistance based on the assignment brief. This matches the file-system
timestamps observed on the original project files during the first audit
session below (e.g. `Program.cs`, the original controllers, and `README.md`
all carried `8 Sep` modification dates before any Claude Code involvement).

The first working version established the main architecture:

- ASP.NET Core MVC targeting .NET 8.
- Entity Framework Core with SQLite.
- An abstract `Item` superclass with `Book`, `Music`, and `Toy` subclasses.
- Common item fields for name, description, library code, and status.
- `Borrower` and `Loan` models.
- Admin item CRUD.
- Reception borrower CRUD plus borrow/return workflow.
- Manager statistics.
- A public search page.
- Initial role-based access for Admin, Reception, and Manager.
- Centralised library rules for the 14-day loan period and $1.00/day late
  fine.

At this stage the project was a functional starting point rather than a
final submission: authentication was still a custom scheme rather than
ASP.NET Core Identity, the catalogue held only a small amount of seed data,
there were no automated tests, concurrent borrowing had not been addressed,
and the UI still needed refinement.

AI use: AI assistance helped generate the initial scaffold and starter
implementation from the assignment requirements. I then worked
with the generated project locally rather than submitting it unchanged.

### 2026-09-12 — Local environment setup and first run on Mac

I continued the work on my Mac, running the
application from Terminal.

Commands used included:
```
dotnet restore
dotnet run
```

`dotnet restore` and the build succeeded, but the application initially
would not run because the project targeted .NET 8 while only the .NET 9
runtime was available locally. I identified the missing .NET 8
runtime/SDK requirement and corrected the local environment before
continuing.

This was a useful checkpoint: it confirmed the project itself could build,
and that the run failure was a local runtime configuration issue rather
than a problem in the source code.

### 2026-09-13 — Validation review and manual checking before formal logging

Before the more substantial architectural work began (the Identity
migration audited in the first Session Entry below), I reviewed
form validation and improved the messages shown to users. By the start of
that first formal session, the project already had:

- An ASP.NET Core MVC project (`LibraryManagementSystem`, target `net8.0`)
  using Entity Framework Core 8.0.11 with the SQLite provider.
- An `Item` abstract base class with `Book`, `Music`, and `Toy` inheriting
  from it, mapped using EF Core Table-Per-Hierarchy (TPH) inheritance with a
  string discriminator column.
- A `Borrower` model, `Loan` model, and a `LibraryRules` static class
  centralising the 14-day loan period and $1.00/day late fine.
- A custom (non-Identity) authentication system: `AppUser` model with
  Username/PasswordHash/Role, hashed via `IPasswordHasher<AppUser>`, and
  cookie-based sign-in built manually in `AccountController` using
  `ClaimsPrincipal`/`ClaimTypes.Role`. `[Authorize(Roles = "...")]` attributes
  were already used correctly on `ItemsController` (Admin),
  `BorrowersController`/`LoansController` (Reception), and
  `ManagerController` (Manager).
- Full CRUD for items (Admin) and borrowers (Reception), a borrow/return
  workflow with fine calculation, and a Manager statistics dashboard
  (loan counts, item status/type counts, fine totals, top-10 popular items).
- A public search portal (`HomeController`) requiring no login, with
  responsive CSS (`wwwroot/css/site.css`, custom design system with CSS
  variables, cards, badges, and a mobile breakpoint at 720px).
- A conditional validation pattern for the shared `ItemFormViewModel`: base
  fields (`Name`, `Description`, `LibraryCode`) are `[Required]`, but
  subtype-specific fields (Author, BookGenre, Artist, ToyType, MinimumAge)
  are nullable on the view model and validated conditionally in
  `ItemsController.ValidateTypeSpecificFields` based on the selected item
  type, so creating a Book does not force Music/Toy fields to be filled in.
- `Borrower` validation via data annotations (`[EmailAddress]`, `[Phone]`,
  `[StringLength]`) checked through `ModelState.IsValid` in
  `BorrowersController`.

The following manual browser testing was reported as passed prior to this
session (manual testing, not automated tests):

- Empty item name — PASS
- Empty library code — PASS
- Empty Book author — PASS
- Empty Book genre — PASS
- Invalid Book publication year — PASS
- Empty Music artist — PASS
- Invalid Toy minimum age — PASS
- Invalid borrower email — PASS
- Empty borrower name — PASS
- Invalid borrower phone — PASS
- Borrower address exceeding maximum length — PASS

Files reviewed/touched during that earlier validation work: `Models/Item.cs`, `Models/Borrower.cs`, `Models/Book.cs`,
`Models/Music.cs`, `Models/Toy.cs`, `ViewModels/ItemFormViewModel.cs`,
`Views/Items/Form.cshtml`, `Views/Borrowers/Form.cshtml`,
`Controllers/ItemsController.cs`, `Controllers/BorrowersController.cs`.

Known limitations identified at this point, before formal logging began:

1. No automated tests.
2. Concurrent borrowing edge case not protected (two receptionists could
   both borrow the same item at nearly the same time).
3. UI functional but wanted further visual/usability refinement.
4. No Admin interface for staff account management (only 3 hardcoded seeded
   accounts).
5. Validation messages required review (later found to already be in good
   shape — see audit below).
6. Authentication used a custom scheme instead of ASP.NET Core Identity.

The project had not been placed under version control (Git) up to this
point.

---

## Session Entries

### 2026-09-13 — Project audit (pre-Identity-migration)

**Objective:** Full audit of the existing project before any architectural
changes (ASP.NET Core Identity migration), per assignment instructions not to
touch authentication/DbContext/database until the current implementation is
understood and a plan is approved.

**Files inspected:** `LibraryManagementSystem.csproj`, `Program.cs`,
`appsettings.json`, `Data/LibraryContext.cs`, `Data/DbSeeder.cs`,
`Models/AppUser.cs`, `Models/Item.cs`, `Models/Book.cs`, `Models/Music.cs`,
`Models/Toy.cs`, `Models/Borrower.cs`, `Models/Loan.cs`,
`Models/LibraryRules.cs`, `Controllers/AccountController.cs`,
`Controllers/ItemsController.cs`, `Controllers/BorrowersController.cs`,
`Controllers/LoansController.cs`, `Controllers/ManagerController.cs`,
`Controllers/HomeController.cs`, `ViewModels/ItemFormViewModel.cs`,
`ViewModels/BorrowViewModel.cs`, `ViewModels/LoginViewModel.cs`,
`ViewModels/ManagerDashboardViewModel.cs`, `ViewModels/ReturnViewModel.cs`,
`Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `README.md`, and the
SQLite database schema (via `sqlite3 library.db ".schema"`).

**Files changed:** None (audit only). Created `PROGRAMMERS_LOG.md`.

**What was found:**

- `dotnet build` succeeds cleanly: 0 warnings, 0 errors.
- The project is **not** a Git repository (`git status` returned
  `fatal: not a git repository`). Git has not been initialised. I had already decided not to initialise Git, so no Git action was taken.
- The database (`library.db`) was created via `Database.EnsureCreated()` in
  `Program.cs`, not via EF Core migrations. There is no `Migrations/` folder
  and no `__EFMigrationsHistory` table. This means introducing real EF Core
  migrations requires either a fresh initial migration against a recreated
  database, or a baseline migration against the existing schema.
- Confirmed via direct schema inspection that `Items` uses genuine EF Core
  TPH inheritance (single table, `ItemDiscriminator` column, nullable
  subtype-specific columns), matching the `Item`/`Book`/`Music`/`Toy` C#
  inheritance hierarchy. Unique index on `Items.LibraryCode` and
  `Users.Username` both confirmed present in the live schema.
- Confirmed authentication is a hand-rolled cookie scheme using a custom
  `AppUser` entity — not ASP.NET Core Identity. No Identity NuGet packages
  are referenced in the `.csproj`.
- Confirmed the previously-reported validation improvements are real and
  match the code: conditional per-subtype validation lives in
  `ItemsController.ValidateTypeSpecificFields`, and the shared
  `ItemFormViewModel` correctly leaves subtype fields nullable/optional at
  the annotation level so a Book does not require Music or Toy fields.
- No automated test project exists anywhere in the repository.
- `wwwroot/css/site.css` (94 lines) contains a real, working design system
  (CSS custom properties, card/badge/table/status styling, one mobile
  breakpoint) — not an empty stub.

**Problems/errors:** None. No build errors encountered during this
inspection-only session.

**Manual testing performed this session:** None (audit only; no functional
code was changed).

**Automated testing performed this session:** None (no test project exists
yet).

**Result:** PASS (build verified green, project structure and current
capabilities fully understood before proposing the Identity migration plan).

**Remaining issues (per known-limitations classification):**

| Limitation | Status |
|---|---|
| No automated tests | UNRESOLVED |
| Concurrent borrowing not protected | UNRESOLVED |
| UI needs visual/usability refinement | PARTIALLY RESOLVED (functional custom design system already exists; further polish still valuable) |
| No Admin staff-management interface | UNRESOLVED |
| Validation messages need review | RESOLVED (confirmed already implemented and manually tested correctly) |
| Authentication needs ASP.NET Core Identity | UNRESOLVED |

**Next step:** Present the audit, requirement matrix, and Identity migration
plan for my approval before making any change to
authentication, `LibraryContext`, `Program.cs`, or the database. Per
instructions, no architectural change will be made until that plan is
explicitly approved.

**References used:**
- None yet requiring citation (this entry is descriptive of the existing
  codebase only). Sources will be cited in later entries when they materially
  influence an implementation decision (expected: Microsoft Learn docs for
  ASP.NET Core Identity, `IdentityDbContext`, EF Core migrations, and the EF
  Core SQLite provider).

---

### 2026-09-13 — Manual backup + approval to proceed

**Objective:** Create a manual backup of the working project before starting
the Identity migration, since this project is not under version control and
I chose not to use Git for this assignment.

**Decision:** Do not initialise Git. Instead, create
a full manual copy of the project as a backup checkpoint before any Identity
or database changes.

**Commands run:**
```
cp -R ~/Downloads/Library ~/Downloads/Library-backup-before-identity
diff -rq ~/Downloads/Library ~/Downloads/Library-backup-before-identity --exclude=bin --exclude=obj
```

**Result:** Backup created successfully at
`~/Downloads/Library-backup-before-identity`. `diff -rq` (excluding `bin`/
`obj` build output) reported no content differences between the live project
and the backup — confirmed byte-for-byte identical source at backup time.
This backup will not be modified going forward; all further work happens
only in `~/Downloads/Library`.

**My decision on database approach:** Recreate `library.db` fresh
(delete the current file, generate a proper combined Identity + library EF
Core migration, run `dotnet ef database update`, reseed demo data and staff
accounts through the seeder) rather than attempting a more complex in-place
baseline migration, since the current database only contains seed/demo data.

**PASS/FAIL:** PASS — backup verified present and identical before proceeding.

**Next step:** Begin the approved Identity migration: add Identity NuGet
packages, create `ApplicationUser`, convert `LibraryContext` to
`IdentityDbContext<ApplicationUser>`, update `Program.cs`, building after
each step before moving on.

---

### 2026-09-13 — ASP.NET Core Identity migration implemented

**Objective:** Replace the custom cookie/AppUser authentication system with
real ASP.NET Core Identity, matching the professor's demonstrated
architecture (`ApplicationUser : IdentityUser`,
`LibraryContext : IdentityDbContext<ApplicationUser>`, EF Core migrations,
Admin/Reception/Manager roles), while preserving all existing library
entities, inheritance, relationships, and validation.

**Files changed:**
- `LibraryManagementSystem.csproj` — added
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 8.0.11 (matching the
  existing EF Core 8.0.11 packages).
- `Models/ApplicationUser.cs` — new, `ApplicationUser : IdentityUser` (kept
  minimal, no extra custom fields, per "do not over-engineer").
- `Models/AppUser.cs` — deleted (fully replaced by `ApplicationUser` /
  Identity's own `AspNetUsers` table).
- `Data/LibraryContext.cs` — changed base class from `DbContext` to
  `IdentityDbContext<ApplicationUser>`; removed the old `DbSet<AppUser> Users`
  and its unique-username index configuration (Identity manages its own
  unique username index internally). All existing configuration was
  preserved unchanged: TPH discriminator on `Item`/`Book`/`Music`/`Toy`,
  unique index on `Items.LibraryCode`, `Loan.FineAmount` precision, and
  `Restrict` delete behavior on both `Loan` foreign keys.
- `Data/DbSeeder.cs` — rewritten from a synchronous method taking
  `IPasswordHasher<AppUser>` to an async `SeedAsync` method taking
  `RoleManager<IdentityRole>` and `UserManager<ApplicationUser>`. Seeds the
  three roles (Admin/Reception/Manager) if missing, and the three demo staff
  accounts via `UserManager.CreateAsync` + `AddToRoleAsync` (idempotent —
  checks `FindByNameAsync` first). Demo item seeding logic unchanged.
- `Controllers/AccountController.cs` — rewritten to use
  `SignInManager<ApplicationUser>.PasswordSignInAsync` for login and
  `SignInManager.SignOutAsync` for logout, replacing the manual
  `IPasswordHasher`/`ClaimsPrincipal` construction. After a successful login,
  roles are read via `UserManager.GetRolesAsync` to preserve the existing
  per-role landing-page redirect behaviour (Admin → Items, Reception → Loans,
  Manager → Manager, else → Home).
- `Program.cs` — replaced `AddAuthentication().AddCookie()` +
  `AddScoped<IPasswordHasher<AppUser>>` with
  `AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<LibraryContext>().AddDefaultTokenProviders()`,
  plus `ConfigureApplicationCookie` to preserve the existing login path,
  access-denied path, and cookie name. Replaced `db.Database.EnsureCreated()`
  with `db.Database.Migrate()` and switched the startup seeding call to the
  new async `DbSeeder.SeedAsync(db, roleManager, userManager)`.

**Why the change was needed:** The assignment requires proper ASP.NET Core
Identity (`IdentityUser`, `IdentityDbContext`, EF Core migrations) matching
what the professor demonstrated, rather than a hand-rolled cookie/hasher
scheme. The existing `[Authorize(Roles = "...")]` attributes on
`ItemsController`, `BorrowersController`, `LoansController`, and
`ManagerController` required no changes at all — they already worked off the
`ClaimTypes.Role` claim, which Identity now populates automatically at
sign-in based on `AspNetUserRoles`.

**Problems/errors encountered:**
`dotnet build` failed immediately after deleting `Models/AppUser.cs` with
four `CS0246: The type or namespace name 'AppUser' could not be found`
errors, in `Data/DbSeeder.cs` (2 references) and
`Controllers/AccountController.cs` (2 references). This was the expected,
predicted failure from the audit (both files still depended on the old
`AppUser` type). Resolved by rewriting both files against
`ApplicationUser`/`UserManager`/`SignInManager` as described above. Rebuild
succeeded with 0 warnings, 0 errors.

**Database change:** With my approval, `library.db` was deleted and
recreated fresh (it contained only seed/demo data, no real user data). An EF
Core migration named `AddAspNetCoreIdentity` was generated with
`dotnet ef migrations add AddAspNetCoreIdentity`, and applied with
`dotnet ef database update`. The generated migration creates the standard
Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`,
`AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`,
`AspNetUserTokens`) alongside the existing `Items`, `Borrowers`, and `Loans`
tables in a single combined schema — confirmed by inspecting the generated
migration file and by direct `sqlite3` schema inspection of the resulting
database. All previously-existing constraints survived: unique index on
`Items.LibraryCode`, `Restrict` delete behavior on both `Loan` foreign keys,
and the TPH discriminator column on `Items`.

**Commands run:**
```
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 8.0.11
dotnet build
dotnet ef migrations add AddAspNetCoreIdentity
rm library.db
dotnet ef database update
dotnet build
dotnet run   (to trigger startup seeding)
```

**Manual testing performed:**
- Ran the app and confirmed via console log output that roles and the three
  staff accounts (admin/reception/manager) were created on first run, plus
  the three demo items.
- Verified directly via `sqlite3` that `AspNetUsers` contains exactly the
  three seeded usernames, `AspNetRoles` contains Admin/Reception/Manager,
  the `AspNetUserRoles` join table correctly maps each user to their
  intended role, and `Items` contains the three demo Book/Music/Toy rows.
- Smoke-tested HTTP behaviour with `curl`: public home page returns 200
  without authentication; `/Account/Login` returns 200; an unauthenticated
  request to `/Items` (Admin-only) returns 302 (redirect to login), proving
  the authorization pipeline is intact.
- Performed a full login flow test with `curl` (extracting the anti-forgery
  token from the login page, posting valid admin credentials, reusing the
  session cookie): login POST returned 302 (successful sign-in redirect),
  and a subsequent authenticated request to `/Items` returned 200 — proving
  `SignInManager.PasswordSignInAsync` and role-based `[Authorize]` both work
  correctly end-to-end with the new Identity pipeline.

**Automated testing:** None yet (no test project exists at this point in the
session — planned as a later step).

**PASS/FAIL result:** PASS. Build clean (0 warnings/errors) after fixes;
Identity migration, seeding, and role-based authorization all verified
working via direct database inspection and live HTTP requests.

**Remaining issues:** Admin staff-management UI, concurrency protection on
borrowing, and automated tests are still outstanding (see requirement matrix
from the initial audit entry) and will be addressed in subsequent sessions.

**Next step:** Manual browser testing of all three staff roles (Admin,
Reception, Manager) and the public portal against the new Identity-backed
login, then proceed to the Admin staff-management feature.

**References used:**
- Microsoft Learn — "Introduction to Identity on ASP.NET Core"
  (`AddIdentity`, `IdentityUser`, `IdentityDbContext` usage pattern).
- Microsoft Learn — "Create an ASP.NET Core app with user data protected by
  authorization" (role-based `[Authorize]` with Identity, `UserManager`/
  `RoleManager` seeding pattern).
- Microsoft Learn — "EF Core migrations" (generating and applying migrations
  with `dotnet ef migrations add` / `dotnet ef database update`, and the
  SQLite EF Core provider notes on migration support).

---

### 2026-09-13 — Manual browser testing session

**Objective:** I chose to pause all further feature work
(Admin staff-management, concurrency fix, automated tests) and personally
perform manual browser testing of the new Identity-backed application before
any further changes are made. Tests are being run one at a time, with each
PASS/FAIL result recorded only after I reported it — no result is
recorded unless I explicitly confirmed it in the browser.

**Setup:** App started with `dotnet run`, confirmed listening at
`http://localhost:5190` (home page and login page both returned HTTP 200 via
`curl` pre-check before I continued with browser testing).

Test results below are recorded as they are reported.

**Test 1 — Public portal: home page loads** (http://localhost:5190/)
I confirmed in the browser: page loads with no login required, all
three seeded demo items visible (The Hobbit / Thriller / Wooden Building
Blocks), layout reasonable.
**Result: PASS**

**Test 2 — Public portal: search and filters**
I found: type filter and available-only filter worked correctly,
and searching a non-matching term correctly showed "no results". However,
two problems were found:
1. Searching lowercase `hobbit` did not match `The Hobbit` — case-sensitive
   search bug.
2. No easy way to clear the search box/filters back to the unfiltered list
   — had to manually delete search text and re-submit.

**Result: FAIL (2 bugs found), fixed same session — see below.**

**Bug 1 fix — case-sensitive search:**
Root cause: `Controllers/HomeController.cs` used `i.Name.Contains(term)` etc.
directly. EF Core's SQLite provider translates `string.Contains()` to the
SQLite `instr()` function for correctness (avoiding LIKE wildcard-escaping
issues), and `instr()` is byte-for-byte case-sensitive — unlike SQL `LIKE`,
which SQLite treats case-insensitively for ASCII by default. So a lowercase
search term never matched a title with different capitalization.
Fix: lowercase both the search term and the compared columns in the query
(`i.Name.ToLower().Contains(term)` etc., with `term` also lowercased), which
still translates to the safe `instr()` function (no wildcard-injection risk)
but now compares consistently in lowercase.
File changed: `Controllers/HomeController.cs`.

**Bug 2 fix — no easy way to clear search:**
Added a "Clear" link next to the Search button in
`Views/Home/Index.cshtml` that links back to the plain `/` (Home/Index) with
no query string, resetting search text, type filter, and the available-only
checkbox in one click.

**Commands run:**
```
dotnet build
pkill -f "dotnet.*LibraryManagementSystem"; pkill -f "dotnet run"
dotnet run
curl "http://localhost:5190/?q=hobbit"   (confirmed "The Hobbit" now matches lowercase search)
```

**PASS/FAIL after fix:** Build succeeded (0 warnings/errors). Case-insensitive
match verified via `curl` (lowercase `hobbit` now returns "The Hobbit").

**Test 2 retest — confirmed by me in the browser:** lowercase "hobbit"
search now matches, Clear button resets search/filters correctly, type
filter and available-only filter still work.
**Result: PASS**

**References used:** None beyond direct inspection — the case-sensitivity
root cause was identified by reasoning about EF Core's SQLite `Contains()`
translation (the `instr()` SQL function), not from an external doc lookup.

---

**Test 3 — Admin login** (http://localhost:5190/Account/Login, admin / Admin123!)
I confirmed in the browser: logged in successfully, redirected to
Items (Admin landing page), nav bar showed username/role and Logout, no
Loans/Borrowers/Statistics links visible for the Admin role.
**Result: PASS**

---

### 2026-09-13 — Full testing, search UX, seed data, and automated tests

**Objective:** After confirming Tests 1–3 in the browser, I
asked Claude Code to take fuller ownership of testing, search
improvement, seed-data expansion, and visual polish, rather than continuing
one manual test at a time. My instructions for this session were:
test as thoroughly as possible using code review, automated tests,
integration tests and programmatic/browser testing (not just reading code
and assuming it works); fix the public search so it is properly
case-insensitive and partial-match friendly; expand seed/demo data
significantly; and improve the visual theme — all while keeping the
implementation simple enough to explain in class, and keeping this log
honest about what was tested by Claude Code programmatically versus what the
I tested manually in a browser.

**Part A — Programmatic authorization/logout testing (before any code
changes this session):**
Using `curl` against the already-running app (cookie jars per role,
anti-forgery tokens extracted from each form), tested:
- Manager attempting `/Items` (Admin-only) → 302 (blocked).
- Manager attempting `/Loans` (Reception-only) → 302 (blocked).
- Manager attempting `/Manager` (own area) → 200 (allowed).
- Reception attempting `/Manager` (Manager-only) → 302 (blocked).
- Reception attempting `/Items` (Admin-only) → 302 (blocked).
- Reception attempting `/Loans` (own area) → 200 (allowed).
- Anonymous requests to `/Items`, `/Loans`, `/Borrowers`, `/Manager` → all
  302 (redirected to login).
- Reception logout via POST `/Account/Logout` → 302, followed by a repeat
  request to `/Loans` → 302 (session correctly invalidated).

**Result: PASS (all role-boundary and logout checks passed).** This was
programmatic testing performed by Claude Code, not manual browser testing by
I.

**Part B — Seed data expansion:**
Files changed: `Data/DbSeeder.cs`.
Rewrote the seeded catalogue from 3 items/0 borrowers/0 loans to:
- 8 Books, 6 Music, 6 Toys (20 items total) with varied genres, authors/
  artists/toy types, and a realistic spread of statuses (14 Available, 3
  Borrowed, 2 Damaged, 1 Destroy).
- 5 seeded Borrowers (previously none existed until manually created via the
  Reception UI).
- 6 seeded Loans covering: 1 active loan not yet overdue, 2 active loans
  overdue, 1 returned loan with a paid fine, 1 returned loan with no fine
  (on time), 1 returned loan with an outstanding unpaid fine — chosen
  specifically so the Manager statistics dashboard shows non-zero, varied
  numbers across every stat category (active/returned/overdue loans,
  paid/outstanding fines) instead of all zeros.
Items with an active (unreturned) loan were given `Status = Borrowed`;
items whose only loan is already returned were given `Status = Available` —
kept internally consistent between the `Items` and `Loans` tables. All
seeding remains idempotent (guarded by `AnyAsync()` checks per table), so
re-running the app against an already-seeded database does not duplicate
data.

**Why:** I felt the 3-item catalogue was too limited and
wanted enough data to properly demonstrate searching, filtering, borrowing
and Manager statistics — an empty/near-empty demo undersells the working
features to an assessor.

**Database change:** Since only seed *data* changed (no schema/model
change), no new EF Core migration was needed. `library.db` (and its
`-shm`/`-wal` files) were deleted and recreated by re-running
`dotnet ef database update` against the existing `AddAspNetCoreIdentity`
migration, then the app was restarted to trigger the new seeding logic.

**Commands run:**
```
dotnet build
pkill -f "dotnet.*LibraryManagementSystem"; pkill -f "dotnet run"
rm library.db library.db-shm library.db-wal
dotnet ef database update
dotnet run
```

**Programmatic verification performed by Claude Code:**
- `sqlite3` queries confirmed exact expected counts: 20 items (14/3/2/1
  across Available/Borrowed/Damaged/Destroy), 5 borrowers, 6 loans.
- `sqlite3` query of the 6 loan rows confirmed the intended mix of due
  dates/return dates relative to the current date.
- Logged in as `manager` via `curl` (with anti-forgery token handling) and
  fetched `/Manager`, then grep-checked the rendered HTML: Total loans = 6,
  Active loans = 3, Returned loans = 3, Overdue loans = 2, Total assessed =
  $7.00, Paid = $4.00, Outstanding = $3.00 — all exactly matching the
  intended seed design.

**Result: PASS.** This was programmatic testing by Claude Code (database
inspection + authenticated `curl` requests against the live dashboard), not
manual browser testing by me.

**Part C — Automated test project:**
Files added: `Tests/LibraryManagementSystem.Tests.csproj`, `Tests/TestDb.cs`,
`Tests/LoansControllerTests.cs`, `Tests/ItemsControllerTests.cs`,
`Tests/HomeControllerTests.cs`.
File changed: `LibraryManagementSystem.csproj` (added an `<ItemGroup>`
excluding `Tests/**` from `Compile`/`Content`/`None` items — required
because SDK-style projects glob-include all `.cs` files under the project
directory by default, and the new nested `Tests/` folder was otherwise being
compiled as if it were part of the main web project, which doesn't
reference `xunit`).

Created an xUnit test project (`dotnet new xunit`) referencing the main
project directly and using the EF Core InMemory provider (so each test gets
an isolated, fast, file-free database) to call the real controller action
methods — not a rewritten/duplicated copy of the business logic. 18 tests
were written, covering exactly the business rules named in the assignment
brief:
- `LoansControllerTests` (8 tests): borrowing an available item sets the due
  date 14 days out and marks the item Borrowed; borrowing an already-
  borrowed item fails and creates no loan; borrowing an unknown library code
  fails with a model error; returning on time records no fine; returning 3
  days late calculates exactly `3 × FinePerLateDay`; returning as Damaged
  updates the item's status; marking a fine paid sets `FinePaid = true`
  only when a fine is actually outstanding.
- `ItemsControllerTests` (5 tests): creating a Book without an Author fails
  validation and saves nothing; creating Music with only its own required
  fields (no Book/Toy fields) succeeds — proving the conditional
  per-subtype validation still doesn't cross-require fields from other
  subtypes; creating a Toy without a minimum age fails; duplicate library
  codes are rejected; deleting an item with loan history is blocked.
- `HomeControllerTests` (5 tests): search is case-insensitive; search
  matches partial substrings; destroyed items are excluded from public
  results; the type filter works; the available-only filter excludes
  borrowed items.

The test project's `TargetFramework` was changed from the `dotnet new
xunit` default of `net9.0` to `net8.0` to match the main project.

**Problems/errors encountered:**
1. First `dotnet test` run failed to build with `CS0246` errors claiming
   `Xunit`/`LibraryContext` couldn't be found — but the errors were reported
   against `LibraryManagementSystem.csproj` (the main web project), not the
   test project. Root cause: SDK-style projects implicitly include all
   `.cs` files under the project folder tree, so the new `Tests/` subfolder
   was being compiled straight into the main project, which has no
   reference to `xunit`. Fixed by excluding `Tests/**` from the main
   project's `Compile`/`Content`/`None` item groups.
2. After that fix, `LoansControllerTests.cs` still failed with
   `CS0246: LibraryContext could not be found` — a missing
   `using LibraryManagementSystem.Data;` in that file. Fixed by adding the
   missing `using`.

**Commands run:**
```
dotnet new xunit -n LibraryManagementSystem.Tests -o Tests
dotnet add reference ../LibraryManagementSystem.csproj   (from Tests/)
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.0.11   (from Tests/)
dotnet build
dotnet test Tests/LibraryManagementSystem.Tests.csproj
```

**Result: PASS.** `dotnet build` succeeds (0 warnings/errors) for the main
project; `dotnet test Tests/LibraryManagementSystem.Tests.csproj` reports
**18 passed, 0 failed, 0 skipped**. This is genuine automated testing (xUnit
running real controller code against an in-memory EF Core database), not
manual testing, and not a fabricated/simulated result — the actual test run
output is quoted above.

**Part D — Visual theme overhaul:**
Files changed: `wwwroot/css/site.css` (full rewrite, same class names
preserved throughout so no view markup had to be restructured),
`Views/Home/Index.cshtml` (type-coded badge class + a "Showing N items"
result-count line), `Views/Items/Index.cshtml` (type-coded badge + status
pill in the Admin item table, matching the public portal's styling).

Replaced the previous blue/navy palette with a warm, cohesive "library"
theme: deep burgundy/maroon primary (`--primary: #7a1f2b`) with a warm gold
accent (`--accent: #b8860b`), a warm parchment background instead of cool
grey, and a dark warm-brown header/footer instead of navy. Headings use a
serif font stack (`Georgia, "Iowan Old Style", "Times New Roman", serif`)
layered over the existing system-font body text, for a more "library reading
room" feel without pulling in an external web font (kept the whole theme
dependency-free — no Google Fonts/CDN calls — so the site still renders
correctly with no internet access, e.g. a locked-down lab machine).
Item type badges (Book/Music/Toy) now get distinct colours on both the
public portal and the Admin item list, status pills got consistent
colour-coding (green/amber/red), cards got a hover lift and softer shadow,
stat cards got a left accent border and larger serif numerals, tables got a
warm header row and row-hover highlight, and the whole layout keeps the
existing 720px mobile breakpoint (only value tweaks, same responsive
structure).

**Why:** I felt the previous styling was "too dull and plain"
and wanted something that reads as a real small library website rather than
a bare assignment form, while explicitly ruling out any JS framework —
Razor views + CSS only, which this satisfies (no new front-end dependencies
were added).

**Commands run:**
```
dotnet build
pkill -f "dotnet.*LibraryManagementSystem"; pkill -f "dotnet run"
dotnet run
```

**Programmatic verification performed by Claude Code:**
- Confirmed via `curl` that the home page, Admin `/Items` and
  `/Items/Create`, Reception `/Loans`, `/Borrowers` and `/Loans/Borrow`, and
  the Manager `/Manager` dashboard all still return HTTP 200 after the
  markup/CSS changes (no Razor errors introduced).
- Confirmed via `curl` + `grep` that the new type-coded badge classes
  (`badge-book`/`badge-music`/`badge-toy`) render on the public portal, and
  that the new "Showing N items" line renders with the correct count (19,
  correctly excluding the 1 seeded `Destroy`-status item from the public
  portal).
- Reran the full xUnit suite (`dotnet test`) after the theme changes: still
  **18 passed, 0 failed**, confirming the CSS/markup-only changes did not
  break any controller behaviour.

**Note:** Claude Code did not visually inspect the new theme in an actual
browser (no browser automation was used this session) — the verification
above confirms the pages render without error and contain the expected
elements, not that the visual design looks good. I still needed to
visually review the new theme in my own browser as part of manual
testing (see the smoke-test checklist at the end of this session's summary).

**Result: PASS** (programmatic/structural verification only — visual
appearance still needed my confirmation).

**Part E — Live end-to-end workflow test and final database reset:**
Beyond the xUnit suite (which uses an isolated in-memory database), Claude
Code also exercised the real running application end-to-end via `curl`
against the live SQLite database to prove the full stack works together,
not just the controller logic in isolation:
- Logged in as `reception`, borrowed `B001` (The Hobbit) for borrower id 1
  (Alice Nguyen) → confirmed via direct `sqlite3` query that the item's
  `Status` flipped from Available (0) to Borrowed (1).
- Opened the Return page for that loan and submitted it as Available →
  confirmed via `sqlite3` that the item's `Status` reverted to Available (0)
  and the loan's `ReturnedAt`/`FineAmount` were correctly populated
  (on-time return, fine = 0.00).
- Attempted to borrow `B008` (Dune), which is seeded as already Borrowed →
  confirmed the request was rejected with the expected "currently Borrowed"
  validation message and no second loan was created.

**Result: PASS.** This live test added one extra real loan record to the
working database (on top of the 6 documented seed loans), which would have
would have made my upcoming manual testing start from an inconsistent
loan count. To avoid confusion, the database was deleted and recreated one
more time from the `AddAspNetCoreIdentity` migration and reseeded, restoring
it to the exact documented baseline (20 items / 5 borrowers / 6 loans),
confirmed again via `sqlite3` counts before handing back control.

**Commands run:**
```
pkill -f "dotnet.*LibraryManagementSystem"; pkill -f "dotnet run"
rm library.db library.db-shm library.db-wal
dotnet ef database update
dotnet run
```

**Summary of this session's automated/programmatic results:**
- `dotnet build`: succeeded, 0 warnings, 0 errors (checked repeatedly after
  every change).
- `dotnet test Tests/LibraryManagementSystem.Tests.csproj`: **18/18 passed**
  (final run, after all changes in this session).
- Programmatic `curl`/`sqlite3` testing covered: role-based authorization
  boundaries (Admin/Reception/Manager cross-access, anonymous access),
  logout/session invalidation, seeded data integrity, Manager statistics
  correctness against known seed values, full live borrow→return workflow,
  rejection of borrowing an unavailable item, and case-insensitive/partial
  public search.
- Known limitation still confirmed present (not fixed this session, as it
  remained outside my approved scope at that point): the
  borrow workflow still has a read-then-write race condition between
  checking `item.Status == Available` and saving the new loan — two
  simultaneous borrow attempts for the same item are not yet safely
  serialized. This matches the "concurrent borrowing protection" item the
  I had listed for later, requiring separate approval.

**Remaining items still requiring my explicit approval (not
started this session, per earlier instruction to pause on these until
approved):** Admin staff-management UI, concurrent-borrowing protection.

**Next step:** Pause and review a concise summary of changes,
full test results, and a short manual smoke-test checklist for them to
personally verify in a browser — especially the new visual theme, which was
only verified programmatically (HTTP 200 + expected markup), not visually,
in this session.

---

### 2026-09-13 — Admin staff-account management (ASP.NET Core Identity)

**Objective:** After confirming the visual theme was satisfactory, I
approved implementing the two remaining major features that had
been deliberately paused earlier: Admin staff-account management, and
concurrent-borrowing protection. This entry covers staff management.

**Requirement:** Admin must be able to view staff accounts, create staff
accounts, assign only Admin/Reception/Manager roles, edit account details,
change roles, and disable/delete accounts safely — using
`UserManager<ApplicationUser>` and `RoleManager<IdentityRole>` directly, with
no separate/custom authentication system.

**Files added:**
- `Models/StaffRoles.cs` — a single shared `string[] All = ["Admin",
  "Reception", "Manager"]` constant, now used by both `Data/DbSeeder.cs`
  (previously had its own private duplicate array) and the new
  `StaffController`, so the list of valid staff roles exists in exactly one
  place.
- `ViewModels/StaffFormViewModel.cs`, `ViewModels/StaffListItemViewModel.cs`.
- `Controllers/StaffController.cs` — `[Authorize(Roles = "Admin")]`. Actions:
  `Index` (list all `AspNetUsers` with their role and lockout status via
  `UserManager.GetRolesAsync`/`IsLockedOutAsync`), `Create`/`Edit` (GET+POST,
  sharing one `Form` view like the existing `ItemsController` convention),
  `ToggleLock` (POST — disables via `SetLockoutEnabledAsync(true)` +
  `SetLockoutEndDateAsync(DateTimeOffset.MaxValue)`, re-enables via
  `SetLockoutEndDateAsync(null)`), `Delete`/`DeleteConfirmed` (GET confirm +
  POST, matching the existing `ItemsController`/`BorrowersController`
  delete-confirmation pattern).
- `Views/Staff/Index.cshtml`, `Views/Staff/Form.cshtml` (shared
  Create/Edit), `Views/Staff/Delete.cshtml` — same CSS classes and structure
  as the existing Items/Borrowers views, so no new styling was needed.

**Files changed:**
- `Data/DbSeeder.cs` — now references `StaffRoles.All` instead of its own
  local array (removed duplication).
- `Views/Shared/_Layout.cshtml` — added a "Staff" nav link, shown only when
  `User.IsInRole("Admin")`.

**Safety rules implemented (all three explicitly requested):**
1. **Cannot delete the last remaining Admin account** — `IsLastAdminAsync`
   checks `UserManager.IsInRoleAsync` + `GetUsersInRoleAsync("Admin").Count`.
2. **Cannot disable (lock out) the last remaining Admin account** — same
   guard applied in `ToggleLock`.
3. **Cannot change the role of the last remaining Admin away from Admin** —
   same guard applied in `Edit`, checked before the role is actually changed.

Two additional guards not explicitly requested but added as a direct,
low-cost consequence of "safely" — flagged here rather than silently added:
- **An Admin cannot delete their own account while signed in** (compares
  `user.Id` against `_userManager.GetUserId(User)`), to avoid a confusing
  mid-session self-lockout.
- **An Admin cannot disable their own account while signed in** — same
  reasoning.
Both are single `if` checks reusing data already being computed; they were
not treated as a separate feature requiring approval, but are called out
here so I could see exactly what was added beyond the literal
brief.

**Password handling:** Create requires a password (validated manually,
consistent with the existing `ItemsController.ValidateTypeSpecificFields`
conditional-validation style already in this codebase); on Edit, an admin
can optionally reset a user's password using `GeneratePasswordResetTokenAsync`
+ `ResetPasswordAsync` (the standard Identity mechanism for an administrator
to reset a password without knowing the old one) — left blank, the password
is unchanged. No plaintext password is ever stored or compared manually;
all hashing is handled by Identity's own `UserManager`.

**Commands run:**
```
dotnet build
```

**Programmatic verification performed by Claude Code (logged in as `admin`
via `curl`, real running app + real SQLite database):**
- Created a new Reception staff account (`testreception`) → confirmed
  present in `AspNetUsers` via `sqlite3`.
- Attempted to delete the only Admin account → blocked (302 redirect back to
  Index with a TempData error), confirmed still present in the database.
- Attempted to disable the only Admin account → blocked, confirmed
  `LockoutEnd` still null.
- Attempted to change the only Admin's role to Reception → blocked with the
  expected ModelState error message, confirmed role unchanged in
  `AspNetUserRoles`.
- Disabled and then deleted the non-critical `testreception` account →
  both succeeded normally, confirming the guards are specific to the
  last-admin/self cases and don't block ordinary staff management.
- Created a fresh throwaway account, disabled it, then attempted to log in
  as that account → login correctly rejected ("Invalid username or
  password" — Identity's `PasswordSignInAsync` rejects locked-out users
  regardless of the `lockoutOnFailure` parameter), proving the "disable"
  action genuinely blocks sign-in and isn't just a cosmetic database flag.

**Automated tests added:** `Tests/IdentityTestHost.cs` (a small DI container
that mirrors the real `AddIdentity<ApplicationUser, IdentityRole>()` setup
in `Program.cs` against an EF Core InMemory-backed `LibraryContext`, so tests
exercise the real `UserManager`/`RoleManager` pipeline — password hashing,
role assignment, lockout — not a hand-rolled substitute), plus
`Tests/StaffControllerTests.cs` (12 tests): creating a staff account assigns
the correct role; creating without a password fails and creates no user;
creating with an invalid/unsupported role fails; editing a non-critical
user's role updates it correctly; the last Admin cannot have their role
changed away; a non-last Admin's role *can* be changed; disabling a
non-admin account succeeds; the last Admin cannot be disabled; an Admin
cannot disable their own account; the last Admin cannot be deleted; deleting
a non-critical account succeeds; an Admin cannot delete their own account.

**Problems/errors encountered (fixed same session):**
1. Two tests (`Edit_ChangeRole_UpdatesRoleCorrectly`,
   `Edit_NonLastAdmin_CanChangeRoleAway`) failed with
   `System.InvalidOperationException: Role MANAGER does not exist.` /
   `Role RECEPTION does not exist.` — the test helper only created whichever
   single role was passed to `CreateUserAsync`, so a test that changed a
   user's role to a role nobody had been assigned yet had nothing to assign
   them to. **Fix:** added `IdentityTestHost.CreateAsync()`, which seeds all
   three `StaffRoles.All` roles up front (mirroring what `DbSeeder` actually
   does at real application startup), and updated every test to use it.
2. `Delete_NonLastAdmin_Succeeds` was passing the string literal `"admin1"`
   as the simulated signed-in user's id, instead of that user's real
   (GUID) `Id` — harmless for that specific test's assertion but sloppy and
   confusing. Fixed to use the actual `ApplicationUser.Id` returned by
   `CreateUserAsync`.

**Result: PASS.** `dotnet build` clean; all 12 new staff-management tests
pass; live `curl` verification against the real app/database confirmed every
safety rule and the create/disable/delete/login-rejection flow end-to-end.

**References used:**
- Microsoft Learn — "Manage users in ASP.NET Core Identity" and
  "Lockout in ASP.NET Core Identity" (`UserManager.SetLockoutEnabledAsync`/
  `SetLockoutEndDateAsync`/`IsLockedOutAsync` as the standard "disable
  account" mechanism, rather than adding a custom `IsActive` flag).
- Microsoft Learn — "Reset passwords in ASP.NET Core Identity"
  (`GeneratePasswordResetTokenAsync` + `ResetPasswordAsync` as the standard
  way for an administrator to reset a user's password without knowing the
  current one).

---

### 2026-09-13 — Concurrent-borrowing protection

**Requirement:** Prevent two Reception users from successfully borrowing the
same item at (near-)the same time, using a SQLite/EF-Core-appropriate
mechanism rather than a SQL Server-specific `rowversion` column, plus an
automated test that actually simulates the race and proves only one loan can
succeed.

**Chosen approach — optimistic concurrency via `Item.Status` as a
concurrency token:** SQLite has no native auto-incrementing `rowversion`/
`timestamp` column type the way SQL Server does, so the standard SQL-Server
sample (`[Timestamp]` on a `byte[] RowVersion` column) doesn't apply here.
Instead, `Item.Status` itself was configured as an EF Core concurrency token
(`modelBuilder.Entity<Item>().Property(i => i.Status).IsConcurrencyToken()`
in `Data/LibraryContext.cs`). This makes EF Core append `AND Status =
@original_status` to every `UPDATE Items ...` statement it generates. If two
requests both read an item as `Available` and race to borrow it: the first
request's `UPDATE ... WHERE Id=@id AND Status=0` matches the row and
succeeds, changing the stored value to `Borrowed`; the second request's
`UPDATE ... WHERE Id=@id AND Status=0` (its own stale original value) now
matches **zero rows**, because the actual stored value has already changed —
EF Core detects the 0-row mismatch and throws
`DbUpdateConcurrencyException` instead of silently double-committing. This
needed **no new column and no schema change** — `Status` already exists as
a plain integer column; `IsConcurrencyToken()` only changes how EF Core
*writes*, not what's stored. This was the "atomic/conditional update"
approach the assignment brief itself suggested as SQLite-appropriate.

**Files changed:**
- `Data/LibraryContext.cs` — added the `IsConcurrencyToken()` configuration
  (with an explanatory comment, since this is genuinely non-obvious EF Core
  behaviour worth documenting for anyone reading the code later).
- `Controllers/LoansController.cs` — `Borrow` now wraps `SaveChangesAsync()`
  in a `try/catch (DbUpdateConcurrencyException)`, converting a caught
  conflict into a normal `ModelState` error ("This item was just borrowed
  by someone else. Please try again.") and re-rendering the form, instead of
  letting it bubble up as an unhandled 500 error.
- `Controllers/ItemsController.cs` — the same `try/catch` was added around
  `Edit`'s `SaveChangesAsync()`. This wasn't explicitly requested, but since
  the concurrency token was added to `Item.Status` globally (not just for
  the borrow path), leaving Admin's item-edit path unguarded would have
  turned this feature into a new source of unhandled 500 errors the moment
  an Admin edit happened to race against a Reception borrow. Flagged here as
  a direct, necessary consequence of the main change rather than scope
  creep.

**Migration:** Generated via
`dotnet ef migrations add AddItemStatusConcurrencyToken`. As expected, the
generated `Up`/`Down` methods are **empty** — confirmed by inspecting the
generated file before applying it — because `IsConcurrencyToken()` is
metadata-only for a plain existing column; it changes generated SQL `WHERE`
clauses, not the schema. Still applied via `dotnet ef database update` (no
data was touched — confirmed the row counts were unchanged before/after) so
the migration history and model snapshot stay in sync with the running
database, per the "use EF Core migrations properly" instruction.

**Commands run:**
```
dotnet build
dotnet ef migrations add AddItemStatusConcurrencyToken
dotnet ef database update
```

**Automated test — genuinely simulating the race:**
`Tests/LoansControllerTests.Borrow_ConcurrentAttemptsForSameItem_OnlyOneSucceeds`
seeds one Available item and two borrowers, opens **two independent
`LibraryContext` instances** (simulating two separate HTTP requests, each
with its own scoped DbContext, exactly as ASP.NET Core would give each real
request its own scoped context), has both load the item as Available before
either writes, then calls `LoansController.Borrow` on each and asserts:
exactly one returns a successful redirect, the other returns a re-rendered
view (not an unhandled exception), and the database ends up with exactly
one loan and the item correctly `Borrowed`.

**A genuine methodology bug found and fixed while writing this test:** the
first version of this test used the EF Core **InMemory** provider (like the
rest of the test suite) and initially reported **both** concurrent borrow
attempts as succeeding — which would have been silent, false confidence in
a race-condition fix that doesn't actually work. Investigating, this is a
known limitation of the InMemory provider: it does not enforce optimistic
concurrency tokens the way a real relational provider does (no simulation of
"0 rows affected" when a `WHERE` clause stops matching). **Fix:** rewrote
this one test to use a **real temporary SQLite database file**
(`Path.GetTempPath()/concurrency-test-{guid}.db`, created via
`EnsureCreatedAsync()`, deleted in a `finally` block including its `-shm`/
`-wal` files) instead of the InMemory provider, so the test exercises the
actual SQLite `WHERE`-clause mechanism the production code relies on. After
that fix, the test correctly reports exactly one success. This is
specifically noted here because it's exactly the kind of thing that could
have produced a test suite that looks green while proving nothing about the
real bug — the rest of the suite intentionally still uses InMemory (fast,
adequate for testing plain business logic), and only this one
concurrency-specific test uses real SQLite, since that's the only way to
honestly prove this particular mechanism.

**Live end-to-end verification against the real running app (not just the
unit test):** Logged in as `reception` in two separate cookie-jar sessions
(simulating two different Reception staff), fetched two independent
anti-forgery tokens, then fired both `POST /Loans/Borrow` requests for the
same library code (`B001`, The Hobbit) **in parallel background shell jobs**
via `curl ... &` / `wait`. Result: one request returned `302` (success), the
other returned `200` with the body containing "just borrowed by someone
else" (the friendly re-rendered form), and `sqlite3` confirmed exactly one
`Loan` row was created for that item and its `Status` ended as `Borrowed`
— the real application, under a real (if artificially synchronized) race,
behaves exactly as the unit test predicts.

**Result: PASS.** `dotnet build` clean; `dotnet test` →
**31/31 passed** (18 original + 12 staff-management + 1 concurrency, plus
the pre-existing suite re-verified unaffected); live concurrent-request test
against the real app and real database confirmed the same outcome outside
of the test harness.

**References used:**
- Microsoft Learn — "Handle concurrency conflicts" (EF Core) — general
  optimistic concurrency model (`IsConcurrencyToken`,
  `DbUpdateConcurrencyException`).
- Microsoft Learn — "EF Core providers — In-memory database provider" —
  confirms the InMemory provider's limitations for testing
  provider-specific/relational behaviour, which is exactly what was observed
  and is the documented reason the concurrency test uses real SQLite instead.
- Microsoft Learn — "SQLite EF Core database provider — Limitations" —
  confirms SQLite has no native rowversion/timestamp type, supporting the
  choice of a plain concurrency-token property over a SQL Server–style
  `[Timestamp] byte[]` column.

---

### 2026-09-13 — Navigation / "Back" button pass

**Objective:** I asked for a review of every workflow page
(create/edit/delete/borrow/return/staff-management) for places a user could
get stuck, with a clear, theme-consistent Back button returning to the
relevant list/dashboard rather than relying on the browser's own Back
button.

**Audit finding:** Most workflow pages (Items Create/Edit/Delete, Borrowers
Create/Edit/Delete, Loans Borrow/Return) already had a working "Cancel"
button linking back to the correct Index action — this existing convention
was preserved and extended to the new Staff pages, not reinvented. The
genuinely missing case was `Views/Account/AccessDenied.cshtml`, which had
**no navigation options at all** beyond the always-visible header — a real
dead-end page. Two smaller, lower-cost improvements were also made: the
existing "Cancel" wording was changed to explicit "← Back to Items" / "← Back
to Borrowers" / "← Back to Loans" / "← Back to Staff Accounts" wording (my
brief specifically asked for a "clear" Back button — "Cancel" is
ambiguous about whether it discards changes or just navigates away).

**Files changed:** `Views/Account/AccessDenied.cshtml` (added a
"← Return to Home" button, styled with the existing `.form-card`/`.btn`
classes — no new CSS), `Views/Items/Form.cshtml`, `Views/Items/Delete.cshtml`,
`Views/Borrowers/Form.cshtml`, `Views/Borrowers/Delete.cshtml`,
`Views/Loans/Borrow.cshtml`, `Views/Loans/Return.cshtml` (wording only,
same destination and same `.btn.btn-secondary` styling as before).

**Pages deliberately left unchanged:** `Manager/Index` (top-level dashboard
reached via nav, not a sub-step — no back destination makes sense), the
Items/Borrowers/Loans/Staff Index list pages (destinations, not workflow
steps), `Account/Login` (an entry point, not somewhere a user gets "stuck").

**Result: PASS** — verified via `curl` + `grep` that the new wording renders
on `Items/Create`, `Staff/Create`, and `Account/AccessDenied`; no build
errors.

**Final combined build/test verification for this session:**
```
dotnet build      → Build succeeded, 0 warnings, 0 errors
dotnet test Tests/LibraryManagementSystem.Tests.csproj
                  → Passed! Failed: 0, Passed: 31, Skipped: 0, Total: 31
```

The working SQLite database (`library.db`) was deleted and recreated one
final time via `dotnet ef database update` + reseeding, since the live
concurrency test above deliberately created a real extra loan record — this
restores the database to the documented baseline (20 items / 5 borrowers /
6 loans / 3 staff accounts) before I continued with my
own manual review.

---

### 2026-09-13 — Final review and closeout

**Objective:** With both remaining major features (staff management,
concurrency protection) complete and stable, and I confirmed
no further features should be added, this entry performs the final
build/test/database verification and a final requirements audit before
submission, and cleans up the log itself (a "References" section had ended
up positioned mid-document rather than at the end, from an earlier editing
pass — moved to the correct place below).

**My decision on the one remaining open item (authorization
integration tests):** I explicitly decided **not** to add a
`WebApplicationFactory`-based xUnit integration suite for role-based
authorization, on the grounds that the assignment does not specifically
require that level of integration testing and that authorization has
already been verified through live authenticated testing (see the
programmatic `curl` role-boundary testing recorded earlier in this log).
This was a deliberate scope decision I approved, not a gap.

**Final commands run:**
```
dotnet build
dotnet test Tests/LibraryManagementSystem.Tests.csproj
dotnet ef migrations list
dotnet ef migrations has-pending-model-changes
```

**Final results:**
- `dotnet build` → **Build succeeded, 0 warnings, 0 errors.**
- `dotnet test` → **Passed! Failed: 0, Passed: 31, Skipped: 0, Total: 31.**
- `dotnet ef migrations list` → two migrations, both applied:
  `20260913072912_AddAspNetCoreIdentity`,
  `20260913081745_AddItemStatusConcurrencyToken`.
- `dotnet ef migrations has-pending-model-changes` → **"No changes have been
  made to the model since the last migration."** (confirms the C# model and
  the applied migrations are in sync — nothing was changed in code without a
  corresponding migration).
- Direct `sqlite3` inspection of the live `library.db` confirmed the exact
  documented baseline: 20 `Items`, 5 `Borrowers`, 6 `Loans`, 3 `AspNetUsers`,
  3 `AspNetRoles`, and all expected Identity tables present (`AspNetUsers`,
  `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`,
  `AspNetUserLogins`, `AspNetUserTokens`) alongside `Items`, `Borrowers`,
  `Loans`, and `__EFMigrationsHistory`.

**Log accuracy review performed:** Read the entire log end-to-end. Found
and fixed one structural issue: a "## References" section (added after the
"visual theme" session) had been left in the middle of the document,
followed by three more dated entries — meaning it did not actually reflect
the final, complete state of things despite its heading. It has been
removed from that position and replaced by the consolidated section below,
which is now genuinely the last section in the file. Also added a
"References used" note to the Admin staff-account management entry, which
had been missing one. No factual claims elsewhere in the log needed
correction — every PASS/FAIL result and command output quoted in this log
reflects an actual `dotnet build`/`dotnet test`/`curl`/`sqlite3` run
performed during that session, not a prediction or a simulated result.

---

## FINAL ASSIGNMENT REQUIREMENTS AUDIT

| Requirement | Status | Evidence |
|---|---|---|
| `Item` superclass, `Book`/`Music`/`Toy` inheritance | **Complete** | `Models/Item.cs` (abstract) + TPH discriminator confirmed in schema |
| Admin item CRUD | **Complete** | `Controllers/ItemsController.cs`, `[Authorize(Roles="Admin")]` |
| Item statuses (Available/Borrowed/Damaged/Destroy) | **Complete** | `Models/Item.cs` `ItemStatus` enum |
| Reception borrower CRUD | **Complete** | `Controllers/BorrowersController.cs` |
| Borrow by library code + due date | **Complete** | `Controllers/LoansController.cs`, `Models/LibraryRules.cs` |
| Return + late fine calculation | **Complete** | `LoansController.Return`, unit-tested |
| Fine payment tracking | **Complete** | `LoansController.MarkFinePaid`, unit-tested |
| Manager statistics dashboard | **Complete** | `Controllers/ManagerController.cs` |
| Public portal, no login required | **Complete** | `Controllers/HomeController.cs` |
| Case-insensitive, partial-match public search | **Complete** | Fixed and unit-tested this engagement |
| Responsive UI (mobile-friendly) | **Complete** | Confirmed by me visually |
| Item/Borrower validation | **Complete** | Pre-existing, preserved and verified unchanged |
| ASP.NET Core Identity (`IdentityUser`, `IdentityDbContext`) | **Complete** | `Models/ApplicationUser.cs`, `Data/LibraryContext.cs` |
| Admin/Reception/Manager roles via Identity | **Complete** | Seeded via `RoleManager`, existing `[Authorize(Roles=...)]` unchanged |
| EF Core migrations (not `EnsureCreated`) | **Complete** | 2 migrations, `Database.Migrate()` at startup |
| Admin staff-account management | **Complete** | `Controllers/StaffController.cs`, last-Admin and self-action guards |
| Concurrent-borrowing protection (SQLite-appropriate) | **Complete** | `Item.Status` as EF concurrency token; verified via real-SQLite unit test and a live parallel-request test |
| Automated tests of core business rules | **Complete** | 31 xUnit tests, `dotnet test` green |
| Automated authorization/role integration tests | **Additional testing / Not required by assignment** — role-based authorization was verified through live authenticated testing, but a dedicated `WebApplicationFactory` xUnit integration suite was not added because it would add unnecessary scope for this simple university project. Decision approved by me. |
| Navigation/"Back" buttons on workflow pages | **Complete** | Added this engagement; one genuine dead end (Access Denied page) fixed |
| Programmer's log maintained contemporaneously | **Complete** | This document |
| References documented | **Complete** | See below |
| AI use disclosed honestly | **Complete** | See below |
| .NET 10 framework upgrade | **Not applicable** | Explicitly out of scope per my instruction; project remains on the working `net8.0` target |

**Nothing in the assignment brief is Missing or Partial as of this final
review.** The only non-"Complete" row above is a deliberate, disclosed,
scope decision I approved, not a gap.

---

### 2026-09-13 — Visual elegance / UX refinement pass

**Objective:** With all functionality complete, tested, and confirmed
stable, I requested a dedicated final round focused
**exclusively on presentation** — visual polish and usability, worth 10% of
the assignment rubric — explicitly ruling out any change to business logic,
Identity configuration, database structure, migrations, borrowing rules,
concurrency handling, or controller behaviour except where a genuine
UI-related bug required it. This entry documents that round. **No
functional code was changed** beyond two data-annotation label fixes (noted
below) and the addition of one static helper method per view for choosing
an icon — nothing that alters validation, business rules, or persistence.

**Design direction (my brief):** a warm, elegant, "premium
library" theme — cream backgrounds, deep burgundy/plum primary, muted gold
accents, serif headings, soft shadows, restrained (not childish) colour use
— applied consistently across the public portal, navigation, Admin,
Reception, Manager, forms, tables, feedback states, and footer, with
accessibility preserved (status must not rely on colour alone, visible
focus states, readable contrast).

**Inspection before editing:** re-read `wwwroot/css/site.css`,
`Views/Shared/_Layout.cshtml`, and every major role page (`Home/Index`,
`Items/Index`, `Loans/Index`, `Manager/Index`, `Staff/Index`,
`Borrowers/Index`, `Account/Login`) to identify what already worked from
the previous theming round (warm burgundy/gold palette, serif headings,
type-coded badges, status pills, card hover states, responsive breakpoint,
Back-navigation links) so it could be preserved and extended rather than
replaced. Confirmed the existing foundation was solid; this round refines
and completes it rather than starting over.

**Files changed (all Razor views + CSS only, one icon library CDN link,
and two label annotations — no controllers, models' business logic,
migrations, or Program.cs touched):**

- `wwwroot/css/site.css` — full refinement pass: retuned the colour palette
  to be more restrained/coordinated (the three item-type badge colours were
  changed from a slightly "candy" lavender/peach/mint set to a muted
  plum/gold/olive family that ties back to the primary/accent colours,
  directly addressing "not overly colourful or childish"); added
  `:focus-visible` global outline styling for keyboard accessibility; added
  `.action-btn`/`.action-btn-danger`/`.action-btn-muted` — small pill-style
  icon+text buttons replacing plain text links in every table's Actions
  column, for consistent, clearly-clickable actions; added `.user-chip` and
  `.logout-btn` styles for the header; added `nav a.active` styling; added
  `.section-block` (white panel wrapper used to group the Manager
  dashboard's four stat sections, improving hierarchy); added `.stat-card
  .stat-warn` (a red-accented variant applied when overdue loans/
  outstanding fines are non-zero, drawing the eye to what needs attention);
  added `.overdue-tag` (an explicit text+icon tag, not just the existing
  background tint, so overdue loans are not signalled by colour alone);
  added `.fine-highlight` (a prominent callout box for the Return page's
  estimated fine); added `.input-icon-wrap` (icon-inside-input styling for
  the search box); added `.empty-state` icon/heading treatment; added
  `.alert-success` (styling only — no controller currently sets a success
  message, added for consistency/forward-compatibility per the "feedback
  states" request); refined spacing, shadow, and radius tokens throughout.
- `Views/Shared/_Layout.cshtml` — added the Bootstrap Icons CDN stylesheet
  link (`cdnjs`, pinned to `1.11.3`); added active-nav-link detection
  (`ViewContext.RouteData.Values["controller"]`) so the current section is
  visibly highlighted; replaced the plain "Username (Role)" text with a
  `.user-chip` (icon + name + role pill) and the bare "Logout" link with a
  clearly visible `.logout-btn`; expanded the footer from a single line to
  a two-line `.footer-inner` (system name + a short, honest tagline
  identifying it as a university coursework project — no fabricated
  business details were added); added an icon to the `TempData["Error"]`
  alert banner.
- `Views/Home/Index.cshtml` (public portal) — added a hero "eyebrow" badge
  ("Open to everyone · no login required"), a search icon inside the search
  input, per-type icons on the item badges (`bi-book`/`bi-disc`/
  `bi-puzzle`), per-status icons on the availability pill (so status is
  never colour-only), and a richer empty-state message with an icon.
- `Views/Items/Index.cshtml`, `Views/Borrowers/Index.cshtml`,
  `Views/Loans/Index.cshtml`, `Views/Staff/Index.cshtml`,
  `Views/Manager/Index.cshtml` — converted plain Edit/Delete/Return/etc.
  text links to the new `.action-btn` pill buttons with icons; added an
  empty-state row for the (currently unreachable, since the app is seeded,
  but still correct) case of an empty list; `Manager/Index` was
  restructured into four `.section-block` panels (Borrowing / Item status &
  type / Fines / Most borrowed) each with an icon heading, and the overdue
  loans / outstanding fines stat cards get the red `.stat-warn` treatment
  when non-zero — directly addressing "make important numbers immediately
  readable" and "improve hierarchy."
- `Views/Loans/Index.cshtml` specifically — overdue rows now show an
  explicit "⚠ Overdue" text+icon tag next to the due date (in addition to
  the existing red row tint), and the Fine column now renders as a
  green "paid" or amber/red "owing" pill instead of plain text — both
  changes are accessibility-motivated (status must not depend on colour
  alone) as well as visual polish.
- `Views/Loans/Return.cshtml` — the estimated fine is now shown in a
  prominent `.fine-highlight` callout (amber if a fine applies, green "no
  late fine" if not) instead of a plain `<dl>` row, directly addressing
  "highlight fines appropriately without making the page visually noisy."
- `Views/Items/Form.cshtml`, `Views/Borrowers/Form.cshtml`,
  `Views/Staff/Form.cshtml` and their `Delete.cshtml` counterparts,
  `Views/Loans/Borrow.cshtml` — added icons to headings and
  Save/Delete/Back buttons; added small uppercase "Book details"/"Music
  details"/"Toy details" labels on the Item form's conditional field
  groups; all existing "Back to X" navigation links (added in the previous
  session) were preserved unchanged in destination and behaviour, only
  re-styled consistently with the rest of the button system.
- `Views/Account/Login.cshtml` — restyled the demo-accounts box as
  icon-labelled credential rows with `<code>` styling; added a small "Back
  to public portal" link (the login page was not a dead end before, but
  this makes the escape route explicit and consistent with the rest of the
  navigation work).
- `Views/Account/AccessDenied.cshtml` — added an icon to match the rest of
  the icon system (the actual navigation fix for this page — it was a true
  dead end before — was already done in the previous session; this round
  only restyled it).

**The two non-visual-only changes, called out explicitly as instructed:**
1. `ViewModels/ItemFormViewModel.cs` — added `[Display(Name = "Library
   Code")]` to the `LibraryCode` property.
2. `Models/Borrower.cs` — added `[Display(Name = "Full Name")]` to the
   `FullName` property.
   Neither of these were requested by name, but both were found live in the
   browser: without a `[Display]` attribute, ASP.NET Core's label tag
   helper falls back to the raw C# property name, so the Add/Edit Item form
   was rendering the label "LibraryCode" (no space) and the Add/Edit
   Borrower form was rendering "FullName" (no space) — a real, visible
   polish defect directly relevant to "elegance," and exactly the kind of
   "genuine UI-related bug" my brief allowed fixing. These are
   pure display-name annotations: they do not change validation rules,
   error messages, database columns, or any test assertion (confirmed —
   the test suite asserts against `nameof(model.Author)` etc., property
   names, never display names).

**Icons — how they were added:** Bootstrap Icons was loaded from `cdnjs`
as a single pinned-version stylesheet link in `_Layout.cshtml`'s `<head>`;
icons are plain `<i class="bi bi-...">` elements with `aria-hidden="true"`
(decorative, since the adjacent text already conveys the meaning to screen
readers). This is a font/CSS-only icon library with no JavaScript
dependency and no build step — it does not make the project depend on
React/Angular/Vue/Tailwind or any other frontend framework, consistent with
my explicit constraint.

**Commands run:**
```
dotnet build          (run after every meaningful batch of view changes)
dotnet test Tests/LibraryManagementSystem.Tests.csproj
dotnet run             (to visually verify in a real browser)
```

**Automated verification:**
- `dotnet build` → **Build succeeded, 0 warnings, 0 errors** (checked
  repeatedly through the session, not just at the end).
- `dotnet test` → **Passed! Failed: 0, Passed: 31, Skipped: 0, Total: 31**
  — the full pre-existing suite (business rules, staff management,
  concurrency) passed unchanged, confirming this round did not alter any
  tested behaviour.
- A `curl` sweep of every major page across all three roles plus the public
  portal (`/`, `/Account/Login`, `/Account/AccessDenied`, `/Items`,
  `/Items/Create`, `/Items/Edit/1`, `/Items/Delete/1`, `/Staff`,
  `/Staff/Create`, `/Loans`, `/Loans/Borrow`, `/Borrowers`,
  `/Borrowers/Create`, `/Manager`) confirmed every page still returns
  **HTTP 200** after the markup changes — no Razor rendering errors were
  introduced.

**Visual verification performed by Claude Code (via the `claude-in-chrome`
browser automation skill available during the session — actual
screenshots taken in a real Chrome browser, not just HTTP status checks):**
- Public homepage at desktop width (1400px): hero, search panel with icon,
  item cards with coloured type badges and status icons, footer — all
  rendered as designed.
- Public homepage at mobile width (390px): header/nav wraps correctly,
  search panel and its buttons stack to full width, item cards stack to a
  single column — confirmed usable, nothing overflows or breaks.
- Admin `/Items` (table with new pill action buttons) and `/Items/Create`
  (form with the new "BOOK DETAILS" section label) — both rendered
  correctly at desktop width.
- `/Account/AccessDenied` — confirmed the icon and "Return to Home" button
  render correctly (this page's actual navigation fix was made in the
  previous session; this round only re-skinned it).
- `/Account/Login` — confirmed the icon-labelled demo-credential rows.
- Logged in as `manager` and viewed `/Manager` — confirmed the four
  `.section-block` panels, icon headings, and that the "Overdue loans" and
  "Outstanding" stat cards correctly show the red `.stat-warn` accent
  (since the seeded data has non-zero values in both), while the other
  stat cards show the normal gold accent.
- Logged in as `reception` and viewed `/Loans` — confirmed overdue rows
  show both the red background tint **and** an explicit "⚠ Overdue"
  text+icon tag, and that paid vs. outstanding fines render as visually
  distinct green/amber-red pills.
- Submitted `/Loans/Borrow` with an invalid library code and no borrower
  selected — confirmed the client-side validation error renders with the
  themed warning icon and colour, consistent with the rest of the form
  system.
- Logged in as `admin` and viewed `/Staff` — confirmed the staff table's
  Edit/Disable/Delete pill buttons render correctly and consistently with
  the Items table.

No database mutation occurred during this session's testing (confirmed via
`sqlite3` row counts before and after) — the `curl` sweep used only `GET`
requests plus read-only login, and the one `Borrow` form submission tested
was deliberately invalid (never reached loan creation), so no database
reset was needed at the end of this round.

**Result: PASS.** Functionality preserved and confirmed via the full
automated test suite (unchanged, 31/31) and a live page-rendering sweep;
the visual/UX changes were verified directly in a real browser at both
desktop and mobile widths, not merely assumed from reading the CSS.

**References used:**
- Bootstrap Icons documentation (icon names/classes used throughout:
  `bi-book`, `bi-disc`, `bi-puzzle`, `bi-check-circle-fill`,
  `bi-clock-history`, `bi-exclamation-triangle-fill`, `bi-x-octagon-fill`,
  `bi-journal-bookmark-fill`, `bi-person-circle`, `bi-box-arrow-right`,
  `bi-shield-lock`, and others) — used purely as a CSS/font icon set, no
  JavaScript API from that library was used.
- Microsoft Learn — "Model validation and Display attributes in ASP.NET
  Core MVC" — confirms `[Display(Name = "...")]` only affects the rendered
  label/error text via `ModelMetadata`, not model binding, validation
  rules, or persistence — the basis for the two label fixes described
  above being safe, presentation-only changes.
- WCAG 2.1 Success Criterion 1.4.1 ("Use of Color") — the basis for adding
  the explicit "Overdue" text tag and status icons rather than relying on
  row/badge colour alone to convey state.

**Next step:** None outstanding from this round. I planned to
visually review the theme in my own browser (a short page checklist was
provided separately) before final submission.

---

## References (final, consolidated)

- Microsoft Learn — "Introduction to Identity on ASP.NET Core" —
  `AddIdentity`, `IdentityUser`, `IdentityDbContext` usage pattern.
- Microsoft Learn — "Create an ASP.NET Core app with user data protected by
  authorization" — role-based `[Authorize]` with Identity, `UserManager`/
  `RoleManager` seeding pattern.
- Microsoft Learn — "Manage users in ASP.NET Core Identity" and "Lockout in
  ASP.NET Core Identity" — `UserManager.SetLockoutEnabledAsync`/
  `SetLockoutEndDateAsync`/`IsLockedOutAsync` as the standard account-disable
  mechanism.
- Microsoft Learn — "Reset passwords in ASP.NET Core Identity" —
  `GeneratePasswordResetTokenAsync` + `ResetPasswordAsync` as the standard
  administrator password-reset mechanism.
- Microsoft Learn — "EF Core migrations" — generating and applying
  migrations with `dotnet ef migrations add` / `dotnet ef database update`,
  and SQLite EF Core provider notes on migration support.
- Microsoft Learn — "Handle concurrency conflicts" (EF Core) — the
  optimistic concurrency model (`IsConcurrencyToken`,
  `DbUpdateConcurrencyException`) used for the borrow-race protection.
- Microsoft Learn — "EF Core providers — In-memory database provider" —
  documents the InMemory provider's limitations for testing
  provider-specific/relational behaviour; this is the documented reason the
  concurrency test was rewritten to use a real SQLite database instead.
- Microsoft Learn — "SQLite EF Core database provider — Limitations" —
  confirms SQLite has no native rowversion/timestamp column type, supporting
  the choice of a plain property configured as a concurrency token instead
  of a SQL Server–style `[Timestamp] byte[]` column.
- Microsoft Learn — "Testing controller logic in ASP.NET Core" — general
  approach to unit-testing MVC controllers directly (used throughout
  `Tests/`).

## AI use disclosure (final)

Two AI tools were used across the life of this project, as permitted by the
assignment. As described in the earlier retrospective entries
above: ChatGPT was used earlier in development to help scaffold
the initial project structure and starter implementation from the
assignment brief. From the Identity migration onward, Claude Code
(Anthropic) was used to: audit the existing codebase; explain ASP.NET
Core Identity/`IdentityDbContext`/EF Core migration concepts; write and
modify C# and Razor source files; generate and apply EF Core migrations;
write the automated xUnit test suite (`Tests/`); and perform programmatic
verification (`curl` against the running app, direct `sqlite3` queries)
of behaviour that was not exercised by the automated tests. Every code
change reported as working in this log was actually built
(`dotnet build`) and, where applicable, actually tested (`dotnet test`) or
actually exercised via a live HTTP/database check in the same session —
none of the results recorded here are predicted or simulated. Genuine
problems encountered along the way (build failures, a role-seeding bug in
the test suite, and — notably — an initial concurrency test that gave a
false-positive "it works" result under the EF Core InMemory provider) are
recorded rather than omitted, including how each was diagnosed and fixed.

Manual browser testing recorded in this log (Tests 1–3 in the "Manual
browser testing session" entry, and the visual-theme confirmation) reflects
only what I personally performed and reported in my own
browser — my manual testing was never fabricated or
assumed on my behalf. All other verification in this log (build/test
output, `curl`/`sqlite3` checks) was performed directly by Claude Code and
is labelled as such throughout, per my explicit instruction to
keep the two kinds of testing clearly distinguished.
