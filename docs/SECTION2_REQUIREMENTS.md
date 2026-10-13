# Section 2 Requirements - Public Kiosk, REST API & Data Import

Author: Susan Acharya (S383819). Branch: `feature/section2-kiosk-api-import`.

## 1. Purpose
Section 2 adds three horizontal features to the Library Management System without changing how the existing desk workflow behaves:

1. A **self-service kiosk** where patrons identify themselves with a library card, see their account and borrow items.
2. A **read-only REST API** that lets other software read the catalogue, categories and operating status.
3. A **CSV bulk import** that lets an Admin add many items at once, with clear feedback on every row.

## 2. Users
| User | What they want |
|---|---|
| Patron (library member) | Borrow an item and check what they owe without queuing at the desk. |
| Developer / partner | Read live catalogue data from their own app, safely. |
| Admin | Add a supplier's stock in one go and know exactly which rows are wrong. |
| Reception / Manager | Find a patron's card number, and be sure kiosk loans look the same as desk loans. |

## 3. Assumptions
- **A1.** The kiosk is a public page, so it needs no staff login. Each kiosk is opened at a branch address such as `/kiosk/CEN`.
- **A2.** A patron's card number is derived from their borrower record (`P` plus the Id padded to 4 digits, for example `P0007`). This means no database change and no migration, which avoids conflicts with Sections 1 and 3. It also means the card is an identifier, not a secret; see limitation L1.
- **A3.** API keys are issued by editing `appsettings.json` (or an environment variable). There is no self-service sign-up.
- **A4.** The API is for server-to-server use, so browser cross-origin access (CORS) is off.
- **A5.** CSV files are UTF-8 text (Excel: Save As, "CSV UTF-8").
- **A6.** Imports only add new items. Updating an existing item by CSV is out of scope.

## 4. Functional requirements

### 4.1 Kiosk (K)
| ID | Requirement | Brief |
|---|---|---|
| K1 | Touch-friendly layout with large buttons and inputs, usable at phone width. | simplified, touch-friendly kiosk |
| K2 | A kiosk is tied to one active branch (`/kiosk/{branchCode}`). Unknown or inactive branches show a "kiosk not available" page. | |
| K3 | A patron enters or scans a library card number. Accepted forms are `P0007`, `p7` and `7`. Anything else gives a clear message. | scan/enter a library code |
| K4 | After identifying, the patron sees their active loans (due date, overdue days, estimated fine), unpaid fines and open reservations. | view account summaries |
| K5 | The patron can borrow an item by scanning or typing its library code (not case sensitive). | check out items |
| K6 | A kiosk loan follows the same rules as a desk loan: item must be Available, must be held at the kiosk's branch, and an item held (Ready) for another patron is refused. | |
| K7 | A patron's own Ready hold becomes Fulfilled when they borrow the item. | |
| K8 | A loan is due 14 days later (the existing `LibraryRules.LoanDays`) and a Borrowed notification is sent (Section 3). | |
| K9 | Two people borrowing the same item at the same moment: only one succeeds, the other is told it was just borrowed. | |
| K10 | The kiosk signs the patron out after 60 seconds without activity and has a Finish button. | |
| K11 | All form posts are protected by anti-forgery tokens. | |
| K12 | Staff can see each patron's card number on the Borrowers page. | |

### 4.2 REST API (A)
| ID | Requirement | Brief |
|---|---|---|
| A1 | `GET /api/v1/items` searches the catalogue with filters (text, type, category, branch, status, available only) and paging. | catalogue items |
| A2 | `GET /api/v1/items/{libraryCode}` returns one item. | |
| A3 | `GET /api/v1/categories` lists item types and genres/toy types with counts. | categories |
| A4 | `GET /api/v1/status` reports whether the system is up, branch details and stock totals. Returns 503 with `"degraded"` if the database is unreachable. | operating status |
| A5 | Every call needs a valid `X-Api-Key` header. Missing or wrong key gives 401. | secure |
| A6 | Only GET is allowed. Other methods give 405. | read-only |
| A7 | Calls are limited per client address (default 60 per minute). Over the limit gives 429 with a `Retry-After` header. | secure |
| A8 | Errors are JSON in the standard problem format. No stack traces or internal details. | |
| A9 | Destroyed items are never shown. No internal database ids, borrower data or loan data are exposed. | |

### 4.3 CSV import (I)
| ID | Requirement | Brief |
|---|---|---|
| I1 | Admin-only page `/Import` with a downloadable template CSV. | CSV bulk import |
| I2 | Supports Book, Music and Toy rows. Columns are matched by header name, in any order; unknown columns are ignored with a warning. | |
| I3 | Each row is validated with the same data-annotation rules as the Admin item form, plus: valid type, valid number fields, status Available or Damaged only, existing and active branch, library code not already used in the file or in the catalogue. | data validation |
| I4 | A blank branch means the default branch (Central). | |
| I5 | "Check the file only" runs all checks and saves nothing. | import feedback |
| I6 | "Stop if any row is invalid" saves nothing unless every row is valid. Without it, valid rows are saved and invalid rows are reported. | |
| I7 | The result shows totals and, per row, the line number, item and every problem found on that row. | import feedback |
| I8 | Whole-file problems (empty, not UTF-8, missing required column, over 1 MB, over 1000 rows, broken quoting) are reported in plain words and nothing is saved. | |
| I9 | Rows are saved in one transaction so a failure part-way leaves no half-imported data. | |

## 5. Non-functional requirements
| ID | Requirement |
|---|---|
| N1 | **No migration** and no edits to `LoansController`, so Section 2 merges cleanly with Sections 1 and 3. |
| N2 | **Security.** API keys compared in constant time and accepted in the header only; rate limiting; anti-forgery on kiosk and import; Admin role on import; input size limits. |
| N3 | **Usability.** Plain-English messages; the same wording style as the rest of the app. |
| N4 | **Accessibility.** Labels on every input, visible focus, high contrast, large touch targets. |
| N5 | **Testability.** Business rules live in services (`KioskService`, `ItemImportService`, `CsvReader`) that are unit tested against a real SQLite database. |
| N6 | **Compatibility.** net8.0, SQLite, existing Identity roles; no new NuGet packages. |

## 6. Out of scope
Fine payment at the kiosk, returning items at the kiosk, a patron PIN, placing holds from the kiosk or API, write endpoints in the API, updating items by CSV, Excel (.xlsx) import.

## 7. Traceability
| Brief item | Requirements | Where to see it |
|---|---|---|
| Touch-friendly kiosk, scan/enter code, check out | K1-K11 | `/kiosk`, `KioskService`, `KioskServiceTests` |
| Account summaries | K4, K12 | Account screen, Borrowers page |
| Secure read-only API: items, categories, status | A1-A9 | `CatalogueApiController`, `CatalogueApiTests` |
| CSV bulk import, validation, feedback | I1-I9 | `/Import`, `ItemImportService`, `ItemImportServiceTests` |
| Requirements doc | this file | |
| User guide | `SECTION2_USER_GUIDE.md` | |
| User journey maps | `SECTION2_USER_JOURNEYS.md` | |
