using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Builds a LoansController with the real notification and reservation services,
/// so the tests keep exercising the same code path as the running application.
/// </summary>
internal static class LoansControllerFactory
{
    public static LoansController Create(LibraryContext db)
    {
        var notifications = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var reservations = new ReservationService(db, notifications);
        return new LoansController(db, notifications, reservations);
    }
}
