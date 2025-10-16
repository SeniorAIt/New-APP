using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkbookManagement.Models
{
    public class Announcement
    {
        [Key] public Guid Id { get; set; }

        // (Legacy) Single-company column – kept for backward compatibility,
        // but multi-company now uses the Targets join table.
        public Guid? CompanyId { get; set; }
        [ForeignKey(nameof(CompanyId))] public Company? Company { get; set; }

        [Required, MaxLength(160)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Body { get; set; } = string.Empty;

        [Required] public string AuthorUserId { get; set; } = string.Empty;
        [ForeignKey(nameof(AuthorUserId))] public ApplicationUser? AuthorUser { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAtUtc { get; set; }

        // ===== Optional attachment =====
        [MaxLength(260)] public string? AttachmentFileName { get; set; }
        [MaxLength(400)] public string? AttachmentPath { get; set; }
        [MaxLength(100)] public string? AttachmentContentType { get; set; }
        public long? AttachmentSizeBytes { get; set; }
        public DateTime? AttachmentUploadedAtUtc { get; set; }
        [NotMapped] public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentPath);

        // ===== NEW: multi-company targeting (empty => Global) =====
        public ICollection<AnnouncementCompany> Targets { get; set; } = new List<AnnouncementCompany>();
        [NotMapped] public bool IsTargeted => Targets != null && Targets.Count > 0;

        public bool IsActive(DateTime utcNow) => !ExpiresAtUtc.HasValue || utcNow < ExpiresAtUtc.Value;
    }

    public class AnnouncementCompany
    {
        public Guid AnnouncementId { get; set; }
        public Announcement Announcement { get; set; } = default!;

        public Guid CompanyId { get; set; }
        public Company Company { get; set; } = default!;
    }
}
