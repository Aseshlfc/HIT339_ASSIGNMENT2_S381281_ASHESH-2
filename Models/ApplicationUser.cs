using Microsoft.AspNetCore.Identity;

namespace LibraryManagementSystem.Models;

public class ApplicationUser : IdentityUser
{
    // The branch (reception desk) this staff member works at. Optional.
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
}
