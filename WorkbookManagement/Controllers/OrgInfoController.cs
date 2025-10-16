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
    public class OrgInfoController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        private static readonly string[] SouthAfricaProvinces = new[]
        {
            "Eastern Cape","Free State","Gauteng","KwaZulu-Natal","Limpopo",
            "Mpumalanga","Northern Cape","North West","Western Cape"
        };

        public OrgInfoController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        // ------- helpers -------
        private static OrgInfoData ParseData(WorkbookSubmission wb)
        {
            if (string.IsNullOrWhiteSpace(wb.Data)) return OrgInfoData.CreateDefault();
            try { return JsonSerializer.Deserialize<OrgInfoData>(wb.Data) ?? OrgInfoData.CreateDefault(); }
            catch { return OrgInfoData.CreateDefault(); }
        }

        private static void SaveData(WorkbookSubmission wb, OrgInfoData data)
        {
            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
        }

        private static bool IsProfileEdit(WorkbookSubmission wb) => wb.SubmissionId == null;
        private static bool WantsProfileSave(string? nav) =>
            string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Persist the current wizard JSON to the company profile and remove the temporary profile draft.
        /// </summary>
        private async Task<IActionResult> SaveProfileAndRedirectAsync(WorkbookSubmission wb)
        {
            // This action is only for the Organisation Info "profile draft" (no SubmissionId).
            if (wb.SubmissionId != null)
                return BadRequest("Saving the Organisation Profile is only allowed for a profile draft.");

            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == wb.CompanyId);
            if (company is null) return NotFound("Company not found.");

            company.OrgInfoJson = wb.Data;
            company.OrgInfoUpdatedAtUtc = DateTime.UtcNow;

            // Remove the temporary draft so it doesn't clutter the Workbooks list
            _db.WorkbookSubmissions.Remove(wb);

            await _db.SaveChangesAsync();

            TempData["ok"] = "Organisation Info has been saved to your company profile.";
            return RedirectToAction("Index", "Workbooks");
        }

        private async Task<WorkbookSubmission?> LoadScopedAsync(int id, bool track = false)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return null;

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            IQueryable<WorkbookSubmission> q = track ? _db.WorkbookSubmissions : _db.WorkbookSubmissions.AsNoTracking();
            var wb = await q.FirstOrDefaultAsync(x => x.Id == id && x.WorkbookType == WorkbookType.Workbook1);
            if (wb is null) return null;

            if (!isSuper)
            {
                if (me.CompanyId is null || wb.CompanyId != me.CompanyId) return null;
            }
            return wb;
        }

        private static List<ApprovalRow> GetDefaultApprovals() => new()
        {
            new ApprovalRow { Name = "Quality Council for Trades & Occupations (QCTO)" },
            new ApprovalRow { Name = "Umalusi Standards & Guidelines for Quality" },
            new ApprovalRow { Name = "Council on Higher Education Quality Assurance Framework" },
            new ApprovalRow { Name = "King IV Report Principles Corporate Governance" },
            new ApprovalRow { Name = "Independent Code of Governance for Non-Profit Organisations" },
            new ApprovalRow { Name = "African Standards & Guidelines for Quality Assurance" },
            new ApprovalRow { Name = "European Standards & Guidelines for Quality Assurance" },
            new ApprovalRow { Name = "ISO 21001:2018 - Education Organisation Management Systems (EOMS)" },
            new ApprovalRow { Name = "Investors in People" },
            new ApprovalRow { Name = "Other (specify)", IsOther = true }
        };

        // ------- START -------
        /// <summary>
        /// Starts an Organisation Info "profile draft" (SubmissionId = null), prefilled from the company profile if available.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Start(Guid? companyId)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            Guid resolvedCompanyId;
            if (isSuper)
            {
                if (companyId == null || companyId == Guid.Empty)
                {
                    ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                    return View("StartPickCompany");
                }

                var exists = await _db.Companies.AnyAsync(c => c.Id == companyId.Value);
                if (!exists) return BadRequest("Invalid company.");
                resolvedCompanyId = companyId.Value;
            }
            else
            {
                if (me.CompanyId == null) return Forbid();
                resolvedCompanyId = me.CompanyId.Value;
            }

            // Prefill from the company profile if present, else a blank default
            var comp = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == resolvedCompanyId);
            var initialJson = comp?.OrgInfoJson ?? JsonSerializer.Serialize(OrgInfoData.CreateDefault(), JsonOpts);

            var draft = new WorkbookSubmission
            {
                Title = $"Organisation Information - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook1,
                Status = SubmissionStatus.Draft,
                CompanyId = resolvedCompanyId,
                UserId = me.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Data = initialJson,
                SubmissionId = null // IMPORTANT: profile draft, NOT part of a submission
            };

            _db.Add(draft);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Step1), new { id = draft.Id });
        }

        // ------- STEP 1: Guide -------
        [HttpGet]
        public async Task<IActionResult> Step1(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step1(int id, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step2), new { id });
        }

        // ------- STEP 2: Overview (read-only) -------
        [HttpGet]
        public async Task<IActionResult> Step2(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(ParseData(wb));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2(int id, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step1), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step3), new { id });
        }

        // ------- STEP 3: Part 1 — Administrative / Head Office -------
        [HttpGet]
        public async Task<IActionResult> Step3(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);

            if (data.Section1.Approvals == null || data.Section1.Approvals.Count == 0)
            {
                data.Section1.Approvals = GetDefaultApprovals();
                SaveData(wb, data);
                await _db.SaveChangesAsync();
            }

            ViewBag.Provinces = SouthAfricaProvinces;
            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.Section1);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step3(int id, OrgInfoSection1 model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Provinces = SouthAfricaProvinces;
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.Section1 = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step2), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step4), new { id });
        }

        // ------- STEP 4: Part 2 — Board of Directors -------
        [HttpGet]
        public async Task<IActionResult> Step4(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.Board ??= new OrgInfoBoardSection();
            if (data.Board.Directors.Count == 0)
                data.Board.Directors.Add(new DirectorRow());

            SaveData(wb, data);
            await _db.SaveChangesAsync();

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.Board);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step4(int id, OrgInfoBoardSection model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.Board = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step3), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step5), new { id });
        }

        // ------- STEP 5: Part 3 — Employment Stats -------
        [HttpGet]
        public async Task<IActionResult> Step5(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.Employment ??= new OrgInfoEmploymentSection();
            if (data.Employment.Positions.Count == 0)
            {
                data.Employment.Positions.Add(new EmploymentPosition());
                SaveData(wb, data);
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.Employment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step5(int id, OrgInfoEmploymentSection model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.Employment = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step4), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step6), new { id });
        }

        // ------- STEP 6: Part 4 — Campuses / Sites -------
        [HttpGet]
        public async Task<IActionResult> Step6(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.Campuses ??= new OrgInfoCampusesSection();
            if (data.Campuses.Sites.Count == 0)
            {
                data.Campuses.Sites.Add(new CampusSiteRow());
                SaveData(wb, data);
                await _db.SaveChangesAsync();
            }

            ViewBag.Provinces = SouthAfricaProvinces;
            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.Campuses);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step6(int id, OrgInfoCampusesSection model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Provinces = SouthAfricaProvinces;
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.Campuses = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step5), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step7), new { id });
        }

        // ------- STEP 7: Part 5 — Qualifications / Programmes / Courses -------
        [HttpGet]
        public async Task<IActionResult> Step7(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.Qualifications ??= new OrgInfoQualificationsSection();
            if (data.Qualifications.Items.Count == 0)
            {
                data.Qualifications.Items.Add(new QualificationCourseRow());
                SaveData(wb, data);
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.Qualifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step7(int id, OrgInfoQualificationsSection model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.Qualifications = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step6), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "save", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Workbooks");

            return RedirectToAction(nameof(Step8), new { id });
        }

        // ------- STEP 8: Part 6 — Pricing -------
        [HttpGet]
        public async Task<IActionResult> Step8(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.Pricing ??= new OrgInfoPricingSection();

            var source = data.Qualifications?.Items ?? new List<QualificationCourseRow>();
            var byCode = data.Pricing.Items.ToDictionary(x => x.Code ?? "", StringComparer.OrdinalIgnoreCase);

            foreach (var q in source)
            {
                var code = q.Code ?? "";
                if (!byCode.TryGetValue(code, out var row))
                {
                    row = new PricingRow { Code = q.Code };
                    data.Pricing.Items.Add(row);
                }
                row.QualificationName = q.Name;
                row.Type = q.Type;
                row.NQFLevel = q.NQFLevel;
                row.Credits = q.Credits;
            }

            SaveData(wb, data);
            await _db.SaveChangesAsync();

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.Pricing);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step8(int id, OrgInfoPricingSection model, string? nav = "save")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);

            // Re-sync identity columns from Step 7 before saving
            var source = data.Qualifications?.Items ?? new List<QualificationCourseRow>();
            var srcByCode = source.ToDictionary(s => s.Code ?? "", StringComparer.OrdinalIgnoreCase);

            foreach (var row in model.Items)
            {
                var k = row.Code ?? "";
                if (srcByCode.TryGetValue(k, out var q))
                {
                    row.QualificationName = q.Name;
                    row.Type = q.Type;
                    row.NQFLevel = q.NQFLevel;
                    row.Credits = q.Credits;
                }
            }

            data.Pricing = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step7), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "next", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step9), new { id });

            return RedirectToAction(nameof(Step8), new { id });
        }

        // ------- STEP 9: Part 7 — Student Stats (Historical) -------
        [HttpGet]
        public async Task<IActionResult> Step9(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.StudentHistorical ??= new OrgInfoStudentHistoricalSection();

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.StudentHistorical);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step9(int id, OrgInfoStudentHistoricalSection model, string? nav = "save")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.StudentHistorical = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step8), new { id });

            if (string.Equals(nav, "saveprofile", StringComparison.OrdinalIgnoreCase))
                return await SaveProfileAndRedirectAsync(wb);

            if (string.Equals(nav, "next", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step10), new { id });

            return RedirectToAction(nameof(Step9), new { id });
        }

        // ------- STEP 10: Part 8 — Student Stats (Current / FINAL) -------
        [HttpGet]
        public async Task<IActionResult> Step10(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.StudentCurrent ??= new OrgInfoStudentCurrentSection();

            ViewBag.Id = id;
            ViewBag.CanSaveProfile = IsProfileEdit(wb);
            return View(data.StudentCurrent);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step10(int id, OrgInfoStudentCurrentSection model, string? nav = "save")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Id = id;
                ViewBag.CanSaveProfile = IsProfileEdit(wb);
                return View(model);
            }

            var data = ParseData(wb);
            data.StudentCurrent = model;
            SaveData(wb, data);

            var n = (nav ?? "save").ToLowerInvariant();

            if (n == "prev")
            {
                await _db.SaveChangesAsync();
                return RedirectToAction(nameof(Step9), new { id });
            }

            if (n == "saveprofile")
            {
                await _db.SaveChangesAsync();
                return await SaveProfileAndRedirectAsync(wb);
            }

            if (n == "next")
            {
                // Finalize ONLY the wizard (rarely used for profile mode).
                wb.Status = SubmissionStatus.Completed;
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return RedirectToAction("Index", "Workbooks");
            }

            // Save (stay on page)
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Step10), new { id });
        }
    }
}
