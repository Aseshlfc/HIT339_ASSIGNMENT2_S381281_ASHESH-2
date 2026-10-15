# Section 2 Notes - Kiosk, API & Import

## Where this branch sits
`feature/section2-kiosk-api-import` is built **on top of the Section 3 branch** (`feature/section3-reservations-notifications`, PR #3), which itself includes Section 1 from main. It was stacked on Section 3 because the kiosk uses Section 3's `INotificationService` and reservations (Ready holds, queue position). **Merge order: Section 3 first, then Section 2.** If Section 3 is merged into main first, this branch needs no rebase work (it already contains Section 3's commits).

## No migration
Section 2 adds no tables or columns, so there is **no migration** and no snapshot conflict. The library card number is not stored: `Borrower.CardNumber` is `[NotMapped]` and derived from the Id (`P0007`).

> **Integration update (15 October 2026):** the kiosk PIN added during integration needs one migration, `AddKioskPin`, which adds four nullable or zero-default columns to `Borrowers`. The card number is still not stored. See the README's Integration notes.

## Files added
- `Services/KioskService.cs`, `Controllers/KioskController.cs`, `ViewModels/KioskViewModels.cs`
- `Views/Kiosk/` (Index, Welcome, Account, BranchNotFound), `Views/Shared/_KioskLayout.cshtml`, `wwwroot/css/kiosk.css`
- `Api/PublicApiOptions.cs`, `Api/ApiKeyFilter.cs`, `Api/CatalogueDtos.cs`, `Controllers/CatalogueApiController.cs`
- `Services/CsvReader.cs`, `Services/ItemImportService.cs`, `Controllers/ImportController.cs`, `ViewModels/ImportViewModels.cs`, `Views/Import/Index.cshtml`
- Tests: `KioskServiceTests.cs`, `KioskControllerTests.cs`, `CatalogueApiTests.cs`, `ItemImportServiceTests.cs`
- Docs: this file, `SECTION2_REQUIREMENTS.md`, `SECTION2_USER_GUIDE.md`, `SECTION2_API.md`, `SECTION2_USER_JOURNEYS.md`, `section2-images/`, `samples/`

## Shared files changed (watch for merge conflicts)
| File | Change | If it conflicts |
|---|---|---|
| `Program.cs` | Two service registrations, `PublicApi` options, `ApiKeyFilter`, the rate limiter and `app.UseRateLimiter()` after `UseRouting()` | Keep both sides; the order `UseRouting`, `UseRateLimiter`, `UseAuthentication` must stay |
| `appsettings.json` | `PublicApi` section | Keep both sides |
| `Models/Borrower.cs` | `CardNumber`, `FormatCardNumber`, `TryParseCardNumber` (not mapped) | Keep both sides |
| `Views/Shared/_Layout.cshtml` | "Kiosk" link for everyone, "Import" link for Admin | Keep all links |
| `Views/Borrowers/Index.cshtml` | "Card number" column | Keep the column |
| `wwwroot/css/site.css` | Import result styles appended | Keep both sides |

`LoansController` is **not** touched.

## Decisions
- **Rules in a service, not in `LoansController`.** Section 3 had already changed `LoansController`, so the kiosk rules live in `KioskService`. They mirror `Borrow` (Available only, same branch, Ready hold respected, 14-day loan, Borrowed notification). The trade-off is that the rules exist in two places; a later refactor could make the desk and the kiosk call one shared method.
- **Branch check on the kiosk.** Section 1 says an item belongs to one branch. A kiosk only lends items held at its own branch.
- **API key in header only**, compared in constant time; rate limit with ASP.NET's built-in limiter (no new packages).
- **CSV reader written by hand** (RFC 4180: quoted fields, commas, doubled quotes, line breaks inside quotes) to avoid adding a NuGet package.
- **Import saves in a single `SaveChanges`**, so it is all-or-nothing at the database level. A race on a duplicate code is caught and reported.
- **SQLite and decimals.** SQLite cannot compare or sum `decimal` in SQL, so unpaid fines are filtered and summed in memory.

## Tests
Section 2 adds 58 tests (kiosk service and controller, card number parsing, API controller and key filter, CSV reader, import service and controller). Run:

```bash
dotnet test Tests/LibraryManagementSystem.Tests.csproj
```

Manual end-to-end checks on a real SQLite database: API authentication, filters, paging, error codes, 405 and 429; kiosk checkout, wrong branch, Ready hold and fines; import dry run, stop-on-error, duplicates and odd files, Admin-only access. Screenshots are in `section2-images/`.

## Known limitations
- **L1. No PIN.** The card number is derived from the borrower Id, so it is guessable. Anyone who knows a number can borrow on that card. A real deployment would add a PIN or a barcode secret (which needs a migration).
  *Integration update (15 October 2026):* addressed. The kiosk now needs a 6-digit PIN set by Reception or Admin, with a lockout after 5 wrong tries (migration `AddKioskPin`).
- **L2.** Fines cannot be paid, items cannot be returned and holds cannot be placed at the kiosk.
- **L3.** API keys live in configuration; there is no key management page.
- **L4.** Import adds items only (no update) and accepts UTF-8 CSV only.
- **L5.** The rate limiter is per server process (in memory), which is fine for one instance.
- **L6.** The repository contains committed `bin/`, `obj/` and `library.db` files, which cause noisy diffs. Worth adding a `.gitignore` as a team.
- **L7.** The project targets `net8.0` while the brief's README mentions .NET 10; the team should agree which to state.
