using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        // Canonical programme types (order preserved)
        private static readonly string[] ProgrammeTypesOrdered = new[]
        {
            "Short Course (Non Credit)",
            "Short Course (Credit Bearing)",
            "Skills Programmme",
            "General Certificate",
            "General Occupational Certificate",
            "Elementary Certificate",
            "Elementary Occupational Certificate",
            "Intermediate Certificate",
            "Intermediate Occupational Certificate",
            "National Certificate",
            "National Occupational Certificate",
            "Higher Certificate",
            "Higher Occupational Certificate",
            "Advanced Occupational Certificate",
            "Occupational Diploma",
            "Diploma",
            "Advanced Certificate",
            "Advanced Occupational Diploma",
            "Advanced Diploma",
            "Specialised Occupational Diploma",
            "Bachelor's Degree",
            "Postgraduate Diploma",
            "Bachelor's Honours Degree",
            "Master's Degree",
            "Professional Master's Degree",
            "Doctoral Degree",
            "Professional Doctorate"
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

        // ------- reflection helpers for Step2 sync -------
        private static string GetStringProp(object obj, params string[] candidates)
        {
            var t = obj.GetType();
            foreach (var name in candidates)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null)
                {
                    var v = p.GetValue(obj)?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) return v!;
                }
            }
            return "";
        }

        private static void TrySetString(object obj, string propName, string value)
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null || !p.CanWrite) return;
            if (p.PropertyType == typeof(string))
            {
                p.SetValue(obj, value);
            }
        }

        private static void TrySetBool(object obj, string propName, bool value)
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null || !p.CanWrite) return;
            var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (pt == typeof(bool))
            {
                p.SetValue(obj, value);
            }
        }

        private static void TrySetInt(object obj, string propName, int value)
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null || !p.CanWrite) return;
            var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (pt == typeof(int))
            {
                p.SetValue(obj, value);
            }
        }

        private static void SyncOverviewQualificationsFromStep7(OrgInfoData data)
        {
            var src = data?.Qualifications?.Items ?? new List<QualificationCourseRow>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in src)
            {
                var t = GetStringProp(row, "Type", "ProgrammeType", "ProgramType", "QualificationType");
                if (string.IsNullOrWhiteSpace(t)) continue;
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }

            var dataType = data!.GetType();
            var sec3Prop = dataType.GetProperty("Section3", BindingFlags.Public | BindingFlags.Instance);
            if (sec3Prop == null) return;

            var sec3 = sec3Prop.GetValue(data);
            if (sec3 == null)
            {
                var sec3Instance = Activator.CreateInstance(sec3Prop.PropertyType);
                sec3Prop.SetValue(data, sec3Instance);
                sec3 = sec3Instance;
            }

            var qProp = sec3!.GetType().GetProperty("Qualifications", BindingFlags.Public | BindingFlags.Instance);
            if (qProp == null || !qProp.CanWrite) return;

            var qListType = qProp.PropertyType;
            if (!qListType.IsGenericType || qListType.GetGenericTypeDefinition() != typeof(List<>)) return;
            var elemType = qListType.GetGenericArguments()[0];

            var orderedKeys = new List<string>();
            foreach (var pt in ProgrammeTypesOrdered)
            {
                if (counts.ContainsKey(pt)) orderedKeys.Add(pt);
            }
            foreach (var extra in counts.Keys.OrderBy(k => k))
            {
                if (!orderedKeys.Contains(extra)) orderedKeys.Add(extra);
            }

            var listType = typeof(List<>).MakeGenericType(elemType);
            var list = (IList)Activator.CreateInstance(listType)!;

            foreach (var key in orderedKeys)
            {
                var count = counts[key];
                var item = Activator.CreateInstance(elemType)!;

                TrySetString(item, "Type", key);
                TrySetString(item, "ProgrammeType", key);
                TrySetString(item, "ProgramType", key);
                TrySetString(item, "QualificationType", key);
                TrySetString(item, "Name", key);

                TrySetBool(item, "Offered", count > 0);
                TrySetBool(item, "IsOffered", count > 0);

                TrySetInt(item, "Quantity", count);
                TrySetInt(item, "Qty", count);
                TrySetInt(item, "Count", count);

                list.Add(item);
            }

            qProp.SetValue(sec3, list);
        }

        // ------- NEW: map completed Step10 rows to historical rows -------
        private static List<StudentHistoricalRow> BuildHistoricalFromCompleted(OrgInfoStudentCurrentSection current)
        {
            var result = new List<StudentHistoricalRow>();
            if (current?.Rows == null) return result;

            foreach (var r in current.Rows.Where(x => x != null && x.Completed))
            {
                var hist = new StudentHistoricalRow
                {
                    ProgrammeType = r.ProgrammeType,
                    African = new GenderBreakdown
                    {
                        M = r.African?.M ?? 0,
                        MD = r.African?.MD ?? 0,
                        F = r.African?.F ?? 0,
                        FD = r.African?.FD ?? 0
                    },
                    Coloured = new GenderBreakdown
                    {
                        M = r.Coloured?.M ?? 0,
                        MD = r.Coloured?.MD ?? 0,
                        F = r.Coloured?.F ?? 0,
                        FD = r.Coloured?.FD ?? 0
                    },
                    Indian = new GenderBreakdown
                    {
                        M = r.Indian?.M ?? 0,
                        MD = r.Indian?.MD ?? 0,
                        F = r.Indian?.F ?? 0,
                        FD = r.Indian?.FD ?? 0
                    },
                    White = new GenderBreakdown
                    {
                        M = r.White?.M ?? 0,
                        MD = r.White?.MD ?? 0,
                        F = r.White?.F ?? 0,
                        FD = r.White?.FD ?? 0
                    }
                };

                // total by race/gender
                int total =
                    (hist.African.M ?? 0) + (hist.African.MD ?? 0) + (hist.African.F ?? 0) + (hist.African.FD ?? 0) +
                    (hist.Coloured.M ?? 0) + (hist.Coloured.MD ?? 0) + (hist.Coloured.F ?? 0) + (hist.Coloured.FD ?? 0) +
                    (hist.Indian.M ?? 0) + (hist.Indian.MD ?? 0) + (hist.Indian.F ?? 0) + (hist.Indian.FD ?? 0) +
                    (hist.White.M ?? 0) + (hist.White.MD ?? 0) + (hist.White.F ?? 0) + (hist.White.FD ?? 0);

                hist.Total = total;

                int sc = r.SC ?? 0;
                int pr = r.PR ?? 0;
                int di = r.DI ?? 0;

                hist.SC = sc;
                hist.PR = pr;
                hist.DI = di;

                if (total > 0)
                {
                    hist.SCPercent = Math.Round((decimal)sc * 100m / total, 2);
                    hist.PRPercent = Math.Round((decimal)pr * 100m / total, 2);
                    hist.DIPercent = Math.Round((decimal)di * 100m / total, 2);
                }
                else
                {
                    hist.SCPercent = null;
                    hist.PRPercent = null;
                    hist.DIPercent = null;
                }

                hist.VAR = total - (sc + pr + di);

                result.Add(hist);
            }

            return result;
        }

        // ------- START -------
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

            var draft = new WorkbookSubmission
            {
                Title = $"Organisation Information - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook1,
                Status = SubmissionStatus.Draft,
                CompanyId = resolvedCompanyId,
                UserId = me.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Data = JsonSerializer.Serialize(OrgInfoData.CreateDefault(), JsonOpts)
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

            var data = ParseData(wb);

            // Build existing overview aggregates
            OrgInfoOverviewBuilder.Build(data);

            // Ensure Section3 shows Q/P/C by TYPE using items captured in Step7
            SyncOverviewQualificationsFromStep7(data);

            // NOTE: no save here; Step2 is read-only and we render from 'data'
            return View(data);
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
                return View(model);
            }

            var data = ParseData(wb);
            data.Section1 = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step2), new { id });

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
                return View(model);
            }

            var data = ParseData(wb);
            data.Board = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step3), new { id });

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
                return View(model);
            }

            var data = ParseData(wb);
            data.Employment = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step4), new { id });

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
                return View(model);
            }

            var data = ParseData(wb);
            data.Campuses = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step5), new { id });

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
                return View(model);
            }

            var data = ParseData(wb);
            data.Qualifications = model;
            SaveData(wb, data);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step6), new { id });

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

            var current = data.StudentCurrent ?? new OrgInfoStudentCurrentSection();
            var rows = current.Rows ?? new List<StudentCurrentRow>();

            // snapshot shows only Completed rows
            var snapshot = rows
                .Where(r => r != null && r.Completed && !string.IsNullOrWhiteSpace(r.ProgrammeType))
                .ToList();

            ViewBag.CurrentRows = snapshot;

            // sync period
            if (current.PeriodFrom.HasValue) data.StudentHistorical.PeriodFrom = current.PeriodFrom;
            if (current.PeriodTo.HasValue) data.StudentHistorical.PeriodTo = current.PeriodTo;
            if (current.Months.HasValue) data.StudentHistorical.Months = current.Months;

            // *** Auto-prefill historical rows ONCE (if empty) from completed Step10 rows ***
            if (data.StudentHistorical.Rows == null || data.StudentHistorical.Rows.Count == 0)
            {
                data.StudentHistorical.Rows = BuildHistoricalFromCompleted(current);
            }

            SaveData(wb, data);
            await _db.SaveChangesAsync();

            ViewBag.Id = id;
            return View(data.StudentHistorical);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step9(int id, OrgInfoStudentHistoricalSection model, string? nav = "save")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            var n = (nav ?? "save").ToLowerInvariant();

            // ========= NEW: explicit "refresh" branch (runs BEFORE ModelState check) =========
            if (n == "refresh")
            {
                // Rebuild historical rows from COMPLETED Step10 rows and sync the period
                var data = ParseData(wb);
                data.StudentHistorical ??= new OrgInfoStudentHistoricalSection();

                var current = data.StudentCurrent ?? new OrgInfoStudentCurrentSection();
                data.StudentHistorical.Rows = BuildHistoricalFromCompleted(current);

                if (current.PeriodFrom.HasValue) data.StudentHistorical.PeriodFrom = current.PeriodFrom;
                if (current.PeriodTo.HasValue) data.StudentHistorical.PeriodTo = current.PeriodTo;
                if (current.Months.HasValue) data.StudentHistorical.Months = current.Months;

                SaveData(wb, data);
                await _db.SaveChangesAsync();

                return RedirectToAction(nameof(Step9), new { id });
            }
            // ========= /NEW =========

            if (!ModelState.IsValid)
            {
                var dataForError = ParseData(wb);
                var snapshot = dataForError.StudentCurrent?.Rows?
                    .Where(r => r != null && r.Completed && !string.IsNullOrWhiteSpace(r.ProgrammeType))
                    .ToList();
                ViewBag.CurrentRows = snapshot;
                ViewBag.Id = id;
                return View(model);
            }

            var dataOk = ParseData(wb);
            dataOk.StudentHistorical = model;
            SaveData(wb, dataOk);
            await _db.SaveChangesAsync();

            if (string.Equals(nav, "prev", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Step8), new { id });

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

            if (n == "next")
            {
                wb.Status = SubmissionStatus.Completed;
                wb.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return RedirectToAction("Index", "Workbooks");
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Step10), new { id });
        }
    }
}
