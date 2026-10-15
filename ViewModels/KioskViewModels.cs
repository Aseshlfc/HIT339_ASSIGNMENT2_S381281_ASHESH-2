using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

/// <summary>Posted from the kiosk welcome screen.</summary>
public class KioskCardForm
{
    public string? CardNumber { get; set; }
}

/// <summary>Posted from the account screen to borrow one item.</summary>
public class KioskCheckoutForm
{
    public string? CardNumber { get; set; }
    public string? LibraryCode { get; set; }
}

public class KioskWelcomeViewModel
{
    public required Branch Branch { get; init; }
    public string? CardNumber { get; init; }
    public string? Error { get; init; }

    /// <summary>True when the patron has just finished, or the idle timer signed them out.</summary>
    public bool SignedOut { get; init; }
}

public class KioskAccountViewModel
{
    public required Branch Branch { get; init; }
    public required KioskAccountSummary Account { get; init; }

    /// <summary>Result of the checkout attempt that led to this screen, if any.</summary>
    public string? Message { get; init; }
    public bool MessageIsError { get; init; }

    /// <summary>Echoed back into the code box after a failed attempt.</summary>
    public string? LibraryCode { get; init; }
}
