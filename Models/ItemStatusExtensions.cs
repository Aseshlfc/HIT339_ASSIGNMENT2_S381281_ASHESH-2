namespace LibraryManagementSystem.Models;

public static class ItemStatusExtensions
{
    public static string Label(this ItemStatus status) => status switch
    {
        ItemStatus.InTransit => "In transit",
        _ => status.ToString()
    };

    public static string Label(this TransferKind kind) => kind switch
    {
        TransferKind.Correction => "Location correction",
        TransferKind.CrossBranchReturn => "Returned at another branch",
        _ => "Transfer"
    };

    public static string Label(this TransferStatus status) => status switch
    {
        TransferStatus.InTransit => "In transit",
        _ => status.ToString()
    };
}
