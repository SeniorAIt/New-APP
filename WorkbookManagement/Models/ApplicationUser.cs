using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    public class ApplicationUser : IdentityUser
    {
        [StringLength(200)]
        public string? CompanyName { get; set; } // optional legacy label

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Tenant link (NEW)
        public Guid? CompanyId { get; set; }
        public Company? Company { get; set; }

        // Navigation
        public ICollection<WorkbookSubmission> WorkbookSubmissions { get; set; } = new List<WorkbookSubmission>();
    }
}
