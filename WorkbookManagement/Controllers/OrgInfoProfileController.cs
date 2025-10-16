using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Controllers
{
    [Authorize]
    public class OrgInfoProfileController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        public OrgInfoProfileController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        /// <summary>
        /// Starts an Org Info "profile edit" session by creating a temporary
        /// Workbook1 (no SubmissionId) prefilled from Company.OrgInfoJson.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> StartWizard()
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();
            if (me.CompanyId is null) return Forbid();

            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == me.CompanyId.Value);

            var initialJson = company?.OrgInfoJson
                ?? JsonSerializer.Serialize(OrgInfoData.CreateDefault(), JsonOpts);

            var draft = new WorkbookSubmission
            {
                Title = $"Organisation Information - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook1,
                Status = SubmissionStatus.Draft,
                CompanyId = me.CompanyId.Value,
                UserId = me.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Data = initialJson,
                SubmissionId = null // explicit: standalone profile edit
            };

            _db.WorkbookSubmissions.Add(draft);
            await _db.SaveChangesAsync();

            // Jump straight to the first editable section
            return RedirectToAction("Step3", "OrgInfo", new { id = draft.Id });
        }
    }
}
