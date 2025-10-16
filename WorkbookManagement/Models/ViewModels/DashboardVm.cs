using System;
using System.Collections.Generic;
using WorkbookManagement.Models;

namespace WorkbookManagement.Models
{
    public class DashboardVm
    {
        // Calendar & announcements
        public int Year { get; set; }
        public int Month { get; set; }
        public List<Announcement> Announcements { get; set; } = new();
        public List<CalendarEvent> Events { get; set; } = new();

        // Company user cards
        public List<RecentDocVm> RecentDocuments { get; set; } = new();
        public SubmissionStatusSummary SubmissionSummary { get; set; } = new();

        // SuperAdmin-only cards
        public List<PendingReviewVm> PendingReviews { get; set; } = new();
        public List<DecisionVm> RecentDecisions { get; set; } = new();
    }

    public class RecentDocVm
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;        // OriginalFileName
        public string ContentType { get; set; } = "application/octet-stream";
        public long SizeBytes { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public string? UploadedBy { get; set; }                     // email or username
        public string? CompanyName { get; set; }                    // for SuperAdmin context
    }

    public class SubmissionStatusSummary
    {
        public int Draft { get; set; }
        public int InProgress { get; set; }
        public int Completed { get; set; }
        public int Submitted { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }

        public int Total => Draft + InProgress + Completed + Submitted + Approved + Rejected;
        public int Decided => Approved + Rejected;
        public int Undecided => Total - Decided;
        public double DecidedPercent => Total == 0 ? 0 : (double)Decided * 100.0 / Total;
    }

    // --- SuperAdmin queue item ---
    public class PendingReviewVm
    {
        public Guid Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        // We’ll order by Id desc (works fine if you don’t have submit timestamp)
    }

    // --- SuperAdmin recent decisions ---
    public class DecisionVm
    {
        public Guid Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public SubmissionBundleStatus Status { get; set; } // Approved or Rejected
        public DateTime? DecidedAtUtc { get; set; }
        public string? DecidedByEmail { get; set; }
        public string? DecisionNote { get; set; }
    }
}
