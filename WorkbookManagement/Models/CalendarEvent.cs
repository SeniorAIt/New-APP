using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkbookManagement.Models
{
    public class CalendarEvent
    {
        [Key] public Guid Id { get; set; }

        // Optional scoping: null = visible to all companies
        public Guid? CompanyId { get; set; }
        [ForeignKey(nameof(CompanyId))] public Company? Company { get; set; }

        [Required, MaxLength(160)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        public bool AllDay { get; set; }

        [Required] public DateTime StartUtc { get; set; }
        public DateTime? EndUtc { get; set; } // if null & not AllDay, treat as 1 hour in UI

        // Who created (SuperAdmin)
        [Required] public string CreatedByUserId { get; set; } = string.Empty;
        [ForeignKey(nameof(CreatedByUserId))] public ApplicationUser? CreatedByUser { get; set; }

        [MaxLength(40)]
        public string? Category { get; set; }
    }
}
