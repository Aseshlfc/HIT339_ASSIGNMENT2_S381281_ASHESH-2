   # Section 3 Notes - Reservations & Notifications

   ## 2026-10-11
   - Cloned Ashesh's repo, installed .NET 8 SDK, ran the app on localhost:5190.
   - Created branch feature/section3-reservations-notifications.
- Created Notification model (Models/Notification.cs) with channel and type enums.
- Added Notifications DbSet to LibraryContext and created the AddNotifications migration.
- Borrower already has Email and Phone, so no model change was needed.
- Created NotificationService (Services/NotificationService.cs) behind an INotificationService interface, registered in Program.cs. Every feature sends notifications through this one method.
- Tested by borrowing B002 as Reception: a Borrowed notification was saved to the Notifications table and logged to the console.
- Added NotificationsController and Views/Notifications/Index.cshtml: a Reception-only dashboard listing notifications newest first (latest 200), with a filter by type. Menu link added in _Layout.cshtml. Styled with the existing site.css classes to match the rest of the app.