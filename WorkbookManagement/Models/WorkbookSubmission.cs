using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    public enum WorkbookType
    {
        Workbook1 = 1,
        Workbook2 = 2,
        Workbook3 = 3
    }

    /// <summary>
    /// Status of an individual workbook (not the bundle).
    /// NOTE: Existing data uses 0=Draft, 1=Submitted, 2=Approved.
    /// We append Completed=3 to avoid changing those meanings.
    /// </summary>
    public enum SubmissionStatus
    {
        Draft = 0,
        Submitted = 1,
        Approved = 2,

        // New explicit value so existing rows aren't reinterpreted
        Completed = 3
    }

    public class WorkbookSubmission
    {
        public int Id { get; set; }

        [Required] public string UserId { get; set; } = default!;
        public ApplicationUser? User { get; set; }

        // Company scoping
        [Required] public Guid CompanyId { get; set; }
        public Company? Company { get; set; }

        [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
        [Required] public WorkbookType WorkbookType { get; set; }

        /// <summary>Flexible JSON payload per workbook.</summary>
        public string? Data { get; set; }

        public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Guid? SubmissionId { get; set; }
        public Submission? Submission { get; set; }
    }
}
