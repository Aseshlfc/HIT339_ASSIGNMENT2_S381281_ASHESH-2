# Windows and VM test checklist (.NET 10)

The .NET 10 upgrade was tested on macOS with Chrome only. This checklist covers what still has to be checked on Windows: on the designated VM, and in Edge and Firefox. Mark each line **Pass**, **Fail** or **Not tested**, and add notes. Do not mark anything as passed unless you actually did it.

Record first:

| Item | Value |
|---|---|
| Machine (VM name or own PC) | |
| Windows version (`winver`) | |
| `dotnet --list-sdks` | |
| Edge version | |
| Firefox version | |
| Branch and commit (`git log -1 --oneline`) | |
| Tester and date | |

## 1. Set-up, build and tests

Follow the README section "Windows (including the designated VM)", using a new database file.

| # | Check | Result |
|---|---|---|
| 1.1 | `dotnet --list-sdks` shows a `10.0.x` SDK | |
| 1.2 | `dotnet restore` and `dotnet build` finish with 0 errors | |
| 1.3 | `dotnet test Tests\LibraryManagementSystem.Tests.csproj`: all tests pass (374 at the time of writing) | |
| 1.4 | First `dotnet run` on a new database file starts without errors, and the console shows the migrations being applied | |
| 1.5 | http://localhost:5190 opens the public catalogue | |
| 1.6 | Optional: `dotnet ef migrations has-pending-model-changes` reports no changes | |
| 1.7 | Optional: with a copy of an existing `library.db`, the app starts and existing borrowers, items and loans are still there | |

## 2. Browsers: do these in both Edge and Firefox

| # | Check | Edge | Firefox |
|---|---|---|---|
| 2.1 | Public search: a word, a library code, the item type, branch and "available only" filters, and a search with no results | | |
| 2.2 | Staff login: a wrong password shows "Invalid username or password"; Reception, Admin and Manager each land on their own start page | | |
| 2.3 | Keyboard only: Tab moves through the menu and forms with a visible focus outline; Enter submits | | |
| 2.4 | Phone width (device toolbar, 375 px): no sideways scrolling on the catalogue, kiosk, Loans and Borrowers pages | | |
| 2.5 | No errors in the browser console (F12) on the pages above | | |

## 3. Kiosk PIN (both browsers)

As Reception, set a 6-digit PIN for a demo borrower on **Borrowers**. Choose your own PIN and do not write it in this file.

| # | Check | Edge | Firefox |
|---|---|---|---|
| 3.1 | Borrowers shows "Not set", then "Set" after saving; the PIN is never displayed | | |
| 3.2 | The kiosk PIN box shows dots, opens a number keypad on touch screens, and is **not** filled in by the browser's saved passwords | | |
| 3.3 | After signing in, the browser does not offer to save the card number and PIN. If it does, note it: the kiosk browser's password saving must be turned off (see Susan's user guide) | | |
| 3.4 | Scanning or typing the card number then pressing Enter moves to the PIN box | | |
| 3.5 | Wrong PIN: "That card number and PIN do not match". The fifth wrong PIN locks the card for 15 minutes, and Borrowers shows "Locked until …" | | |
| 3.6 | Reset the PIN as Reception or Admin: the card works again at once | | |
| 3.7 | Borrow an available item: a success message appears, and a "Borrowed" notification appears on the Notifications page | | |
| 3.8 | Finish and sign out, then Back and Refresh: the account never reappears ("Your session ended") | | |
| 3.9 | Wait 60 seconds on the account page: it signs out by itself | | |
| 3.10 | Back to Main Site opens the public catalogue | | |

## 4. Staff journeys (one browser is enough)

| # | Check | Result |
|---|---|---|
| 4.1 | Reception: lend an item at the desk; lending an item that is already on loan is refused | |
| 4.2 | Reception: place a hold on a borrowed item, return the item, and check the hold becomes Ready with a "ReservationReady" notification | |
| 4.3 | Reception: return an overdue item; check the fine and the "FineIssued" notification, then mark the fine paid | |
| 4.4 | Transfers: request, dispatch, then receive at the destination (as Admin) | |
| 4.5 | Admin: CSV import with `docs\samples\items-import-valid.csv`, first with "check only", then for real; then with `items-import-with-errors.csv` | |
| 4.6 | Admin can open Borrowers only to set PINs; Edit and Delete are refused. Manager cannot open Borrowers | |
| 4.7 | Manager: open all three reports, apply a date range and a branch filter, and export each report as CSV and as PDF | |

## 5. PDF fonts on Windows

Reports use the bundled Lato font for Latin, Greek and Cyrillic, and fonts installed on Windows for other scripts: Microsoft YaHei (Chinese), Yu Gothic (Japanese), Malgun Gothic (Korean), Nirmala UI (Hindi and other Indian scripts), Leelawadee UI (Thai), Segoe UI (Arabic and Hebrew) and Segoe UI Emoji. A minimal Windows install, such as Server Core, may not have them, and those characters then appear as empty boxes.

| # | Check | Result |
|---|---|---|
| 5.1 | As Reception, add a borrower named `Zoë Ng 张伟 नमस्ते مرحبا Ελένη Жанна 😀` and lend them an item | |
| 5.2 | As Manager, export the Borrowing report as PDF and open it. The name appears with no empty boxes, and the Arabic word reads right to left | |
| 5.3 | The CSV export of the same report opens in Excel with the name shown correctly | |
| 5.4 | Any PDF error, or a message about a missing Visual C++ runtime, is noted with the full text | |

## 6. Report back

Send the completed tables, the output of `dotnet test`, and screenshots of anything marked **Fail**.
