using System;
using System.Text.Json.Serialization;

namespace WorkbookManagement.Models
{
    // This partial adds signature fields to the existing QaInfo model used on Step 4.
    public partial class QaInfo
    {
        // Store as data URLs (e.g., "data:image/png;base64,...") for direct <img src=...> rendering.
        [JsonPropertyName("orgRepresentativeSignature")]
        public string? OrgRepresentativeSignature { get; set; }

        [JsonPropertyName("assessorSignature")]
        public string? AssessorSignature { get; set; }

        [JsonPropertyName("orgSignedAtUtc")]
        public DateTime? OrgSignedAtUtc { get; set; }

        [JsonPropertyName("assessorSignedAtUtc")]
        public DateTime? AssessorSignedAtUtc { get; set; }
    }
}
