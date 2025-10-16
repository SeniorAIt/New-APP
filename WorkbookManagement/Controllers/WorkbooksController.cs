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
    public class WorkbooksController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        public WorkbooksController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        // GET: /Workbooks
        // GET: /Workbooks
        public async Task<IActionResult> Index(
            string? q,                  // free text: title, user email, company name
            Guid? companyId,            // filter by company
            WorkbookType? type,         // filter by workbook type
            SubmissionStatus? status)   // filter by status
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            IQueryable<WorkbookSubmission> query = _db.WorkbookSubmissions
                .Include(w => w.User)
                .Include(w => w.Company);

            if (!isSuper)
            {
                if (me.CompanyId is null) return Forbid();
                query = query.Where(w => w.CompanyId == me.CompanyId);
            }
            else
            {
                // company dropdown source
                ViewBag.Companies = await _db.Companies
                    .OrderBy(c => c.Name)
                    .ToListAsync();
            }

            // --- filters ---
            if (companyId.HasValue && companyId.Value != Guid.Empty)
                query = query.Where(w => w.CompanyId == companyId.Value);

            if (type.HasValue)
                query = query.Where(w => w.WorkbookType == type.Value);

            if (status.HasValue)
                query = query.Where(w => w.Status == status.Value);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(w =>
                    EF.Functions.Like(w.Title.ToLower(), $"%{term}%") ||
                    (w.User != null && EF.Functions.Like(w.User.Email!.ToLower(), $"%{term}%")) ||
                    (w.Company != null && EF.Functions.Like(w.Company.Name.ToLower(), $"%{term}%"))
                );
            }

            var list = await query
                .OrderByDescending(w => w.UpdatedAt)
                .ToListAsync();

            // keep selected values to re-fill the form
            ViewBag.FilterQ = q ?? "";
            ViewBag.FilterCompanyId = companyId;
            ViewBag.FilterType = type;
            ViewBag.FilterStatus = status;

            return View(list);
        }


        // ========= VIEW-ONLY READ FOR ALL WORKBOOKS =========
        // GET: /Workbooks/Show/5
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();
            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            var wb = await _db.WorkbookSubmissions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (wb is null) return NotFound();
            if (!isSuper && me.CompanyId != wb.CompanyId) return Forbid();

            ViewBag.Submission = wb;

            switch (wb.WorkbookType)
            {
                case WorkbookType.Workbook3: // Training QA (TQA) nice read-only
                    {
                        var data = SafeDeserializeTqa(wb.Data);

                        // Compliance overview (same calc used on Step 2)
                        var sr = ComputeOverviewStats(data.SiteReadiness);
                        var fr = ComputeOverviewStats(data.Facilitator);
                        var lp = ComputeOverviewStats(data.Learner);
                        var asu = ComputeOverviewStats(data.AdminSupport);
                        var rc = ComputeOverviewStats(data.Risk);

                        int overallCriteria = sr.Criteria + fr.Criteria + lp.Criteria + asu.Criteria + rc.Criteria;
                        int overallComp = sr.Comp + fr.Comp + lp.Comp + asu.Comp + rc.Comp;
                        int overallNonComp = sr.NonComp + fr.NonComp + lp.NonComp + asu.NonComp + rc.NonComp;
                        int overallApplicable = overallComp + overallNonComp;
                        double overallPct = overallApplicable == 0 ? 0 : (double)overallComp / overallApplicable;

                        ViewBag.CO_Overall = new { Criteria = overallCriteria, Comp = overallComp, NonComp = overallNonComp, Pct = overallPct };
                        ViewBag.CO_Rows = new[]
                        {
                            new { Section = "SITE READINESS (P2):",              Criteria = sr.Criteria,  Comp = sr.Comp,  NonComp = sr.NonComp,  Pct = sr.Pct  },
                            new { Section = "FACILITATOR READINESS (P3):",       Criteria = fr.Criteria,  Comp = fr.Comp,  NonComp = fr.NonComp,  Pct = fr.Pct  },
                            new { Section = "LEARNER PREPAREDNESS (P4):",        Criteria = lp.Criteria,  Comp = lp.Comp,  NonComp = lp.NonComp,  Pct = lp.Pct  },
                            new { Section = "ADMIN & SUPPORT SYSTEMS (P5):",     Criteria = asu.Criteria, Comp = asu.Comp, NonComp = asu.NonComp, Pct = asu.Pct },
                            new { Section = "RISK & CONTINGENCY PLANNING (P6):", Criteria = rc.Criteria,  Comp = rc.Comp,  NonComp = rc.NonComp,  Pct = rc.Pct  },
                        };

                        // Use smart "Open" route so the button goes to the wizard
                        ViewBag.EditUrl = Url.Action("Open", new { id = wb.Id });

                        return View("ShowTqa", data); // /Views/Workbooks/ShowTqa.cshtml
                    }

                case WorkbookType.Workbook2: // QA (WB2) – generic read-only for now
                case WorkbookType.Workbook1: // Org Info – generic read-only for now
                default:
                    {
                        ViewBag.PrettyJson = PrettyJson(wb.Data);
                        // Use smart "Open" route (will fall back to Workbooks/Edit)
                        ViewBag.EditUrl = Url.Action("Open", new { id = wb.Id });
                        return View("ShowGeneric", wb); // /Views/Workbooks/ShowGeneric.cshtml
                    }
            }
        }

        // ========= SMART OPEN (redirects to the right editor/wizard) =========
        // GET: /Workbooks/Open/5
        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();
            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            var wb = await _db.WorkbookSubmissions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (wb is null) return NotFound();
            if (!isSuper && me.CompanyId != wb.CompanyId) return Forbid();

            // Route to the *wizard entry point* for each workbook type
            return wb.WorkbookType switch
            {
                WorkbookType.Workbook1 => RedirectToAction("Step1", "OrgInfo", new { id = wb.Id }),
                WorkbookType.Workbook2 => RedirectToAction("Step1", "QualityAssurance", new { id = wb.Id }),
                WorkbookType.Workbook3 => RedirectToAction("Step1", "TrainingQualityAssurance", new { id = wb.Id }),
                _ => RedirectToAction(nameof(Edit), new { id = wb.Id })  // fallback
            };
        }

        // ---------- helpers for Show ----------
        private static (int Criteria, int Comp, int NonComp, double Pct) ComputeOverviewStats(TqaGridSection? sec)
        {
            if (sec == null) return (0, 0, 0, 0);
            int comp = sec.Compliant, non = sec.NotCompliant;
            int applicable = comp + non;
            double pct = applicable == 0 ? 0 : (double)comp / applicable;
            return (sec.Rows?.Count ?? 0, comp, non, pct);
        }

        private static TqaData SafeDeserializeTqa(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return TqaData.CreateDefault();
            try { return JsonSerializer.Deserialize<TqaData>(json) ?? TqaData.CreateDefault(); }
            catch { return TqaData.CreateDefault(); }
        }

        private static string PrettyJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "{}";
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { return json ?? "{}"; }
        }

        // GET: /Workbooks/Create
        public async Task<IActionResult> Create()
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");
            if (isSuper)
            {
                // SuperAdmin can create on behalf of any company
                ViewBag.Companies = await _db.Companies
                    .OrderBy(c => c.Name)
                    .ToListAsync();
            }
            else
            {
                if (me.CompanyId is null) return Forbid();
            }

            return View(new WorkbookSubmission());
        }

        // POST: /Workbooks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Title,WorkbookType,Data,Status,CompanyId")] WorkbookSubmission input)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            if (!isSuper)
            {
                // Company-scoped users must create only for their own company
                if (me.CompanyId is null) return Forbid();
                input.CompanyId = me.CompanyId.Value;
            }
            else
            {
                // SuperAdmin must select a valid company
                if (input.CompanyId == Guid.Empty || !await _db.Companies.AnyAsync(c => c.Id == input.CompanyId))
                {
                    ModelState.AddModelError("CompanyId", "Please select a valid company.");
                }
            }

            if (!ModelState.IsValid)
            {
                if (isSuper)
                {
                    ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                }
                return View(input);
            }

            input.UserId = me.Id; // creator
            input.CreatedAt = DateTime.UtcNow;
            input.UpdatedAt = DateTime.UtcNow;

            _db.Add(input);
            await _db.SaveChangesAsync();
            TempData["ok"] = "Workbook created.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Workbooks/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            var wb = await _db.WorkbookSubmissions
                .Include(w => w.Company)
                .FirstOrDefaultAsync(w => w.Id == id);
            if (wb == null) return NotFound();

            if (!isSuper)
            {
                if (me.CompanyId is null || wb.CompanyId != me.CompanyId) return Forbid();
            }

            return View(wb);
        }

        // POST: /Workbooks/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Title,WorkbookType,Data,Status,CompanyId")] WorkbookSubmission input,
            Workbook1Vm w1) // bind Workbook 1 fields via "w1.*" names in the view
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            if (!isSuper)
            {
                if (me.CompanyId is null) return Forbid();
                input.CompanyId = me.CompanyId.Value;
            }
            else
            {
                if (input.CompanyId == Guid.Empty || !await _db.Companies.AnyAsync(c => c.Id == input.CompanyId))
                    ModelState.AddModelError("CompanyId", "Please select a valid company.");
            }

            // If this is Workbook 1, validate & serialize its typed fields into JSON
            if (input.WorkbookType == WorkbookType.Workbook1)
            {
                // Validate with the correct prefix ("w1") used by the view's input names
                if (!TryValidateModel(w1, prefix: "w1"))
                {
                    if (isSuper) ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                    return View(input);
                }

                // Serialize into the expected JSON structure
                input.Data = System.Text.Json.JsonSerializer.Serialize(new
                {
                    overview = new
                    {
                        institution_name = w1.InstitutionName,
                        institution_type = w1.InstitutionType,
                        registration_number = w1.RegistrationNumber,
                        contact_details = new
                        {
                            address = w1.ContactAddress,
                            phone = w1.ContactPhone,
                            email = w1.ContactEmail
                        }
                    }
                });
            }
            // For Workbook2/3 we’ll keep using the "Data (JSON)" textbox for now.

            if (!ModelState.IsValid)
            {
                if (isSuper) ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                return View(input);
            }

            input.UserId = me.Id;
            input.CreatedAt = DateTime.UtcNow;
            input.UpdatedAt = DateTime.UtcNow;

            _db.Add(input);
            await _db.SaveChangesAsync();
            TempData["ok"] = "Workbook created.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Workbooks/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return Challenge();

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            var wb = await _db.WorkbookSubmissions.FindAsync(id);
            if (wb == null) return NotFound();

            if (!isSuper)
            {
                if (me.CompanyId is null || wb.CompanyId != me.CompanyId) return Forbid();
            }

            _db.WorkbookSubmissions.Remove(wb);
            await _db.SaveChangesAsync();
            TempData["ok"] = "Workbook deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
