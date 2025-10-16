// File: Models/Workbooks/Submission.cs
using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    // Keep this name to avoid clashing with WorkbookSubmission.SubmissionStatus
    public enum SubmissionBundleStatus
    {
        Draft = 0,        // created
        InProgress = 1,   // user is working on workbooks
        Completed = 2,    // all required workbooks marked Completed
        Submitted = 3,    // user submitted the bundle
        Approved = 4,     // super admin approved
        Rejected = 5      // super admin rejected
    }

    public class Submission
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string OwnerUserId { get; set; } = default!;
        public ApplicationUser OwnerUser { get; set; } = default!;

        [Required]
        public Guid CompanyId { get; set; }
        public Company Company { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public SubmissionBundleStatus Status { get; set; } = SubmissionBundleStatus.Draft;

        // --- Admin decision metadata (optional but recommended) ---
        [MaxLength(2000)]
        public string? DecisionNote { get; set; }

        public string? DecidedByUserId { get; set; }
        public ApplicationUser? DecidedByUser { get; set; }

        public DateTime? DecidedAtUtc { get; set; }

        // Convenience: true if Approved/Rejected
        public bool IsTerminal => Status == SubmissionBundleStatus.Approved || Status == SubmissionBundleStatus.Rejected;

        public ICollection<WorkbookSubmission> Workbooks { get; set; } = new List<WorkbookSubmission>();
    }
}
