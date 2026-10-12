# Section 3 Notes - Reservations & Notifications

## 2026-10-11
- Cloned Ashesh's repo, installed .NET 8 SDK, ran the app on localhost:5190.
- Created branch feature/section3-reservations-notifications.
- Created Notification model (Models/Notification.cs) with channel and type enums.
- Added Notifications DbSet to LibraryContext and created the AddNotifications migration.
- Borrower already has Email and Phone, so no model change was needed.
- Created NotificationService (Services/NotificationService.cs) behind an INotificationService interface, registered in Program.cs. Every feature sends notifications through this one method.
- Tested by borrowing B002 as Reception: a Borrowed notification was saved to the Notifications table and logged to the console.

## 2026-10-12
- Added NotificationsController and Views/Notifications/Index.cshtml: a Reception-only dashboard listing notifications newest first (latest 200), with a filter by type. Menu link added in _Layout.cshtml. Styled with the existing site.css classes to match the rest of the app.
- Added Reservation model (Models/Reservation.cs) with a status enum (Waiting, Ready, Fulfilled, Cancelled, Expired), linked to Item and Borrower. Queue position is derived from CreatedAt order among Waiting rows. ReadyAt is stored now so hold expiry can be added without another migration.
- Created the AddReservations migration (creates only the Reservations table).
- Added ReservationsController, ReserveViewModel and Views/Reservations (Index, Create): Reception places and cancels holds. Holds are only allowed on Borrowed or Damaged items. Validation blocks holds on Available, Destroy and In-transit items, duplicate active holds, and holds by a borrower who already has the item on loan.
- Validation blocks holds on Available, Destroy and In-transit items, duplicate active holds, and holds by a borrower who already has the item on loan. Cancelling is limited to Waiting holds; Ready holds are handled by the return flow (Stage 7).
- The Create form reuses the layout of Loans/Borrow (form-card, input-icon-wrap) so the screens look consistent.
- Tested the five hold cases manually and saved screenshots 01 to 08 in docs/screenshots.
- Changing the LoansController constructor broke 26 call sites in Ashesh's test project. Fixed by adding Tests/LoansControllerFactory.cs, which builds the controller with the real NotificationService and ReservationService, and swapping every call to use it. Why: the existing tests keep exercising the real code path, and no test was removed or weakened.
- Test run after the change: 162 of 163 pass. The one failure (Borrow_ConcurrentAttemptsForSameItem_OnlyOneSucceeds) also fails on a clean copy of main on Windows, because the test cannot delete its temporary SQLite file at cleanup. It was reported to Ashesh and is not caused by Section 3.