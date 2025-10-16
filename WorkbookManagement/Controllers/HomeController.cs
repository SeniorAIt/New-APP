using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly TimeZoneInfo _tz; // SAST injected in Program.cs

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext db, TimeZoneInfo tz)
        {
            _logger = logger;
            _db = db;
            _tz = tz;
        }

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private async Task<Guid?> CurrentUserCompanyIdAsync()
        {
            if (CurrentUserId == null) return null;
            return await _db.Users
                .Where(u => u.Id == CurrentUserId)
                .Select(u => u.CompanyId)
                .FirstOrDefaultAsync();
        }

        // Dashboard
        [HttpGet]
        public async Task<IActionResult> Index(int? year = null, int? month = null)
        {
            var nowUtc = DateTime.UtcNow;

            // Calendar month selection (defaults to current UTC month)
            int y = year ?? nowUtc.Year;
            int m = month ?? nowUtc.Month;

            var firstOfMonthUtc = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonthUtc = firstOfMonthUtc.AddMonths(1);

            var companyId = await CurrentUserCompanyIdAsync();

            // Announcements: global OR company-specific; and not expired
            var announcements = await _db.Announcements
                .Where(a =>
                    (a.CompanyId == null || (companyId != null && a.CompanyId == companyId)) &&
                    (!a.ExpiresAtUtc.HasValue || a.ExpiresAtUtc > nowUtc))
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(10)
                .AsNoTracking()
                .ToListAsync();

            // Calendar events this month (global or company)
            var eventsThisMonth = await _db.CalendarEvents
                .Where(e =>
                    (e.CompanyId == null || (companyId != null && e.CompanyId == companyId)) &&
                    e.StartUtc >= firstOfMonthUtc &&
                    e.StartUtc < nextMonthUtc)
                .OrderBy(e => e.StartUtc)
                .AsNoTracking()
                .ToListAsync();

            // Recent documents (last 6) — only relevant to company users; SuperAdmin sees all
            var docsQuery = _db.CompanyDocuments
                .Include(d => d.UploadedByUser)
                .Include(d => d.Company)
                .AsQueryable();

            if (User.IsInRole("SuperAdmin"))
            {
                // all companies
            }
            else if (companyId != null)
            {
                docsQuery = docsQuery.Where(d => d.CompanyId == companyId);
            }
            else
            {
                docsQuery = docsQuery.Where(d => false); // no company -> no docs
            }

            var recentDocs = await docsQuery
                .OrderByDescending(d => d.UploadedAtUtc)
                .Take(6)
                .Select(d => new RecentDocVm
                {
                    Id = d.Id,
                    FileName = d.OriginalFileName,
                    ContentType = d.ContentType ?? "application/octet-stream",
                    SizeBytes = d.SizeBytes,
                    UploadedAtUtc = d.UploadedAtUtc,
                    UploadedBy = d.UploadedByUser!.Email ?? d.UploadedByUser.UserName,
                    CompanyName = d.Company!.Name
                })
                .AsNoTracking()
                .ToListAsync();

            // Submission status summary (for company users; SuperAdmin aggregates all)
            var subsQuery = _db.Submissions.AsQueryable();
            if (!User.IsInRole("SuperAdmin") && companyId != null)
            {
                subsQuery = subsQuery.Where(s => s.CompanyId == companyId);
            }

            var statusCounts = await subsQuery
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var summary = new SubmissionStatusSummary();
            foreach (var row in statusCounts)
            {
                switch (row.Status)
                {
                    case SubmissionBundleStatus.Draft:
                        summary.Draft = row.Count; break;
                    case SubmissionBundleStatus.InProgress:
                        summary.InProgress = row.Count; break;
                    case SubmissionBundleStatus.Completed:
                        summary.Completed = row.Count; break;
                    case SubmissionBundleStatus.Submitted:
                        summary.Submitted = row.Count; break;
                    case SubmissionBundleStatus.Approved:
                        summary.Approved = row.Count; break;
                    case SubmissionBundleStatus.Rejected:
                        summary.Rejected = row.Count; break;
                }
            }

            // --- SUPERADMIN ADDITIONS ---

            List<PendingReviewVm> pending = new();
            List<DecisionVm> recentDecisions = new();

            if (User.IsInRole("SuperAdmin"))
            {
                // Review queue = Submissions with Status == Submitted (latest first)
                pending = await _db.Submissions
                    .Include(s => s.Company)
                    .Include(s => s.OwnerUser)
                    .Where(s => s.Status == SubmissionBundleStatus.Submitted)
                    .OrderByDescending(s => s.Id)
                    .Take(8)
                    .Select(s => new PendingReviewVm
                    {
                        Id = s.Id,
                        CompanyName = s.Company != null ? s.Company.Name : "(Unknown)",
                        OwnerEmail = s.OwnerUser != null
                            ? (s.OwnerUser.Email ?? s.OwnerUser.UserName)
                            : "(Unknown)"
                    })
                    .AsNoTracking()
                    .ToListAsync();

                // Recent decisions = Approved/Rejected (most recent)
                recentDecisions = await _db.Submissions
                    .Include(s => s.Company)
                    .Include(s => s.OwnerUser)
                    .Include(s => s.DecidedByUser)
                    .Where(s => s.Status == SubmissionBundleStatus.Approved ||
                                s.Status == SubmissionBundleStatus.Rejected)
                    .OrderByDescending(s => s.DecidedAtUtc)
                    .Take(8)
                    .Select(s => new DecisionVm
                    {
                        Id = s.Id,
                        CompanyName = s.Company != null ? s.Company.Name : "(Unknown)",
                        OwnerEmail = s.OwnerUser != null
                            ? (s.OwnerUser.Email ?? s.OwnerUser.UserName)
                            : "(Unknown)",
                        Status = s.Status,
                        DecidedAtUtc = s.DecidedAtUtc,
                        DecidedByEmail = s.DecidedByUser != null
                            ? (s.DecidedByUser.Email ?? s.DecidedByUser.UserName)
                            : null,
                        DecisionNote = s.DecisionNote
                    })
                    .AsNoTracking()
                    .ToListAsync();
            }

            var vm = new DashboardVm
            {
                Year = y,
                Month = m,
                Announcements = announcements,
                Events = eventsThisMonth,
                RecentDocuments = recentDocs,
                SubmissionSummary = summary,
                PendingReviews = pending,
                RecentDecisions = recentDecisions
            };

            ViewBag.DisplayTimeZone = _tz; // Africa/Johannesburg from Program.cs

            return View(vm);
        }



        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
            => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
