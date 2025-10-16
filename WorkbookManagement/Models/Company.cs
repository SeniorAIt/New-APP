using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    public class Company
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? ContactEmail { get; set; }

        [StringLength(200)]
        public string? ContactPhone { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

        // Stored JSON for the Organisation Info profile (same shape as Workbook 1)
        public string? OrgInfoJson { get; set; }

        // When the Org Info profile was last updated (UTC)
        public DateTime? OrgInfoUpdatedAtUtc { get; set; }
    }
}
