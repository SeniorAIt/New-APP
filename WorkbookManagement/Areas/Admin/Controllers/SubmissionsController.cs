using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "SuperAdmin")]
    public class SubmissionsController : Controller
    {
        private readonly ApplicationDbContext _db;
        public SubmissionsController(ApplicationDbContext db) => _db = db;

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: /Admin/Submissions
        // Supports filtering by free-text (company or owner email) and bundle Status
        [HttpGet]
        public async Task<IActionResult> Index(string? q, SubmissionBundleStatus? status)
        {
            var query = _db.Submissions
                .Include(s => s.Company)
                .Include(s => s.OwnerUser)
                .Include(s => s.Workbooks)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(s =>
                    (s.Company != null && EF.Functions.Like(s.Company.Name.ToLower(), $"%{term}%")) ||
                    (s.OwnerUser != null && EF.Functions.Like(s.OwnerUser.Email!.ToLower(), $"%{term}%")));
            }

            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            var list = await query
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            // feed the view so the form keeps the current filters
            ViewBag.FilterQ = q ?? "";
            ViewBag.FilterStatus = status;

            return View(list);
        }

        // GET: /Admin/Submissions/Details/{id}
        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            var sub = await _db.Submissions
                .Include(s => s.Company)
                .Include(s => s.OwnerUser)
                .Include(s => s.DecidedByUser)
                .Include(s => s.Workbooks)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null) return NotFound();
            return View(sub);
        }

        // POST: /Admin/Submissions/Decide/{id}?decision=approve|reject
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Decide(Guid id, string decision, string? reason)
        {
            var sub = await _db.Submissions
                .Include(s => s.Workbooks)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null) return NotFound();

            // Only decide once bundle has been submitted
            if (sub.Status != SubmissionBundleStatus.Submitted)
            {
                TempData["err"] = "Only bundles with status 'Submitted' can be approved or rejected.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Prevent double decisions
            if (sub.IsTerminal)
            {
                TempData["err"] = "This submission has already been decided.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var d = (decision ?? string.Empty).Trim().ToLowerInvariant();
            if (d == "approve")
            {
                sub.Status = SubmissionBundleStatus.Approved;
            }
            else if (d == "reject")
            {
                sub.Status = SubmissionBundleStatus.Rejected;
            }
            else
            {
                TempData["err"] = "Unknown decision. Choose approve or reject.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Record audit/meta
            sub.DecisionNote = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim();
            sub.DecidedByUserId = CurrentUserId;
            sub.DecidedAtUtc = DateTime.UtcNow;
            sub.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["ok"] = sub.Status == SubmissionBundleStatus.Approved
                ? "Submission approved."
                : "Submission rejected.";

            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
