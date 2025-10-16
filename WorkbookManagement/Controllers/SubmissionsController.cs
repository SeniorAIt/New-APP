using System.Security.Claims;
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
                if (cid.HasValue)
                {
                    // company-scoped users see their company’s submissions
                    q = q.Where(s => s.CompanyId == cid.Value);
                }
                else
                {
                    // fallback: owner only
                    q = q.Where(s => s.OwnerUserId == CurrentUserId);
                }
            }

            var list = await q.AsNoTracking().ToListAsync();
            return View(list);
        }

        // POST: /Submissions/Start
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

            // Prevent multiple concurrent “active” bundles for this owner/company.
            // IMPORTANT: use only EF-translatable comparisons here.
            var existingActive = await _db.Submissions
                .Where(s => s.CompanyId == cid.Value && s.OwnerUserId == CurrentUserId)
                .Where(s =>
                       s.Status == SubmissionBundleStatus.Draft
                    || s.Status == SubmissionBundleStatus.InProgress
                    || s.Status == SubmissionBundleStatus.Completed)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (existingActive != null)
            {
                TempData["ok"] = "You already have an active submission. Continue working on it below.";
                return RedirectToAction(nameof(Details), new { id = existingActive.Id });
            }

            var now = DateTime.UtcNow;

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

            // Create the 3 child workbooks for this submission
            var wbRows = new[]
            {
                new WorkbookSubmission
                {
                    Title = $"New Org Info - {now:yyyy-MM-dd HH:mm}",
                    WorkbookType = WorkbookType.Workbook1,
                    Status = SubmissionStatus.Draft,
                    UserId = CurrentUserId,
                    CompanyId = cid.Value,
                    SubmissionId = submission.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new WorkbookSubmission
                {
                    Title = $"New QA Workbook - {now:yyyy-MM-dd HH:mm}",
                    WorkbookType = WorkbookType.Workbook2,
                    Status = SubmissionStatus.Draft,
                    UserId = CurrentUserId,
                    CompanyId = cid.Value,
                    SubmissionId = submission.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new WorkbookSubmission
                {
                    Title = $"New Training QA Workbook - {now:yyyy-MM-dd HH:mm}",
                    WorkbookType = WorkbookType.Workbook3,
                    Status = SubmissionStatus.Draft,
                    UserId = CurrentUserId,
                    CompanyId = cid.Value,
                    SubmissionId = submission.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            };

            _db.WorkbookSubmissions.AddRange(wbRows);
            await _db.SaveChangesAsync();

            TempData["ok"] = "New submission started. Complete all three workbooks, then submit the bundle.";
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

            // Derive bundle status from children if not already a terminal state
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

            if (IsBundleTerminal(s.Status))
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
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["ok"] = "Submission sent.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: /Submissions/Delete/{id} (only when Draft)
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

            if (s.Status != SubmissionBundleStatus.Draft)
            {
                TempData["err"] = "Only draft submissions can be deleted.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _db.Submissions.Remove(s); // cascades to its workbooks
            await _db.SaveChangesAsync();

            TempData["ok"] = "Submission deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
