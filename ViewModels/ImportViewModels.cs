using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.ViewModels;

public class ImportPageViewModel
{
    public bool ValidateOnly { get; set; }
    public bool StopOnAnyError { get; set; }
    public ImportReport? Report { get; set; }
    public string? FileName { get; set; }
}
