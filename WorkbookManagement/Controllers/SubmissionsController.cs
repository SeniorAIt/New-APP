using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Controllers
{
    [Authorize]
    public class SubmissionsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public SubmissionsController(ApplicationDbContext db) => _db = db;

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsSuperAdmin => User.IsInRole("SuperAdmin");

        private async Task<Guid?> CurrentUserCompanyIdAsync()
        {
            return await _db.Users
                .Where(u => u.Id == CurrentUserId)
                .Select(u => u.CompanyId)
                .FirstOrDefaultAsync();
        }

        // Treat a workbook as "complete" when it's not Draft
        private static bool IsWorkbookComplete(WorkbookSubmission w) =>
            w.Status != SubmissionStatus.Draft;

        // Helper for in-memory checks ONLY (do not use inside EF queries)
        private static bool IsBundleTerminal(SubmissionBundleStatus s) =>
            s is SubmissionBundleStatus.Submitted
              or SubmissionBundleStatus.Approved
              or SubmissionBundleStatus.Rejected;

        private static OrgInfoData FromJsonOrDefault(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return OrgInfoData.CreateDefault();
            try { return JsonSerializer.Deserialize<OrgInfoData>(json) ?? OrgInfoData.CreateDefault(); }
            catch { return OrgInfoData.CreateDefault(); }
        }

        // GET: /Submissions
        public async Task<IActionResult> Index()
        {
            var q = _db.Submissions
                .Include(s => s.Company)
                .Include(s => s.OwnerUser)
                .Include(s => s.Workbooks)
                .OrderByDescending(s => s.CreatedAt)
                .AsQueryable();

            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid.HasValue) q = q.Where(s => s.CompanyId == cid.Value);
                else q = q.Where(s => s.OwnerUserId == CurrentUserId);
            }

            var list = await q.AsNoTracking().ToListAsync();
            return View(list);
        }

        // POST: /Submissions/Start
        // Allows multiple active bundles. WB1 is prefilled from Company Org Profile and marked Completed.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start()
        {
            var cid = await CurrentUserCompanyIdAsync();
            if (cid == null)
            {
                TempData["err"] = "Your user is not linked to a company.";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.UtcNow;

            // Create parent submission (no single-active restriction anymore)
            var submission = new Submission
            {
                CompanyId = cid.Value,
                OwnerUserId = CurrentUserId,
                Status = SubmissionBundleStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Submissions.Add(submission);
            await _db.SaveChangesAsync();

            // Fetch latest Org Info profile and normalize JSON
            var profile = await _db.Companies
                .AsNoTracking()
                .Where(c => c.Id == cid.Value)
                .Select(c => new { c.OrgInfoJson, c.OrgInfoUpdatedAtUtc })
                .FirstOrDefaultAsync();

            var normalizedOrgInfo = JsonSerializer.Serialize(FromJsonOrDefault(profile?.OrgInfoJson));

            // Child workbooks
            var wb1 = new WorkbookSubmission
            {
                Title = $"Organisation Information - {now:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook1,
                Data = normalizedOrgInfo,
                Status = SubmissionStatus.Completed,      // still editable
                UserId = CurrentUserId,
                CompanyId = cid.Value,
                SubmissionId = submission.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            var wb2 = new WorkbookSubmission
            {
                Title = $"Quality Assurance - {now:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook2,
                Status = SubmissionStatus.Draft,
                Data = "{}",
                UserId = CurrentUserId,
                CompanyId = cid.Value,
                SubmissionId = submission.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            var wb3 = new WorkbookSubmission
            {
                Title = $"Training QA - {now:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook3,
                Status = SubmissionStatus.Draft,
                Data = "{}",
                UserId = CurrentUserId,
                CompanyId = cid.Value,
                SubmissionId = submission.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.WorkbookSubmissions.AddRange(wb1, wb2, wb3);
            await _db.SaveChangesAsync();

            TempData["ok"] = profile?.OrgInfoUpdatedAtUtc is DateTime t
                ? $"New submission started. Workbook 1 prefilled from Org Profile last updated {t.ToLocalTime():yyyy/MM/dd HH:mm}."
                : "New submission started. No Organisation Profile found; Workbook 1 started from a blank template.";

            return RedirectToAction(nameof(Details), new { id = submission.Id });
        }

        // GET: /Submissions/Details/{id}
        public async Task<IActionResult> Details(Guid id)
        {
            var s = await _db.Submissions
                .Include(x => x.Company)
                .Include(x => x.OwnerUser)
                .Include(x => x.Workbooks)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();

            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (s.CompanyId != cid) return Forbid();
            }

            if (!IsBundleTerminal(s.Status))
            {
                var count = s.Workbooks?.Count ?? 0;
                var anyProgress = s.Workbooks?.Any(w => w.Status != SubmissionStatus.Draft) == true;
                var allCompleted = (count == 3) && s.Workbooks!.All(IsWorkbookComplete);

                s.Status = allCompleted
                    ? SubmissionBundleStatus.Completed
                    : anyProgress
                        ? SubmissionBundleStatus.InProgress
                        : SubmissionBundleStatus.Draft;

                s.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return View(s);
        }

        // POST: /Submissions/Submit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(Guid id)
        {
            var s = await _db.Submissions
                .Include(x => x.Workbooks)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();

            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (s.CompanyId != cid) return Forbid();
            }

            if (IsBundleTerminal(s.Status) && s.Status != SubmissionBundleStatus.Rejected)
            {
                TempData["err"] = "This submission has already been finalized.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var allCompleted = s.Workbooks.Count == 3
                               && s.Workbooks.All(IsWorkbookComplete);

            if (!allCompleted)
            {
                TempData["err"] = "All three workbooks must be completed before submitting.";
                return RedirectToAction(nameof(Details), new { id });
            }

            s.Status = SubmissionBundleStatus.Submitted;
            s.DecisionNote = null;
            s.DecidedByUserId = null;
            s.DecidedAtUtc = null;

            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["ok"] = "Submission sent.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: /Submissions/Resubmit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resubmit(Guid id)
        {
            var s = await _db.Submissions
                .Include(x => x.Workbooks)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();

            var cid = await CurrentUserCompanyIdAsync();
            if (cid == null || s.CompanyId != cid.Value) return Forbid();

            if (s.Status != SubmissionBundleStatus.Rejected)
            {
                TempData["err"] = "Only rejected submissions can be resubmitted.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var allCompleted = s.Workbooks.Count == 3
                               && s.Workbooks.All(IsWorkbookComplete);

            if (!allCompleted)
            {
                TempData["err"] = "All three workbooks must be completed before resubmitting.";
                return RedirectToAction(nameof(Details), new { id });
            }

            s.Status = SubmissionBundleStatus.Submitted;
            s.DecisionNote = null;
            s.DecidedByUserId = null;
            s.DecidedAtUtc = null;
            s.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["ok"] = "Submission resubmitted for review.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: /Submissions/Delete/{id} (allowed until Submitted)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var s = await _db.Submissions
                .Include(x => x.Workbooks)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();

            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (s.CompanyId != cid) return Forbid();
            }

            // Block deletion once it's been submitted (or beyond)
            if (IsBundleTerminal(s.Status)) // Submitted / Approved / Rejected
            {
                TempData["err"] = "This submission has already been submitted and can no longer be deleted.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _db.Submissions.Remove(s); // cascades to its workbooks
            await _db.SaveChangesAsync();

            TempData["ok"] = "Submission deleted.";
            return RedirectToAction(nameof(Index));
        }

    }
}
