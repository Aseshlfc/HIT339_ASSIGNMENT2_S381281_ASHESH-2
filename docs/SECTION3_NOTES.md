   # Section 3 Notes - Reservations & Notifications

   ## 2026-10-11
   - Cloned Ashesh's repo, installed .NET 8 SDK, ran the app on localhost:5190.
   - Created branch feature/section3-reservations-notifications.
- Created Notification model (Models/Notification.cs) with channel and type enums.
- Added Notifications DbSet to LibraryContext and created the AddNotifications migration.
- Borrower already has Email and Phone, so no model change was needed.
- Created NotificationService (Services/NotificationService.cs) behind an INotificationService interface, registered in Program.cs. Every feature sends notifications through this one method.