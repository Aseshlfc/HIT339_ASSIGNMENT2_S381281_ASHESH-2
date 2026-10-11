namespace LibraryManagementSystem.ViewModels;

public class StaffListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public bool IsLockedOut { get; set; }
}
