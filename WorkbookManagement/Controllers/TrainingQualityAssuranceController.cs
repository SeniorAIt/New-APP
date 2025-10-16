using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WorkbookManagement.Data;
using WorkbookManagement.Models;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkbookManagement.Controllers
{
    // Route aliases so both /TrainingQA/... and /TrainQA/... work.
    [Authorize]
    [Route("TrainingQA/[action]")]
    [Route("TrainQA/[action]")]
    public class TrainingQualityAssuranceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        public TrainingQualityAssuranceController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        // =======================================================
        // START (same pattern as the other workbooks)
        // =======================================================
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
                    // View path: /Views/TrainingQualityAssurance/StartPickCompany.cshtml
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

            var draft = new WorkbookSubmission
            {
                Title = $"Training QA - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook3,
                Status = SubmissionStatus.Draft,
                CompanyId = resolvedCompanyId,
                UserId = me.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Data = JsonSerializer.Serialize(TqaData.CreateDefault(), JsonOpts)
            };

            _db.Add(draft);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Step1), new { id = draft.Id });
        }

        // =======================================================
        // STEP 1 — GUIDE
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step1(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            ViewBag.Id = id;
            return View(data.Guide); // /Views/TrainingQualityAssurance/Step1.cshtml
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step1(int id, TqaGuide model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.Guide = model ?? new TqaGuide();

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "save" => RedirectToAction("Index", "Workbooks"),
                "prev" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step2), new { id })
            };
        }

        [HttpGet]
        public async Task<IActionResult> Step2(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.General ??= new TqaGeneral();

            // --- Compliance Overview (Part 1 sidebar/table) ---
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

            ViewBag.Id = id;
            return View(data.General);   // strongly typed to TqaGeneral
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2(int id, TqaGeneral model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            data.General = model ?? new TqaGeneral();

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            switch ((nav ?? "next").ToLowerInvariant())
            {
                case "prev": return RedirectToAction(nameof(Step1), new { id });
                case "save": return RedirectToAction("Index", "Workbooks");
                default: return RedirectToAction(nameof(Step3), new { id });
            }
        }

        // ---------------- Helper for Compliance Overview ----------------
        // Computes Criteria, Compliant, NotCompliant and % (Compliant / (Compliant + NotCompliant))
        private static (int Criteria, int Comp, int NonComp, double Pct) ComputeOverviewStats(TqaGridSection? sec)
        {
            if (sec == null) return (0, 0, 0, 0);
            int criteria = sec.Rows?.Count ?? 0;
            int comp = sec.Compliant;
            int nonComp = sec.NotCompliant;
            int applicable = comp + nonComp;
            double pct = applicable == 0 ? 0 : (double)comp / applicable;
            return (criteria, comp, nonComp, pct);
        }

        // =======================================================
        // STEP 3 — PART 2: SITE READINESS (grid)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step3(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            bool seeded = EnsureSiteSeed(data.SiteReadiness);
            if (seeded)
            {
                wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            ViewBag.SubmissionId = wb.Id;
            return View("Step3", data); // /Views/TrainingQualityAssurance/Step3.cshtml (typed to TqaData)
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step3(int id, [Bind(Prefix = "SiteReadiness")] TqaSiteReadiness site, string? nav = "save")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            EnsureSiteSeed(data.SiteReadiness);

            // Replace only the SiteReadiness subtree from the posted form
            data.SiteReadiness = site ?? new TqaSiteReadiness();

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            switch ((nav ?? "save").ToLowerInvariant())
            {
                case "prev": return RedirectToAction(nameof(Step2), new { id = wb.Id });
                case "next": return RedirectToAction(nameof(Step4), new { id = wb.Id });
                default: return RedirectToAction(nameof(Step3), new { id = wb.Id });
            }
        }

        // ---- Seeding helper (idempotent) – Excel-accurate ------------------------
        private static bool EnsureSiteSeed(TqaSiteReadiness sec)
        {
            if (sec == null) return false;
            if (sec.Parts.Count > 0 || sec.Rows.Count > 0) return false; // already seeded

            sec.Parts.AddRange(new[]
            {
                new TqaPart { PartCode = "2.1", Title = "Accessibility" },
                new TqaPart { PartCode = "2.2", Title = "Health & Safety" },
                new TqaPart { PartCode = "2.3", Title = "Facilities" },
                new TqaPart { PartCode = "2.4", Title = "Cleanliness" },
                new TqaPart { PartCode = "2.5", Title = "Internet/Connectivity" },
                new TqaPart { PartCode = "2.6", Title = "Equipment" },
            });

            sec.Rows.AddRange(new[]
            {
                // 2.1 Accessibility (1)
                new TqaRow { PartCode="2.1", Code="2.1.1", Requirement="Venue accessible to all (including people with disabilities)" },

                // 2.2 Health & Safety (7)
                new TqaRow { PartCode="2.2", Code="2.2.1", Requirement="Emergency exits clearly marked & accessible" },
                new TqaRow { PartCode="2.2", Code="2.2.2", Requirement="Emergency evacuation plan available & visible" },
                new TqaRow { PartCode="2.2", Code="2.2.3", Requirement="List of emergency contacts & numbers is available" },
                new TqaRow { PartCode="2.2", Code="2.2.4", Requirement="Fire extinguishers available (valid service dates)" },
                new TqaRow { PartCode="2.2", Code="2.2.5", Requirement="First aid kit available (contents & register checked)" },
                new TqaRow { PartCode="2.2", Code="2.2.6", Requirement="Training area free from hazards & safe for all" },
                new TqaRow { PartCode="2.2", Code="2.2.7", Requirement="Where required, PPE is available & use enforced" },

                // 2.3 Facilities (6)
                new TqaRow { PartCode="2.3", Code="2.3.1", Requirement="Adequate seating/work areas (venue supports the training type)" },
                new TqaRow { PartCode="2.3", Code="2.3.2", Requirement="Adequate toilets (accessible, clean, water, toilet paper)" },
                new TqaRow { PartCode="2.3", Code="2.3.3", Requirement="Sufficient lighting & ventilation" },
                new TqaRow { PartCode="2.3", Code="2.3.4", Requirement="Reasonable noise levels (internal/external)" },
                new TqaRow { PartCode="2.3", Code="2.3.5", Requirement="Power points & electrical switches are working" },
                new TqaRow { PartCode="2.3", Code="2.3.6", Requirement="Power points not overloaded (no excessive extension cords)" },

                // 2.4 Cleanliness (4)
                new TqaRow { PartCode="2.4", Code="2.4.1", Requirement="Venue is clean & hygienic" },
                new TqaRow { PartCode="2.4", Code="2.4.2", Requirement="Refuse/waste bins available (clean & emptied regularly)" },
                new TqaRow { PartCode="2.4", Code="2.4.3", Requirement="No smoking allowed (designated smoking areas with ashtrays)" },
                new TqaRow { PartCode="2.4", Code="2.4.4", Requirement="Separate area(s) provided for breaks & refreshments" },

                // 2.5 Internet/Connectivity (1)
                new TqaRow { PartCode="2.5", Code="2.5.1", Requirement="Wi-Fi or offline e-content available (if applicable)" },

                // 2.6 Equipment (6)
                new TqaRow { PartCode="2.6", Code="2.6.1", Requirement="Equipment checklist (p2_equip) completed" },
                new TqaRow { PartCode="2.6", Code="2.6.2", Requirement="All equipment requirements & quantities are met" },
                new TqaRow { PartCode="2.6", Code="2.6.3", Requirement="All equipment is in good condition & fit for purpose" },
                new TqaRow { PartCode="2.6", Code="2.6.4", Requirement="All electrical/gas equipment tested & functioning correctly" },
                new TqaRow { PartCode="2.6", Code="2.6.5", Requirement="Equipment meets safety requirements & is free from hazards" },
                new TqaRow { PartCode="2.6", Code="2.6.6", Requirement="Presentation equipment available (projectors, laptops, whiteboards, etc.)" },
            });

            return true;
        }

        // =======================================================
        // STEP 4 — PART 2: EQUIPMENT REGISTER (grid)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step4(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            bool seeded = EnsureEquipmentSeed(data.Equipment);
            if (seeded)
            {
                wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            return View(data.Equipment); // /Views/TrainingQualityAssurance/Step4.cshtml
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step4(int id, TqaEquipment model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            EnsureEquipmentSeed(data.Equipment);          // keep shape
            data.Equipment = model ?? new TqaEquipment(); // replace subtree

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step3), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step5), new { id })
            };
        }

        // ---- Seeding helper (idempotent): 10 blank rows --------------------------------
        private static bool EnsureEquipmentSeed(TqaEquipment sec)
        {
            if (sec == null) return false;
            if (sec.Rows.Count > 0) return false;

            // Pre-create 10 empty rows so the form renders immediately
            for (int i = 0; i < 10; i++)
                sec.Rows.Add(new TqaRow());

            return true;
        }

        // =======================================================
        // STEP 5 — PART 3: FACILITATOR READINESS (grid)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step5(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            bool seeded = EnsureFacilitatorSeed(data.Facilitator);
            if (seeded)
            {
                wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            return View(data.Facilitator); // /Views/TrainingQualityAssurance/Step5.cshtml
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step5(int id, TqaFacilitator model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            EnsureFacilitatorSeed(data.Facilitator); // keep shape
            data.Facilitator = model ?? new TqaFacilitator();

            wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step4), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step6), new { id })
            };
        }

        // ---- Seeding helper (idempotent) – Excel-accurate (19 criteria) ----------
        private static bool EnsureFacilitatorSeed(TqaFacilitator sec)
        {
            if (sec == null) return false;
            if (sec.Parts.Count > 0 || sec.Rows.Count > 0) return false;

            sec.Parts.AddRange(new[]
            {
        new TqaPart { PartCode = "3.1", Title = "Qualifications" },
        new TqaPart { PartCode = "3.2", Title = "Teaching Skills" },
        new TqaPart { PartCode = "3.3", Title = "Course Material & Structure" },
        new TqaPart { PartCode = "3.4", Title = "Venue & Equipment" },
        new TqaPart { PartCode = "3.5", Title = "Learners" },
        new TqaPart { PartCode = "3.6", Title = "Policies & Procedures" },
    });

            sec.Rows.AddRange(new[]
            {
        // 3.1 Qualifications (2)
        new TqaRow { PartCode="3.1", Code="3.1.1", Requirement="Relevant subject matter qualifications and/or proven expertise." },
        new TqaRow { PartCode="3.1", Code="3.1.2", Requirement="Registered Assessor/Moderator (if applicable/required)." },

        // 3.2 Teaching Skills (1)
        new TqaRow { PartCode="3.2", Code="3.2.1", Requirement="Observed sample session (delivery style, clarity, engagement, etc.)." },

        // 3.3 Course Material & Structure (3)
        new TqaRow { PartCode="3.3", Code="3.3.1", Requirement="Confirmed receipt & review of all course materials (guides, slides, etc.)." },
        new TqaRow { PartCode="3.3", Code="3.3.2", Requirement="Facilitator is familiar with assessment process & requirements (summative, formative)." },
        new TqaRow { PartCode="3.3", Code="3.3.3", Requirement="Facilitator has a clear plan for delivery (timings, activities, assessments, etc.)." },

        // 3.4 Venue & Equipment (6)
        new TqaRow { PartCode="3.4", Code="3.4.1", Requirement="Completed organisation/site induction." },
        new TqaRow { PartCode="3.4", Code="3.4.2", Requirement="Familiar with access controls, reporting lines & contingency plans." },
        new TqaRow { PartCode="3.4", Code="3.4.3", Requirement="Verified equipment availability & condition." },
        new TqaRow { PartCode="3.4", Code="3.4.4", Requirement="Understands material & consumable requirements (use, replenishment, costing, budget, etc.)." },
        new TqaRow { PartCode="3.4", Code="3.4.5", Requirement="Proficient with required teaching tools/platforms." },
        new TqaRow { PartCode="3.4", Code="3.4.6", Requirement="Knows/understands safety standards & emergency procedures (including PPE)." },

        // 3.5 Learners (3)
        new TqaRow { PartCode="3.5", Code="3.5.1", Requirement="Knows group size & general background." },
        new TqaRow { PartCode="3.5", Code="3.5.2", Requirement="List of learner names, emergency contacts, special needs, etc." },
        new TqaRow { PartCode="3.5", Code="3.5.3", Requirement="Understands confidentiality of learner information (POPI Act)." },

        // 3.6 Policies & Procedures (4)
        new TqaRow { PartCode="3.6", Code="3.6.1", Requirement="Completed company-specific induction." },
        new TqaRow { PartCode="3.6", Code="3.6.2", Requirement="Familiar with company policies & procedures and how/where to access them." },
        new TqaRow { PartCode="3.6", Code="3.6.3", Requirement="Understands KPIs related to role and frequency of evaluation." },
        new TqaRow { PartCode="3.6", Code="3.6.4", Requirement="Familiar with process for managing & reporting learner performance & discipline." },
    });

            return true;
        }

        // =======================================================
        // STEP 6 — PART 4: LEARNER PREPAREDNESS (grid)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step6(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            bool seeded = EnsureLearnerSeed(data.Learner);
            if (seeded)
            {
                wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            return View(data.Learner); // /Views/TrainingQualityAssurance/Step6.cshtml
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step6(int id, TqaLearner model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            EnsureLearnerSeed(data.Learner);     // keep shape
            data.Learner = model ?? new TqaLearner();

            wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step5), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step7), new { id })
            };
        }
        // ---- Seeding helper (idempotent) – Excel-accurate (13 criteria) ----------
        private static bool EnsureLearnerSeed(TqaLearner sec)
        {
            if (sec == null) return false;
            if (sec.Parts.Count > 0 || sec.Rows.Count > 0) return false;

            sec.Parts.AddRange(new[]
            {
        new TqaPart { PartCode = "4.1", Title = "Entry Requirements" },
        new TqaPart { PartCode = "4.2", Title = "Attendance" },
        new TqaPart { PartCode = "4.3", Title = "Learner Details" },
        new TqaPart { PartCode = "4.4", Title = "Induction" },
        new TqaPart { PartCode = "4.5", Title = "Materials" },
    });

            sec.Rows.AddRange(new[]
            {
        // 4.1 Entry Requirements (4)
        new TqaRow { PartCode="4.1", Code="4.1.1", Requirement="Learners meet entry requirements" },
        new TqaRow { PartCode="4.1", Code="4.1.2", Requirement="Learner prerequisites have been verified against course requirements" },
        new TqaRow { PartCode="4.1", Code="4.1.3", Requirement="RPL has been completed & verified (if applicable)" },
        new TqaRow { PartCode="4.1", Code="4.1.4", Requirement="All learner registration documents completed & submitted with supporting documents (Certified ID)" },

        // 4.2 Attendance (2)
        new TqaRow { PartCode="4.2", Code="4.2.1", Requirement="Learner attendance is confirmed." },
        new TqaRow { PartCode="4.2", Code="4.2.3", Requirement="Learners informed – training venue(s), dates, times, objectives, contact person, etc. (clear communication)" },

        // 4.3 Learner Details (2)
        new TqaRow { PartCode="4.3", Code="4.3.1", Requirement="Learner registration details confirmed (correct spelling of names, contact information, etc.)" },
        new TqaRow { PartCode="4.3", Code="4.3.2", Requirement="Special needs identified; accommodation process in place (where feasible), handled confidentially" },

        // 4.4 Induction (4)
        new TqaRow { PartCode="4.4", Code="4.4.1", Requirement="Learner induction completed (signed code of conduct)" },
        new TqaRow { PartCode="4.4", Code="4.4.2", Requirement="Site-specific orientation/induction completed" },
        new TqaRow { PartCode="4.4", Code="4.4.3", Requirement="Health & safety induction completed" },
        new TqaRow { PartCode="4.4", Code="4.4.4", Requirement="Learners know & understand all requirements to successfully complete training & consequences of poor performance" },

        // 4.5 Materials (1)
        new TqaRow { PartCode="4.5", Code="4.5.1", Requirement="Learners informed how/when they will receive learning materials & resources (hard copy/digital?)" },
    });

            return true;
        }


        // =======================================================
        // STEP 7 — PART 5: ADMINISTRATION & SUPPORT (grid)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step7(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            bool seeded = EnsureAdminSupportSeed(data.AdminSupport);
            if (seeded)
            {
                wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            return View(data.AdminSupport); // /Views/TrainingQualityAssurance/Step7.cshtml
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step7(int id, TqaAdminSupport model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            EnsureAdminSupportSeed(data.AdminSupport); // keep shape
            data.AdminSupport = model ?? new TqaAdminSupport();

            wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step6), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step8), new { id })
            };
        }

        // ---- Seeding helper (idempotent) – Excel-accurate (23 criteria) ----------
        private static bool EnsureAdminSupportSeed(TqaAdminSupport sec)
        {
            if (sec == null) return false;
            if (sec.Parts.Count > 0 || sec.Rows.Count > 0) return false;

            sec.Parts.AddRange(new[]
            {
        new TqaPart { PartCode = "5.1", Title = "On-Site Registration" },
        new TqaPart { PartCode = "5.2", Title = "Learner Support" },
        new TqaPart { PartCode = "5.3", Title = "Materials & Equipment" },
        new TqaPart { PartCode = "5.4", Title = "Support Staff" },
        new TqaPart { PartCode = "5.5", Title = "Feedback & Collection" },
        new TqaPart { PartCode = "5.6", Title = "Data Protection (POPIA)" },
        new TqaPart { PartCode = "5.7", Title = "Stakeholders" },
        new TqaPart { PartCode = "5.8", Title = "Logistics" },
    });

            sec.Rows.AddRange(new[]
            {
        // 5.1 On-Site Registration (3)
        new TqaRow { PartCode="5.1", Code="5.1.1", Requirement="Clear process for learner sign-in" },
        new TqaRow { PartCode="5.1", Code="5.1.2", Requirement="Attendance registers ready & accurate" },
        new TqaRow { PartCode="5.1", Code="5.1.3", Requirement="Learner site access (name badges, access cards, passwords, etc.)" },

        // 5.2 Learner Support (4)
        new TqaRow { PartCode="5.2", Code="5.2.1", Requirement="Process for learners to raise queries or concerns" },
        new TqaRow { PartCode="5.2", Code="5.2.2", Requirement="Designated person/department to deal with learner queries & concerns" },
        new TqaRow { PartCode="5.2", Code="5.2.3", Requirement="Anonymous reporting system for ethical, compliance or integrity concerns" },
        new TqaRow { PartCode="5.2", Code="5.2.4", Requirement="Process for completion of assessments and workplace components" },

        // 5.3 Materials & Equipment (6)
        new TqaRow { PartCode="5.3", Code="5.3.1", Requirement="Clear process for distributing materials (aligns with 4.5.1 Learner Preparedness)" },
        new TqaRow { PartCode="5.3", Code="5.3.2", Requirement="Available learner guides/workbooks/assessment sheets/workplace guides verified against learner numbers" },
        new TqaRow { PartCode="5.3", Code="5.3.3", Requirement="Access to and function of digital resources confirmed" },
        new TqaRow { PartCode="5.3", Code="5.3.4", Requirement="Process & responsible person for equipment issue and ongoing control" },
        new TqaRow { PartCode="5.3", Code="5.3.5", Requirement="Process & responsible person for repair and replacement of equipment" },
        new TqaRow { PartCode="5.3", Code="5.3.6", Requirement="Equipment availability & condition verified against Part 2: Equipment Register" },

        // 5.4 Support Staff (2)
        new TqaRow { PartCode="5.4", Code="5.4.1", Requirement="Support departments/staff roles & responsibilities clearly defined" },
        new TqaRow { PartCode="5.4", Code="5.4.2", Requirement="Process for support requests; contact information provided" },

        // 5.5 Feedback & Collection (3)
        new TqaRow { PartCode="5.5", Code="5.5.1", Requirement="Learner feedback forms available; frequency & process for completion & submission" },
        new TqaRow { PartCode="5.5", Code="5.5.2", Requirement="Facilitator feedback & learner progress reporting — frequency & submission process" },
        new TqaRow { PartCode="5.5", Code="5.5.3", Requirement="Responsible person for collection, correlation & reporting on learner/facilitator feedback" },

        // 5.6 Data Protection (POPIA) (2)
        new TqaRow { PartCode="5.6", Code="5.6.1", Requirement="Learner registration & personal information handled securely (physical/digital)" },
        new TqaRow { PartCode="5.6", Code="5.6.2", Requirement="Data is only used for the intended purpose" },

        // 5.7 Stakeholders (2)
        new TqaRow { PartCode="5.7", Code="5.7.1", Requirement="Community partner/employer actively involved & supportive" },
        new TqaRow { PartCode="5.7", Code="5.7.2", Requirement="Stakeholder MOUs in place; clear communication, meeting minutes & reporting structures" },

        // 5.8 Logistics (1)
        new TqaRow { PartCode="5.8", Code="5.8.1", Requirement="Logistics in place for staff/learner transport & catering (if applicable)" },
    });

            return true;
        }

        // =======================================================
        // STEP 8 — PART 6: RISK & CONTINGENCY (grid)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Step8(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            bool seeded = EnsureRiskSeed(data.Risk);
            if (seeded)
            {
                wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            ViewBag.Id = id;
            return View(data.Risk); // /Views/TrainingQualityAssurance/Step8.cshtml
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step8(int id, TqaRisk model, string? nav = "save")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var data = ParseData(wb);
            EnsureRiskSeed(data.Risk);  // keep table shape
            data.Risk = model ?? new TqaRisk();

            wb.Data = System.Text.Json.JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var n = (nav ?? "save").ToLowerInvariant();
            if (n == "prev")
            {
                return RedirectToAction(nameof(Step7), new { id });
            }
            if (n == "save")
            {
                // Stay in-progress; do NOT mark completed
                return RedirectToAction("Index", "Workbooks");
            }

            // n == "next" (Finish): mark workbook completed
            wb.Status = SubmissionStatus.Completed;
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return RedirectToAction("Index", "Workbooks");
        }


        // ---- Seeding helper (idempotent) – Excel-accurate (7 criteria) ----------
        private static bool EnsureRiskSeed(TqaRisk sec)
        {
            if (sec == null) return false;
            if (sec.Parts.Count > 0 || sec.Rows.Count > 0) return false;

            sec.Parts.AddRange(new[]
            {
        new TqaPart { PartCode = "6.1", Title = "Site Risk Assessment" },
        new TqaPart { PartCode = "6.2", Title = "Contingency Plans" },
        new TqaPart { PartCode = "6.3", Title = "Communication Protocols" },
    });

            sec.Rows.AddRange(new[]
            {
        // 6.1 Site Risk Assessment (2)
        new TqaRow { PartCode="6.1", Code="6.1.1", Requirement="Site risk assessment has been conducted & recorded" },
        new TqaRow { PartCode="6.1", Code="6.1.2", Requirement="All specific risks identified and mitigation actions implemented" },

        // 6.2 Contingency Plans (3)
        new TqaRow { PartCode="6.2", Code="6.2.1", Requirement="Current contingency plan in place" },
        new TqaRow { PartCode="6.2", Code="6.2.2", Requirement="Plan addresses identified risks (facilitator absence, learner dropouts, tech failures, power outages, etc.)" },
        new TqaRow { PartCode="6.2", Code="6.2.3", Requirement="Contingency plan tested; steps taken to address non-compliances" },

        // 6.3 Communication Protocols (2)
        new TqaRow { PartCode="6.3", Code="6.3.1", Requirement="Lines of communication clearly defined; contact information available and current" },
        new TqaRow { PartCode="6.3", Code="6.3.2", Requirement="Escalation process clearly defined with realistic timelines" },
    });

            return true;
        }

        // =======================================================
        // Helpers
        // =======================================================
        private async Task<WorkbookSubmission?> LoadScopedAsync(int id, bool track = false)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return null;

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            IQueryable<WorkbookSubmission> q = track
                ? _db.WorkbookSubmissions
                : _db.WorkbookSubmissions.AsNoTracking();

            var wb = await q.FirstOrDefaultAsync(x => x.Id == id && x.WorkbookType == WorkbookType.Workbook3);
            if (wb is null) return null;

            if (!isSuper)
            {
                if (me.CompanyId is null || wb.CompanyId != me.CompanyId) return null;
            }
            return wb;
        }

        private static TqaData ParseData(WorkbookSubmission wb)
        {
            if (string.IsNullOrWhiteSpace(wb.Data))
                return TqaData.CreateDefault();

            try
            {
                return JsonSerializer.Deserialize<TqaData>(wb.Data) ?? TqaData.CreateDefault();
            }
            catch
            {
                return TqaData.CreateDefault();
            }
        }
    }
}
