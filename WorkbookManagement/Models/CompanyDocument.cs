using System;
using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    // Canonical list for the dropdown & filtering
    public enum DocumentKind
    {
        [Display(Name = "-")]
        Unknown = 0,

        [Display(Name = "Director ID")]
        DirectorId = 1,

        [Display(Name = "CoR39")]
        CoR39 = 2,

        [Display(Name = "Accreditation Letter")]
        AccreditationLetter = 3,

        [Display(Name = "Share Certificate")]
        ShareCertificate = 4,

        [Display(Name = "OHS Certificate")]
        OHSCertificate = 5,

        [Display(Name = "OHS Audit Report")]
        OHSAuditReport = 6,

        [Display(Name = "Lease Agreement")]
        LeaseAgreement = 7,

        [Display(Name = "SARS Pin Letter")]
        SARSPinLetter = 8
    }

    public class CompanyDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CompanyId { get; set; }
        public Company Company { get; set; } = default!;

        public string UploadedByUserId { get; set; } = default!;
        public ApplicationUser UploadedByUser { get; set; } = default!;

        public string OriginalFileName { get; set; } = default!;
        public string StoredFileName { get; set; } = default!;

        public string? ContentType { get; set; }
        public long SizeBytes { get; set; }

        // Stored as int by EF (enum). Migration should already match.
        public DocumentKind DocumentType { get; set; } = DocumentKind.Unknown;

        public string? Notes { get; set; }

        public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
