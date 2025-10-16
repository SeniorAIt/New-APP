using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Controllers
{
    [Authorize]
    [Route("QualityAssurance/[action]")]
    [Route("Qa/[action]")]
    public class QualityAssuranceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        public QualityAssuranceController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        // ---------- START ----------
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
                Title = $"Quality Assurance - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                WorkbookType = WorkbookType.Workbook2,
                Status = SubmissionStatus.Draft,
                CompanyId = resolvedCompanyId,
                UserId = me.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Data = JsonSerializer.Serialize(QaData.CreateDefault(), JsonOpts)
            };

            _db.Add(draft);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Step1), new { id = draft.Id });
        }

        // ===== STEP 1 — OVERVIEW =====
        [HttpGet]
        public async Task<IActionResult> Step1(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            // Determine read-only using the correct enum: SubmissionBundleStatus
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;

            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            // Top nav needs this
            ViewBag.Id = id;

            var data = ParseData(wb);
            return View(data.Overview);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step1(int id, QaOverview model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Guard writes when bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;

            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step1), new { id });
            }

            var data = ParseData(wb);
            data.Overview = model;
            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction("Index", "Workbooks"),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step2), new { id })
            };
        }

        // ===== STEP 2 — GUIDE =====
        [HttpGet]
        public async Task<IActionResult> Step2(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            // Top nav requires this
            ViewBag.Id = id;

            return View(); // your existing Step2 view has no model
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2(int id, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes if read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step2), new { id });
            }

            // No data to persist for Guide; keep your timestamp update
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step1), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step3), new { id })
            };
        }

        // ===== STEP 3 — SUMMARY =====
        [HttpGet]
        public async Task<IActionResult> Step3(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);

            // Build per-category metrics using the same CI rules:
            // applicable = CI in {2,3}, compliant = CI==3, not compliant = CI==2
            (int criteria, int applicable, int comp, int ncomp, double compPct, double ncompPct) m;

            var labels = new List<string>();
            var compPct = new List<double>();
            var ncompPct = new List<double>();

            void AddCategory(string code, List<GelRow>? rows)
            {
                labels.Add(code);
                m = Calc(rows);
                compPct.Add(m.compPct);
                ncompPct.Add(m.ncompPct);
            }

            AddCategory("GEL", data.GEL?.Rows);
            AddCategory("SPR", data.SPR?.Rows);
            AddCategory("TLA", data.TLA?.Rows);
            AddCategory("LSW", data.LSW?.Rows);
            AddCategory("SCE", data.SCE?.Rows);
            AddCategory("RLE", data.RLE?.Rows);
            AddCategory("QMI", data.QMI?.Rows);
            AddCategory("SEC", data.SEC?.Rows);
            AddCategory("LCR", data.LCR?.Rows);

            ViewBag.CatLabels = labels.ToArray();
            ViewBag.CatComp = compPct.ToArray();   // 0..1 fractions (of applicable)
            ViewBag.CatNComp = ncompPct.ToArray();  // 0..1 fractions (of applicable)

            // Helper: build a part breakdown (labels + comp + ncomp) for a section
            static (string[] labels, double[] comp, double[] ncomp) BuildPartBreakdown(
                Func<List<GelRow>?, (int, int, int, int, double, double)> calc,
                List<GelRow>? rows)
            {
                var partLabels = new List<string>();
                var partComp = new List<double>();
                var partNComp = new List<double>();

                if (rows != null && rows.Count > 0)
                {
                    var grouped = rows
                        .GroupBy(r => r.PartCode)
                        .OrderBy(g => g.Key);

                    foreach (var g in grouped)
                    {
                        var mm = calc(g.ToList());
                        partLabels.Add(g.Key);
                        partComp.Add(mm.Item5);   // compPct
                        partNComp.Add(mm.Item6);  // ncompPct
                    }
                }

                return (partLabels.ToArray(), partComp.ToArray(), partNComp.ToArray());
            }

            // Compute breakdown for each section and place in ViewBags used by the view
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.GEL?.Rows);
                ViewBag.GelLabels = l; ViewBag.GelComp = c; ViewBag.GelNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.SPR?.Rows);
                ViewBag.SprLabels = l; ViewBag.SprComp = c; ViewBag.SprNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.TLA?.Rows);
                ViewBag.TlaLabels = l; ViewBag.TlaComp = c; ViewBag.TlaNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.LSW?.Rows);
                ViewBag.LswLabels = l; ViewBag.LswComp = c; ViewBag.LswNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.SCE?.Rows);
                ViewBag.SceLabels = l; ViewBag.SceComp = c; ViewBag.SceNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.RLE?.Rows);
                ViewBag.RleLabels = l; ViewBag.RleComp = c; ViewBag.RleNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.QMI?.Rows);
                ViewBag.QmiLabels = l; ViewBag.QmiComp = c; ViewBag.QmiNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.SEC?.Rows);
                ViewBag.SecLabels = l; ViewBag.SecComp = c; ViewBag.SecNComp = n;
            }
            {
                var (l, c, n) = BuildPartBreakdown(Calc, data.LCR?.Rows);
                ViewBag.LcrLabels = l; ViewBag.LcrComp = c; ViewBag.LcrNComp = n;
            }

            // Top nav needs this
            ViewBag.Id = id;

            return View(data.Summary); // Keep QaSummary as the page model (notes etc.)
        }


        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step3(int id, QaSummary model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when bundle is read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step3), new { id });
            }

            var data = ParseData(wb);
            data.Summary = model;
            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step2), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step4), new { id })
            };
        }

        // ===== STEP 4 — INFO =====
        private const int MaxSignatureDataUrlLength = 500_000; // ~500 KB upper bound
        private static readonly HashSet<string> AllowedSignatureMime = new(StringComparer.OrdinalIgnoreCase)
{
    "image/png", "image/jpeg"
};

        private static bool TryValidateSignatureDataUrl(string? dataUrl, out string normalized, out string? error)
        {
            normalized = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                // Empty is allowed (means "no change")
                return true;
            }

            dataUrl = dataUrl.Trim();

            // Must be a data URL: data:[mime];base64,XXXXX
            const string prefix = "data:";
            var commaIndex = dataUrl.IndexOf(',');
            if (!dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || commaIndex < 0)
            {
                error = "Invalid signature format.";
                return false;
            }

            var header = dataUrl.Substring(0, commaIndex); // e.g. data:image/png;base64
            var payload = dataUrl[(commaIndex + 1)..];     // base64 data

            // Check base64 flag in header
            if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                error = "Signature must be Base64 encoded.";
                return false;
            }

            // Extract MIME
            // header is like: data:image/png;base64
            var mimePart = header[prefix.Length..]; // image/png;base64
            var semi = mimePart.IndexOf(';');
            var mime = semi > 0 ? mimePart.Substring(0, semi).Trim() : mimePart.Trim();

            if (!AllowedSignatureMime.Contains(mime))
            {
                error = "Signature must be a PNG or JPEG image.";
                return false;
            }

            // Size cap (data URL length is a decent proxy)
            if (dataUrl.Length > MaxSignatureDataUrlLength)
            {
                error = "Signature image is too large. Please upload a smaller image or draw a lighter signature.";
                return false;
            }

            // Base64 integrity
            try
            {
                // Some browsers add newlines/spaces; strip whitespace
                var base64 = string.Concat(payload.Where(c => !char.IsWhiteSpace(c)));
                _ = Convert.FromBase64String(base64); // throws if invalid

                // Normalize: rebuild the data URL compactly
                normalized = $"data:{mime};base64,{base64}";
                return true;
            }
            catch
            {
                error = "Signature image data is corrupted or invalid.";
                return false;
            }
        }

        [HttpGet]
        public async Task<IActionResult> Step4(int id)
        {
            var wb = await LoadScopedAsync(id);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            ViewBag.Id = id; // top nav

            var data = ParseData(wb);

            // Supply signature previews/timestamps for the view
            ViewBag.OrgRepresentativeSignature = data.Info.OrgRepresentativeSignature ?? "";
            ViewBag.AssessorSignature = data.Info.AssessorSignature ?? "";

            string? fmt(DateTime? utc) => utc.HasValue ? utc.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : null;
            ViewBag.OrgSignedAtDisplay = fmt(data.Info.OrgSignedAtUtc) ?? "";
            ViewBag.AssessorSignedAtDisplay = fmt(data.Info.AssessorSignedAtUtc) ?? "";

            return View(data.Info);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step4(int id, QaInfo model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step4), new { id });
            }

            var data = ParseData(wb);

            // Update non-signature fields explicitly
            data.Info.AppetdRegNo = model.AppetdRegNo;
            data.Info.TradingName = model.TradingName;
            data.Info.OrganisationType = model.OrganisationType;
            data.Info.SiteDepartment = model.SiteDepartment;
            data.Info.StreetAddress1 = model.StreetAddress1;
            data.Info.StreetAddress2 = model.StreetAddress2;
            data.Info.Town = model.Town;
            data.Info.Province = model.Province;
            data.Info.Suburb = model.Suburb;
            data.Info.Zip = model.Zip;
            data.Info.ContactPerson = model.ContactPerson;
            data.Info.ContactNumber = model.ContactNumber;
            data.Info.Email = model.Email;

            data.Info.AssessmentDate = model.AssessmentDate;
            data.Info.OrganisationInfo = model.OrganisationInfo;
            data.Info.AccreditationStatus = model.AccreditationStatus;
            data.Info.SupportingDocsSubmitted = model.SupportingDocsSubmitted;
            data.Info.CaImplementationDeadlineDays = model.CaImplementationDeadlineDays;
            data.Info.ReassessmentDeadlineDays = model.ReassessmentDeadlineDays;
            data.Info.CaVerificationDate = model.CaVerificationDate;
            data.Info.ReassessmentDate = model.ReassessmentDate;

            data.Info.AssessorFullName = model.AssessorFullName;
            data.Info.AssessorOrganisation = model.AssessorOrganisation;
            data.Info.AssessorContactNumber = model.AssessorContactNumber;
            data.Info.AssessorEmail = model.AssessorEmail;

            // Read posted signature data URLs (pad OR upload)
            var postedOrgSig = (Request?.Form?["OrgRepresentativeSignature"].ToString() ?? "").Trim();
            var postedAssessorSig = (Request?.Form?["AssessorSignature"].ToString() ?? "").Trim();

            // Validate signatures (if posted)
            if (!string.IsNullOrEmpty(postedOrgSig))
            {
                if (!TryValidateSignatureDataUrl(postedOrgSig, out var normalized, out var err))
                    ModelState.AddModelError(nameof(QaInfo.OrgRepresentativeSignature), err!);
                else
                    postedOrgSig = normalized;
            }

            if (!string.IsNullOrEmpty(postedAssessorSig))
            {
                if (!TryValidateSignatureDataUrl(postedAssessorSig, out var normalized, out var err))
                    ModelState.AddModelError(nameof(QaInfo.AssessorSignature), err!);
                else
                    postedAssessorSig = normalized;
            }

            if (!ModelState.IsValid)
            {
                // Rehydrate ViewBags and return the same view with errors
                ViewBag.ReadOnly = false;
                ViewBag.Id = id;

                ViewBag.OrgRepresentativeSignature = data.Info.OrgRepresentativeSignature ?? "";
                ViewBag.AssessorSignature = data.Info.AssessorSignature ?? "";

                string? fmt(DateTime? utc) => utc.HasValue ? utc.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : null;
                ViewBag.OrgSignedAtDisplay = fmt(data.Info.OrgSignedAtUtc) ?? "";
                ViewBag.AssessorSignedAtDisplay = fmt(data.Info.AssessorSignedAtUtc) ?? "";

                return View(model);
            }

            var nowUtc = DateTime.UtcNow;

            // Company user/org representative may set their signature
            if (!string.IsNullOrEmpty(postedOrgSig) && (User.IsInRole("CompanyUser") || User.IsInRole("CompanyAdmin")))
            {
                data.Info.OrgRepresentativeSignature = postedOrgSig;
                data.Info.OrgSignedAtUtc = nowUtc;
            }

            // SuperAdmin (assessor) may set the assessor signature
            if (!string.IsNullOrEmpty(postedAssessorSig) && User.IsInRole("SuperAdmin"))
            {
                data.Info.AssessorSignature = postedAssessorSig;
                data.Info.AssessorSignedAtUtc = nowUtc;
            }

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



        // ===== STEP 5 — GEL =====
        [HttpGet]
        public async Task<IActionResult> Step5(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.GEL ??= new QaGEL();

            bool changed = false;

            // Only seed & prefill when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureGelSeed(data.GEL)) changed = true;

                // Prefill header from INFO (Step 4) if target fields are blank
                var info = data.Info;
                var s = data.GEL;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(s.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { s.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(s.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { s.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(s.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { s.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((s.AssessmentDate == null || s.AssessmentDate == default) && info.AssessmentDate != null)
                    { s.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((s.CaVerification == null || s.CaVerification == default) && info.CaVerificationDate != null)
                    { s.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((s.ReassessmentDate == null || s.ReassessmentDate == default) && info.ReassessmentDate != null)
                    { s.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // top nav
            return View(data.GEL);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step5(int id, QaGEL model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly) return RedirectToAction(nameof(Step5), new { id });

            var data = ParseData(wb);
            data.GEL ??= new QaGEL();

            // Ensure skeleton exists (parts/rows/text)
            EnsureGelSeed(data.GEL);

            if (model != null)
            {
                // Header fields
                data.GEL.Organisation = model.Organisation;
                data.GEL.Department = model.Department;
                data.GEL.AppetdRegNo = model.AppetdRegNo;
                data.GEL.AssessmentDate = model.AssessmentDate;
                data.GEL.CaVerification = model.CaVerification;
                data.GEL.ReassessmentDate = model.ReassessmentDate;

                // Merge row answers by key; fallback to index
                if (data.GEL.Rows != null && model.Rows != null)
                {
                    var map = data.GEL.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        if (map.TryGetValue($"{posted.PartCode}|{posted.Code}", out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.GEL.Rows.Count)
                        {
                            var t = data.GEL.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // No assignments to read-only computed properties here

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step4), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step6), new { id })
            };
        }

        // ===== STEP 6 — SPR =====
        [HttpGet]
        public async Task<IActionResult> Step6(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.SPR ??= new QaSPR();

            bool changed = false;

            // Only seed & prefill when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureSprSeed(data.SPR)) changed = true;

                // Prefill header from INFO (Step 4) if target fields are blank
                var info = data.Info;
                var s = data.SPR;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(s.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { s.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(s.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { s.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(s.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { s.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((s.AssessmentDate == null || s.AssessmentDate == default) && info.AssessmentDate != null)
                    { s.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((s.CaVerification == null || s.CaVerification == default) && info.CaVerificationDate != null)
                    { s.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((s.ReassessmentDate == null || s.ReassessmentDate == default) && info.ReassessmentDate != null)
                    { s.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // top nav
            return View(data.SPR);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step6(int id, QaSPR model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes if read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly) return RedirectToAction(nameof(Step6), new { id });

            var data = ParseData(wb);
            data.SPR ??= new QaSPR();

            // Ensure the static Parts/Rows skeleton exists
            EnsureSprSeed(data.SPR);

            if (model != null)
            {
                // Header fields (these are editable)
                data.SPR.Organisation = model.Organisation;
                data.SPR.Department = model.Department;
                data.SPR.AppetdRegNo = model.AppetdRegNo;
                data.SPR.AssessmentDate = model.AssessmentDate;
                data.SPR.CaVerification = model.CaVerification;
                data.SPR.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits by stable key PartCode|Code (fallback to index)
                if (data.SPR.Rows != null && model.Rows != null)
                {
                    var byKey = data.SPR.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        if (byKey.TryGetValue($"{posted.PartCode}|{posted.Code}", out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.SPR.Rows.Count)
                        {
                            var t = data.SPR.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Do NOT assign data.SPR.Criteria/Compliant/NotCompliant/Percent etc. (they're computed)

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step5), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step7), new { id })
            };
        }

        // ===== STEP 7 — TLA =====
        [HttpGet]
        public async Task<IActionResult> Step7(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.TLA ??= new QaTLA();

            bool changed = false;

            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureTlaSeed(data.TLA)) changed = true;

                // Prefill header from INFO (Step 4) if blank
                var info = data.Info;
                var t = data.TLA;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(t.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { t.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(t.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { t.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(t.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { t.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!t.AssessmentDate.HasValue || t.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { t.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!t.CaVerification.HasValue || t.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { t.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!t.ReassessmentDate.HasValue || t.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { t.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // top nav needs this
            return View(data.TLA);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step7(int id, QaTLA model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes in read-only mode
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly) return RedirectToAction(nameof(Step7), new { id });

            var data = ParseData(wb);
            data.TLA ??= new QaTLA();

            // Make sure the static skeleton exists (safe to call repeatedly)
            EnsureTlaSeed(data.TLA);

            if (model != null)
            {
                // Header fields
                data.TLA.Organisation = model.Organisation;
                data.TLA.Department = model.Department;
                data.TLA.AppetdRegNo = model.AppetdRegNo;
                data.TLA.AssessmentDate = model.AssessmentDate;
                data.TLA.CaVerification = model.CaVerification;
                data.TLA.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits by key (PartCode|Code); fallback to index if needed
                if (data.TLA.Rows != null && model.Rows != null)
                {
                    var byKey = data.TLA.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        if (byKey.TryGetValue($"{posted.PartCode}|{posted.Code}", out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.TLA.Rows.Count)
                        {
                            var t = data.TLA.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Do NOT assign computed fields like Criteria/Compliant/NotCompliant/NotApplicable/Percent

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step6), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step8), new { id })
            };
        }


        // ===== STEP 8 — LSW =====
        [HttpGet]
        public async Task<IActionResult> Step8(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.LSW ??= new QaLSW();

            bool changed = false;

            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureLswSeed(data.LSW)) changed = true;

                // Prefill header from INFO (Step 4) if blank
                var info = data.Info;
                var lsw = data.LSW;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(lsw.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { lsw.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(lsw.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { lsw.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(lsw.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { lsw.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!lsw.AssessmentDate.HasValue || lsw.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { lsw.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!lsw.CaVerification.HasValue || lsw.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { lsw.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!lsw.ReassessmentDate.HasValue || lsw.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { lsw.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // for top nav
            return View(data.LSW);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step8(int id, QaLSW model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly) return RedirectToAction(nameof(Step8), new { id });

            var data = ParseData(wb);
            data.LSW ??= new QaLSW();

            // Make sure static skeleton exists (safe to call more than once)
            EnsureLswSeed(data.LSW);

            if (model != null)
            {
                // Header fields
                data.LSW.Organisation = model.Organisation;
                data.LSW.Department = model.Department;
                data.LSW.AppetdRegNo = model.AppetdRegNo;
                data.LSW.AssessmentDate = model.AssessmentDate;
                data.LSW.CaVerification = model.CaVerification;
                data.LSW.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits (by PartCode|Code; fall back to index if needed)
                if (data.LSW.Rows != null && model.Rows != null)
                {
                    var byKey = data.LSW.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        if (byKey.TryGetValue($"{posted.PartCode}|{posted.Code}", out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.LSW.Rows.Count)
                        {
                            var t = data.LSW.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Don't assign computed props like Criteria/Compliant/NotCompliant/NotApplicable/Percent

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step7), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step9), new { id })
            };
        }

        // ===== STEP 9 — SCE =====
        [HttpGet]
        public async Task<IActionResult> Step9(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.SCE ??= new QaSCE();

            bool changed = false;

            // Only seed & prefill when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureSceSeed(data.SCE)) changed = true;

                // Prefill header from INFO (Step 4) if blank
                var info = data.Info;
                var s = data.SCE;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(s.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { s.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(s.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { s.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(s.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { s.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!s.AssessmentDate.HasValue || s.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { s.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!s.CaVerification.HasValue || s.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { s.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!s.ReassessmentDate.HasValue || s.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { s.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // top nav
            return View(data.SCE);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step9(int id, QaSCE model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly) return RedirectToAction(nameof(Step9), new { id });

            var data = ParseData(wb);
            data.SCE ??= new QaSCE();

            // Ensure static skeleton exists (idempotent)
            EnsureSceSeed(data.SCE);

            if (model != null)
            {
                // Header fields
                data.SCE.Organisation = model.Organisation;
                data.SCE.Department = model.Department;
                data.SCE.AppetdRegNo = model.AppetdRegNo;
                data.SCE.AssessmentDate = model.AssessmentDate;
                data.SCE.CaVerification = model.CaVerification;
                data.SCE.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits by PartCode|Code (fallback to index)
                if (data.SCE.Rows != null && model.Rows != null)
                {
                    var map = data.SCE.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        if (map.TryGetValue($"{posted.PartCode}|{posted.Code}", out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.SCE.Rows.Count)
                        {
                            var t = data.SCE.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Do NOT assign computed fields (Criteria/Compliant/NotCompliant/NotApplicable/Percent)

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (nav ?? "next").ToLowerInvariant() switch
            {
                "prev" => RedirectToAction(nameof(Step8), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction(nameof(Step10), new { id })
            };
        }

        // ===== STEP 10 — RLE (Resource Management & Learning Environment) =====
        [HttpGet]
        public async Task<IActionResult> Step10(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.RLE ??= new QaRLE();

            bool changed = false;

            // Seed & prefill only when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureRleSeed(data.RLE)) changed = true;

                // Prefill header from INFO (Step 4) if target fields are blank
                var info = data.Info;
                var rle = data.RLE;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(rle.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { rle.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(rle.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { rle.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(rle.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { rle.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!rle.AssessmentDate.HasValue || rle.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { rle.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!rle.CaVerification.HasValue || rle.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { rle.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!rle.ReassessmentDate.HasValue || rle.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { rle.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // for top nav
            return View(data.RLE);
        }


        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step10(int id, QaRLE model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step10), new { id });
            }

            var data = ParseData(wb);
            data.RLE ??= new QaRLE();

            // Make sure the static skeleton exists
            EnsureRleSeed(data.RLE);

            if (model != null)
            {
                // Header fields
                data.RLE.Organisation = model.Organisation;
                data.RLE.Department = model.Department;
                data.RLE.AppetdRegNo = model.AppetdRegNo;
                data.RLE.AssessmentDate = model.AssessmentDate;
                data.RLE.CaVerification = model.CaVerification;
                data.RLE.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits (do NOT overwrite static text/codes)
                if (data.RLE.Rows != null && model.Rows != null)
                {
                    var map = data.RLE.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        var key = $"{posted.PartCode}|{posted.Code}";
                        if (map.TryGetValue(key, out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.RLE.Rows.Count)
                        {
                            // Fallback by index if key not found
                            var t = data.RLE.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Do NOT assign computed summary props (Criteria/Compliant/NotCompliant/NotApplicable/Percent)

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            switch ((nav ?? "next").ToLowerInvariant())
            {
                case "prev": return RedirectToAction(nameof(Step9), new { id });
                case "save": return RedirectToAction("Index", "Workbooks");
                default: return RedirectToAction(nameof(Step11), new { id });
            }
        }

        // ===== STEP 11 — QMI (Quality Management, Monitoring & Improvement) =====
        [HttpGet]
        public async Task<IActionResult> Step11(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.QMI ??= new QaQMI();

            bool changed = false;

            // Seed & prefill only when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureQmiSeed(data.QMI)) changed = true;

                // Prefill header from INFO (Step 4) if target fields are blank
                var info = data.Info;
                var qmi = data.QMI;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(qmi.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { qmi.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(qmi.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { qmi.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(qmi.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { qmi.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!qmi.AssessmentDate.HasValue || qmi.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { qmi.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!qmi.CaVerification.HasValue || qmi.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { qmi.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!qmi.ReassessmentDate.HasValue || qmi.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { qmi.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // for top nav
            return View(data.QMI);
        }


        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step11(int id, QaQMI model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step11), new { id });
            }

            var data = ParseData(wb);
            data.QMI ??= new QaQMI();

            // Ensure skeleton exists so we only update editable fields
            EnsureQmiSeed(data.QMI);

            if (model != null)
            {
                // Header fields
                data.QMI.Organisation = model.Organisation;
                data.QMI.Department = model.Department;
                data.QMI.AppetdRegNo = model.AppetdRegNo;
                data.QMI.AssessmentDate = model.AssessmentDate;
                data.QMI.CaVerification = model.CaVerification;
                data.QMI.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits (preserve static: PartCode/Code/Requirement/Action)
                if (data.QMI.Rows != null && model.Rows != null)
                {
                    var map = data.QMI.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        var key = $"{posted.PartCode}|{posted.Code}";
                        if (map.TryGetValue(key, out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.QMI.Rows.Count)
                        {
                            // Fallback by index
                            var t = data.QMI.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Do NOT assign computed properties (Criteria/Compliant/NotCompliant/NotApplicable/Percent)

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            switch ((nav ?? "next").ToLowerInvariant())
            {
                case "prev": return RedirectToAction(nameof(Step10), new { id });
                case "save": return RedirectToAction("Index", "Workbooks");
                default: return RedirectToAction(nameof(Step12), new { id });
            }
        }

        // ===== STEP 12 — SEC (Stakeholder Engagement & Communication) =====
        [HttpGet]
        public async Task<IActionResult> Step12(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.SEC ??= new QaSEC();

            bool changed = false;

            // Seed & prefill only when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureSecSeed(data.SEC)) changed = true;

                // Prefill header from INFO (Step 4) if target fields are blank
                var info = data.Info;
                var sec = data.SEC;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(sec.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { sec.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(sec.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { sec.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(sec.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { sec.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!sec.AssessmentDate.HasValue || sec.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { sec.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!sec.CaVerification.HasValue || sec.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { sec.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!sec.ReassessmentDate.HasValue || sec.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { sec.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // top nav
            return View(data.SEC);
        }


        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step12(int id, QaSEC model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step12), new { id });
            }

            var data = ParseData(wb);
            data.SEC ??= new QaSEC();

            // Ensure the static skeleton exists so we only update editable fields
            EnsureSecSeed(data.SEC);

            if (model != null)
            {
                // Header fields
                data.SEC.Organisation = model.Organisation;
                data.SEC.Department = model.Department;
                data.SEC.AppetdRegNo = model.AppetdRegNo;
                data.SEC.AssessmentDate = model.AssessmentDate;
                data.SEC.CaVerification = model.CaVerification;
                data.SEC.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits, preserving static text (PartCode/Code/Requirement/Action)
                if (data.SEC.Rows != null && model.Rows != null)
                {
                    var map = data.SEC.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        var key = $"{posted.PartCode}|{posted.Code}";
                        if (map.TryGetValue(key, out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.SEC.Rows.Count)
                        {
                            // Fallback by index if key not found
                            var t = data.SEC.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Don't assign computed props (Criteria/Compliant/NotCompliant/NotApplicable/Percent)

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            switch ((nav ?? "next").ToLowerInvariant())
            {
                case "prev": return RedirectToAction(nameof(Step11), new { id });
                case "save": return RedirectToAction("Index", "Workbooks");
                default: return RedirectToAction(nameof(Step13), new { id });
            }
        }

        // ===== STEP 13 — LCR (Legal Compliance & Reporting) =====
        [HttpGet]
        public async Task<IActionResult> Step13(int id)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Read-only if parent bundle is Submitted/Approved
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            ViewBag.ReadOnly = readOnly;

            var data = ParseData(wb);
            data.LCR ??= new QaLCR();

            bool changed = false;

            // Only seed & prefill when NOT read-only
            if (!readOnly)
            {
                // Seed rubric rows once
                if (EnsureLcrSeed(data.LCR)) changed = true;

                // Prefill header from INFO (Step 4) if target fields are blank
                var info = data.Info;
                var lcr = data.LCR;
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(lcr.Organisation) && !string.IsNullOrWhiteSpace(info.TradingName))
                    { lcr.Organisation = info.TradingName; changed = true; }

                    if (string.IsNullOrWhiteSpace(lcr.Department) && !string.IsNullOrWhiteSpace(info.SiteDepartment))
                    { lcr.Department = info.SiteDepartment; changed = true; }

                    if (string.IsNullOrWhiteSpace(lcr.AppetdRegNo) && !string.IsNullOrWhiteSpace(info.AppetdRegNo))
                    { lcr.AppetdRegNo = info.AppetdRegNo; changed = true; }

                    if ((!lcr.AssessmentDate.HasValue || lcr.AssessmentDate == default) && info.AssessmentDate.HasValue)
                    { lcr.AssessmentDate = info.AssessmentDate; changed = true; }

                    if ((!lcr.CaVerification.HasValue || lcr.CaVerification == default) && info.CaVerificationDate.HasValue)
                    { lcr.CaVerification = info.CaVerificationDate; changed = true; }

                    if ((!lcr.ReassessmentDate.HasValue || lcr.ReassessmentDate == default) && info.ReassessmentDate.HasValue)
                    { lcr.ReassessmentDate = info.ReassessmentDate; changed = true; }
                }

                if (changed)
                {
                    wb.Data = JsonSerializer.Serialize(data, JsonOpts);
                    wb.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Id = id; // top nav
            return View(data.LCR);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Step13(int id, QaLCR model, string? nav = "next")
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            // Block writes when read-only
            var bundle = wb.SubmissionId.HasValue
                ? await _db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == wb.SubmissionId.Value)
                : null;
            var readOnly = bundle?.Status == SubmissionBundleStatus.Submitted
                        || bundle?.Status == SubmissionBundleStatus.Approved;
            if (readOnly)
            {
                return RedirectToAction(nameof(Step13), new { id });
            }

            var data = ParseData(wb);
            data.LCR ??= new QaLCR();

            // Ensure seeded/static content exists before we merge editable fields
            EnsureLcrSeed(data.LCR);

            if (model != null)
            {
                // Header fields
                data.LCR.Organisation = model.Organisation;
                data.LCR.Department = model.Department;
                data.LCR.AppetdRegNo = model.AppetdRegNo;
                data.LCR.AssessmentDate = model.AssessmentDate;
                data.LCR.CaVerification = model.CaVerification;
                data.LCR.ReassessmentDate = model.ReassessmentDate;

                // Merge row edits while preserving static columns (PartCode/Code/Requirement/Action)
                if (data.LCR.Rows != null && model.Rows != null)
                {
                    var map = data.LCR.Rows.ToDictionary(r => $"{r.PartCode}|{r.Code}");
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        var posted = model.Rows[i];
                        if (posted is null) continue;

                        var key = $"{posted.PartCode}|{posted.Code}";
                        if (map.TryGetValue(key, out var target))
                        {
                            target.CI = posted.CI;
                            target.CorrectiveAction = posted.CorrectiveAction;
                            target.AssignedTo = posted.AssignedTo;
                            target.CODate = posted.CODate;
                            target.CO = posted.CO;
                            target.VerifiedBy = posted.VerifiedBy;
                        }
                        else if (i < data.LCR.Rows.Count)
                        {
                            var t = data.LCR.Rows[i];
                            t.CI = posted.CI;
                            t.CorrectiveAction = posted.CorrectiveAction;
                            t.AssignedTo = posted.AssignedTo;
                            t.CODate = posted.CODate;
                            t.CO = posted.CO;
                            t.VerifiedBy = posted.VerifiedBy;
                        }
                    }
                }
            }

            // Do not assign read-only computed props (Criteria/Compliant/NotCompliant/NotApplicable/Percent)

            var action = (nav ?? "next").ToLowerInvariant();

            // Mark the workbook completed only on Finish (next/default here)
            if (action != "prev" && action != "save")
            {
                wb.Status = SubmissionStatus.Completed;
            }

            wb.Data = JsonSerializer.Serialize(data, JsonOpts);
            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return action switch
            {
                "prev" => RedirectToAction(nameof(Step12), new { id }),
                "save" => RedirectToAction("Index", "Workbooks"),
                _ => RedirectToAction("Index", "Workbooks"),
            };
        }

        // ===== Helpers =====
        private async Task<WorkbookSubmission?> LoadScopedAsync(int id, bool track = false)
        {
            var me = await _users.GetUserAsync(User);
            if (me is null) return null;

            var isSuper = await _users.IsInRoleAsync(me, "SuperAdmin");

            IQueryable<WorkbookSubmission> q = track
                ? _db.WorkbookSubmissions
                : _db.WorkbookSubmissions.AsNoTracking();

            var wb = await q.FirstOrDefaultAsync(x => x.Id == id && x.WorkbookType == WorkbookType.Workbook2);
            if (wb is null) return null;

            if (!isSuper)
            {
                if (me.CompanyId is null || wb.CompanyId != me.CompanyId) return null;
            }
            return wb;
        }

        private static (int criteria, int applicable, int comp, int ncomp, double compPct, double ncompPct)
    Calc(List<GelRow>? rows)
        {
            if (rows == null || rows.Count == 0) return (0, 0, 0, 0, 0d, 0d);

            int criteria = rows.Count;
            int comp = rows.Count(r => r.CI == 3);
            int ncomp = rows.Count(r => r.CI == 2);
            int applicable = comp + ncomp; // exclude CI==1 "Not Applicable"

            double compPct = applicable > 0 ? (double)comp / applicable : 0d; // 0..1
            double ncompPct = applicable > 0 ? (double)ncomp / applicable : 0d; // 0..1
            return (criteria, applicable, comp, ncomp, compPct, ncompPct);
        }


        private static QaData ParseData(WorkbookSubmission wb)
        {
            if (string.IsNullOrWhiteSpace(wb.Data))
                return QaData.CreateDefault();

            try { return JsonSerializer.Deserialize<QaData>(wb.Data) ?? QaData.CreateDefault(); }
            catch { return QaData.CreateDefault(); }
        }

        private async Task<IActionResult> NavOnlyAsync(
            int id, string? nav, string prevAction, string nextAction = "",
            (string action, string controller)? finishAction = null)
        {
            var wb = await LoadScopedAsync(id, track: true);
            if (wb is null) return NotFound();

            wb.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var n = (nav ?? "next").ToLowerInvariant();
            if (n == "prev") return RedirectToAction(prevAction, new { id });
            if (n == "save") return RedirectToAction("Index", "Workbooks");

            if (finishAction.HasValue) return RedirectToAction(finishAction.Value.action, finishAction.Value.controller);
            return RedirectToAction(nextAction, new { id });
        }

        // ---- Seeds ----

        private static bool EnsureGelSeed(QaGEL gel)
        {
            if (gel == null) return false;
            gel.Parts ??= new List<GelPart>();
            gel.Rows ??= new List<GelRow>();
            if (gel.Rows.Count > 0) return false;

            void Part(string code, string title, string desc) => gel.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action) => gel.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action });

            // GEL 1.1
            Part("GEL 1.1",
                "The governing body provides ethical and effective leadership, demonstrating accountability, responsibility, fairness, and transparency",
                "Assesses if the board operates with integrity, sets an ethical tone, makes informed decisions in the best interest of the institution, oversees strategy implementation, ensures adequate resource allocation, holds management accountable, and manages conflicts of interest effectively.");
            Row("GEL 1.1", "1.1.1", "Signed Code of Conduct/Ethics for Governing Body members");
            Row("GEL 1.1", "1.1.2", "Minutes of Governing Body meetings (reflecting strategic discussions, ethical considerations, oversight questions, decision-making processes)");
            Row("GEL 1.1", "1.1.3", "Governing Body Charter/Terms of Reference");
            Row("GEL 1.1", "1.1.4", "Conflict of Interest Policy & Register");
            Row("GEL 1.1", "1.1.5", "Annual Report/Integrated Report");

            // GEL 1.2
            Part("GEL 1.2",
                "Clear distinction and balance of roles and responsibilities between the governing body and executive management are defined and implemented.",
                "Examines if there's a formal delegation of authority, clarity on strategic oversight (Board) versus operational management (Executive), and processes ensuring the Board doesn't unduly interfere in operations but receives adequate information for oversight.");
            Row("GEL 1.2", "1.2.1", "Delegation of Authority Framework/Policy");
            Row("GEL 1.2", "1.2.2", "Terms of Reference for Board Committees & CEO/Principal");
            Row("GEL 1.2", "1.2.3", "Reporting structure documentation");
            Row("GEL 1.2", "1.2.4", "Relevant sections in Board meeting minutes showing appropriate reporting and delegation.");

            // GEL 1.3
            Part("GEL 1.3",
                "The governing body oversees and monitors the establishment and implementation of an effective risk management process and internal controls.",
                "Assesses if the Board understands key institutional risks, ensures a framework exists to identify, assess, mitigate, and monitor risks, receives regular risk reports, and oversees the adequacy of internal financial and operational controls.");
            Row("GEL 1.3", "1.3.1", "Risk Management Policy & Framework");
            Row("GEL 1.3", "1.3.2", "Strategic Risk Register");
            Row("GEL 1.3", "1.3.3", "Minutes of Governing Body/Audit & Risk Committee meetings discussing risk and controls");
            Row("GEL 1.3", "1.3.4", "Internal/External Audit reports on controls");
            Row("GEL 1.3", "1.3.5", "Management assurance reports on risk/controls.");

            // GEL 1.4
            Part("GEL 1.4",
                "The governing body ensures the institution promotes and maintains an ethical culture, supported by a code of conduct/ethics.",
                "Looks at how the Board champions ethical behaviour, ensures an institutional Code of Conduct exists and is communicated, monitors ethical climate, and ensures mechanisms exist for reporting unethical conduct (whistleblowing).");
            Row("GEL 1.4", "1.4.1", "Institutional Code of Conduct/Ethics Policy");
            Row("GEL 1.4", "1.4.2", "Communication records regarding the Code");
            Row("GEL 1.4", "1.4.3", "Staff/Learner survey results related to ethics");
            Row("GEL 1.4", "1.4.4", "Whistleblowing Policy & reports (anonymised summary)");
            Row("GEL 1.4", "1.4.5", "Board minutes discussing ethical culture.");

            // GEL 1.5
            Part("GEL 1.5",
                "Leadership demonstrates commitment to the QMS/EOMS, establishing a quality policy and objectives.",
                "Checks if top management actively participates in the QMS/EOMS, ensures its integration with strategy, sets measurable quality objectives, provides resources, and promotes a quality culture.");
            Row("GEL 1.5", "1.5.1", "Signed Quality Policy");
            Row("GEL 1.5", "1.5.2", "Documented Quality Objectives (SMART)");
            Row("GEL 1.5", "1.5.3", "Management Review minutes");
            Row("GEL 1.5", "1.5.4", "Communication records regarding quality policy/objectives");
            Row("GEL 1.5", "1.5.5", "Resource allocation evidence linked to quality initiatives.");

            // GEL 1.6
            Part("GEL 1.6",
                "The governing body ensures compliance with all applicable laws, regulations and non-binding rules, codes, and standards (including accreditation requirements).",
                "Assesses the process for identifying applicable legal/regulatory requirements, assigning responsibility for compliance, monitoring changes, receiving assurance on compliance status, and addressing non-compliance.");
            Row("GEL 1.6", "1.6.1", "Compliance Policy/Framework");
            Row("GEL 1.6", "1.6.2", "Legal/Regulatory Compliance Register");
            Row("GEL 1.6", "1.6.3", "Reports to the Governing Body/Committees on compliance status");
            Row("GEL 1.6", "1.6.4", "Accreditation certificates/letter");
            Row("GEL 1.6", "1.6.5", "External audit reports");
            Row("GEL 1.6", "1.6.6", "Internal compliance checklists/reports.");

            // GEL 1.7
            Part("GEL 1.7",
                "Processes are in place for regular governing body performance evaluation and development.",
                "Examines if the Board assesses its own effectiveness, identifies areas for improvement, and implements development activities.");
            Row("GEL 1.7", "1.7.1", "Policy/Procedure for Governing Body Evaluation");
            Row("GEL 1.7", "1.7.2", "Records of past evaluations (e.g., questionnaires, reports)");
            Row("GEL 1.7", "1.7.3", "Board development plan/records of training attended.");

            return true;
        }

        private static bool EnsureSprSeed(QaSPR spr)
        {
            if (spr == null) return false;
            spr.Parts ??= new List<GelPart>();
            spr.Rows ??= new List<GelRow>();
            if (spr.Rows.Count > 0) return false;

            void Part(string code, string title, string desc) => spr.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action) => spr.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action });

            // SPR 2.1
            Part("SPR 2.1",
                "A documented strategic plan exists, aligned with the institution's mission, vision, context, and stakeholder needs, incorporating clear objectives and performance indicators.",
                "Assesses the existence, relevance, and quality of the strategic plan. It should define the institution's direction, be based on analysis (SWOT/PESTLE), reflect stakeholder input, and include measurable goals (KPIs) to track progress.");
            Row("SPR 2.1", "2.1.1", "Documented Strategic Plan (current)");
            Row("SPR 2.1", "2.1.2", "Institutional Mission & Vision statements");
            Row("SPR 2.1", "2.1.3", "Environmental scanning documentation (SWOT/PESTLE analysis)");
            Row("SPR 2.1", "2.1.4", "Records of stakeholder consultations during planning");
            Row("SPR 2.1", "2.1.5", "List of strategic objectives with associated KPIs and targets.");

            // SPR 2.2
            Part("SPR 2.2",
                "Strategic planning considers the external environment, internal capabilities, interested party requirements, and identifies strategic risks and opportunities.",
                "");
            Row("SPR 2.2", "2.2.1", "Evidence within the Strategic Plan or supporting documents (market analysis, resource reviews, stakeholder needs, risk/opportunity summaries linked to strategy)");

            // SPR 2.3
            Part("SPR 2.3",
                "Risk management processes are embedded in strategic and operational planning to identify, assess, mitigate, monitor, and report on risks impacting institutional objectives.",
                "Looks beyond just having a risk register; assesses if risk thinking is integral to planning and decision-making at all levels. Are potential risks considered when setting objectives or planning activities? Are mitigation actions part of operational plans?");
            Row("SPR 2.3", "2.3.1", "Strategic Plan showing risk considerations");
            Row("SPR 2.3", "2.3.2", "Operational plans including risk mitigation actions");
            Row("SPR 2.3", "2.3.3", "Departmental risk registers");
            Row("SPR 2.3", "2.3.4", "Project management documentation showing risk assessment");
            Row("SPR 2.3", "2.3.5", "Management meeting minutes discussing operational risks");
            Row("SPR 2.3", "2.3.6", "Risk Management Policy & Procedures.");

            // SPR 2.4
            Part("SPR 2.4",
                "Resource allocation (financial, human, physical) is aligned with strategic objectives and planned activities.",
                "Assesses if budgeting and resource planning processes directly support the achievement of strategic goals. Are funds, staff time, and facilities prioritised for strategic initiatives?");
            Row("SPR 2.4", "2.4.1", "Annual Budget linked to Strategic Plan objectives");
            Row("SPR 2.4", "2.4.2", "Business cases for major projects/initiatives showing strategic alignment");
            Row("SPR 2.4", "2.4.3", "Staffing plans reflecting strategic priorities");
            Row("SPR 2.4", "2.4.4", "Capital expenditure plans linked to strategy");
            Row("SPR 2.4", "2.4.5", "Management reports showing resource allocation vs plan");

            // SPR 2.5
            Part("SPR 2.5",
                "Strategic objectives and plans are effectively communicated to relevant internal and external stakeholders.",
                "Examines how the strategy is shared with staff, learners, and other stakeholders to ensure understanding, buy-in, and alignment of effort.");
            Row("SPR 2.5", "2.5.1", "Internal communication plan/records (emails, newsletters, meeting presentations)");
            Row("SPR 2.5", "2.5.2", "Staff meeting minutes discussing strategy");
            Row("SPR 2.5", "2.5.3", "Public-facing documents summarising strategic direction (website section, annual report)");
            Row("SPR 2.5", "2.5.4", "Learner communication materials mentioning relevant goals.");

            // SPR 2.6
            Part("SPR 2.6",
                "Progress towards strategic objectives is regularly monitored, evaluated, and reported to the governing body and relevant stakeholders.",
                "Assesses the system for tracking performance against KPIs, analysing progress, identifying deviations, taking corrective action, and reporting findings upwards and outwards where appropriate.");
            Row("SPR 2.6", "2.6.1", "Performance dashboards/reports showing progress against KPIs");
            Row("SPR 2.6", "2.6.2", "Management meeting minutes discussing strategic performance");
            Row("SPR 2.6", "2.6.3", "Reports to the Governing Body on strategic progress");
            Row("SPR 2.6", "2.6.4", "Annual Performance Reviews/Reports");
            Row("SPR 2.6", "2.6.5", "Evidence of adjustments made based on performance monitoring");

            return true;
        }

        private static bool EnsureTlaSeed(QaTLA tla)
        {
            if (tla == null) return false;
            tla.Parts ??= new List<GelPart>();
            tla.Rows ??= new List<GelRow>();
            if (tla.Rows.Count > 0) return false;

            void Part(string code, string title, string desc) => tla.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action) => tla.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action });

            // TLA 3.1
            Part("TLA 3.1",
                "Programme design and curriculum development are aligned with the NQF, relevant occupational standards (QCTO), qualification requirements (CHE/Umalusi), stated learning outcomes, and stakeholder/industry needs",
                "Assesses the process for designing/reviewing programmes. Ensures content is current, meets regulatory body specs (SAQA ID, credits, level), leads to defined outcomes, and incorporates input from employers/industry advisory groups to ensure relevance.");
            Row("TLA 3.1", "3.1.1", "Documented Programme/Curriculum Design & Review Policy/Procedure");
            Row("TLA 3.1", "3.1.2", "Programme specifications/qualification documents detailing outcomes");
            Row("TLA 3.1", "3.1.3", "NQF level, credits, SAQA ID linkage; Curriculum documents (syllabi, module descriptors)");
            Row("TLA 3.1", "3.1.4", "Minutes of programme review meetings");
            Row("TLA 3.1", "3.1.5", "Records of industry/stakeholder consultation (advisory committee minutes, surveys)");
            Row("TLA 3.1", "3.1.6", "Approval letters from QCTO/CHE/Umalusi");

            // TLA 3.2
            Part("TLA 3.2",
                "Programmes promote student-centred learning, encouraging active engagement, critical thinking and the development of specified competencies.",
                "Examines the pedagogical approach embedded in the curriculum and teaching practices. Looks for evidence of activities beyond passive lectures, such as problem-based learning, case studies, group work, practical application, simulations, research tasks.");
            Row("TLA 3.2", "3.2.1", "Module guides/Lesson plans showing varied, active learning activities");
            Row("TLA 3.2", "3.2.2", "Examples of learner work demonstrating critical thinking/application (portfolios, projects)");
            Row("TLA 3.2", "3.2.3", "Learning platform (LMS) structure and activities (if applicable)");
            Row("TLA 3.2", "3.2.4", "Classroom observation reports");
            Row("TLA 3.2", "3.2.5", "Learner feedback on teaching methods");

            // TLA 3.3
            Part("TLA 3.3",
                "Teaching and facilitation methodologies are appropriate for the learning outcomes, learner profiles, NQF level, and mode of delivery (face-to-face, online, blended).",
                "Assesses the appropriateness and variety of teaching methods used. Considers if methods suit the subject matter, learner cohort, complexity (NQF level), and delivery channel (online pedagogy differs from face-to-face).");
            Row("TLA 3.3", "3.3.1", "Lesson plans/Module descriptors outlining teaching strategies");
            Row("TLA 3.3", "3.3.2", "Staff development records related to pedagogy/online teaching");
            Row("TLA 3.3", "3.3.3", "Examples of teaching materials (presentations, videos, interactive exercises)");
            Row("TLA 3.3", "3.3.4", "Classroom observation reports");
            Row("TLA 3.3", "3.3.5", "Learner feedback specifically on teaching effectiveness and appropriateness");

            // TLA 3.4
            Part("TLA 3.4",
                "Assessment policies and procedures ensure assessments are fair, valid, reliable, sufficient, authentic, transparent, and consistently applied across all learners and sites.",
                "Examines the robustness of the assessment system: Fair, Valid, Reliable, Sufficient, Authentic, Transparent. Requires clear policies, well-designed tasks, marking guides, security, etc.");
            Row("TLA 3.4", "3.4.1", "Documented Assessment Policy & Procedures (including plagiarism, appeals, reasonable adjustments)");
            Row("TLA 3.4", "3.4.2", "Sample assessment tasks (assignments, exams, practicals) clearly linked to learning outcomes");
            Row("TLA 3.4", "3.4.3", "Marking criteria/rubrics/memoranda");
            Row("TLA 3.4", "3.4.4", "Evidence of assessment validation/review process");
            Row("TLA 3.4", "3.4.5", "Secure storage procedures for assessments");
            Row("TLA 3.4", "3.4.6", "Policy on Assessment Appeals");

            // TLA 3.5
            Part("TLA 3.5",
                "A robust system for internal and external moderation of assessments is implemented to ensure consistency, fairness, and maintenance of standards.",
                "Assesses the quality assurance applied to marking/grading. Internal moderation occurs before results are finalised. External moderation is by external peers/regulators (SETA/QCTO/CHE).");
            Row("TLA 3.5", "3.5.1", "Moderation Policy & Procedures (Internal & External)");
            Row("TLA 3.5", "3.5.2", "Internal moderation reports/checklists");
            Row("TLA 3.5", "3.5.3", "Samples of moderated learner work");
            Row("TLA 3.5", "3.5.4", "External Moderation reports from SETA/QCTO/CHE or appointed moderators");
            Row("TLA 3.5", "3.5.5", "Minutes of assessment/moderation meetings");
            Row("TLA 3.5", "3.5.6", "Evidence of actions taken based on moderation findings");

            // TLA 3.6
            Part("TLA 3.6",
                "Timely, constructive feedback is provided to learners on their assessments to support learning and development.",
                "Examines the quality and timeliness of feedback. It should explain strengths/weaknesses and suggest improvements; turnaround times should be clear.");
            Row("TLA 3.6", "3.6.1", "Policy/guidelines on providing assessment feedback (including turnaround times)");
            Row("TLA 3.6", "3.6.2", "Sample marked learner assessments showing constructive comments");
            Row("TLA 3.6", "3.6.3", "Learner feedback surveys asking about quality/timeliness of assessment feedback");
            Row("TLA 3.6", "3.6.4", "Evidence of feedback provided through LMS (if applicable)");

            // TLA 3.7
            Part("TLA 3.7",
                "Mechanisms are in place for the regular review and improvement of programme design, curriculum, teaching methods, and assessment practices, incorporating learner and stakeholder feedback.",
                "Assesses the cyclical nature of QA for programmes: formal review processes analysing data/feedback and resulting in action plans.");
            Row("TLA 3.7", "3.7.1", "Programme Review Policy/Procedure");
            Row("TLA 3.7", "3.7.2", "Annual/Periodic Programme Review Reports");
            Row("TLA 3.7", "3.7.3", "Analysis of learner feedback data (module surveys, focus groups)");
            Row("TLA 3.7", "3.7.4", "Analysis of stakeholder feedback (employer surveys, advisory boards)");
            Row("TLA 3.7", "3.7.5", "Minutes of meetings where programme improvements are discussed/approved");
            Row("TLA 3.7", "3.7.6", "Action plans resulting from reviews & evidence of implementation");

            // TLA 3.8
            Part("TLA 3.8",
                "Where applicable, work-integrated learning (WIL) or workplace experience components are effectively managed, monitored, and integrated with theoretical learning, involving relevant workplace partners",
                "Applicable for programmes with practical/workplace elements: planning, placements, monitoring, assessment in the workplace, communication with mentors, linking theory to practice.");
            Row("TLA 3.8", "3.8.1", "WIL Policy & Procedures");
            Row("TLA 3.8", "3.8.2", "Templates for workplace agreements/MOUs");
            Row("TLA 3.8", "3.8.3", "Learner logbooks/portfolios for workplace activities");
            Row("TLA 3.8", "3.8.4", "Workplace assessment tools/criteria");
            Row("TLA 3.8", "3.8.5", "Records of communication/visits with workplace partners");
            Row("TLA 3.8", "3.8.6", "Learner/Employer feedback on WIL component");
            Row("TLA 3.8", "3.8.7", "QCTO requirements for workplace component");

            return true;
        }
        private static bool EnsureLswSeed(QaLSW lsw)
        {
            if (lsw == null) return false;
            lsw.Parts ??= new List<GelPart>();
            lsw.Rows ??= new List<GelRow>();
            if (lsw.Rows.Count > 0) return false; // already seeded

            void Part(string code, string title, string desc) => lsw.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action) => lsw.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action });

            // LSW 4.1
            Part("LSW 4.1",
                "Admission policies and procedures are clear, fair, transparent, consistently applied, and compliant with regulatory requirements, including criteria for Recognition of Prior Learning (RPL) where applicable",
                "Assesses the entire process of learner entry: documented, non-discriminatory, consistently applied; includes RPL handling against programme requirements.");
            Row("LSW 4.1", "4.1.1", "Documented Admission Policy & Procedures");
            Row("LSW 4.1", "4.1.2", "Documented RPL Policy & Procedures");
            Row("LSW 4.1", "4.1.3", "Programme admission criteria");
            Row("LSW 4.1", "4.1.4", "Application forms");
            Row("LSW 4.1", "4.1.5", "Records demonstrating consistent application of criteria");
            Row("LSW 4.1", "4.1.6", "Sample RPL assessment records");
            Row("LSW 4.1", "4.1.7", "Communication templates for applicants (acceptance, rejection, RPL outcome)");

            // LSW 4.2
            Part("LSW 4.2",
                "Accurate and accessible information is provided to prospective learners regarding programmes, admission requirements, fees, and support services",
                "Quality and accessibility of pre-enrolment information, especially costs and entry rules; clarity on available support.");
            Row("LSW 4.2", "4.2.1", "Website content");
            Row("LSW 4.2", "4.2.2", "Prospectus/brochures & Fees Schedule");
            Row("LSW 4.2", "4.2.3", "Programme information sheets");
            Row("LSW 4.2", "4.2.4", "Pre-enrolment advisory service records (if offered)");
            Row("LSW 4.2", "4.2.5", "Open day materials");

            // LSW 4.3
            Part("LSW 4.3",
                "Learner induction/orientation programmes effectively integrate new learners into the institutional environment and academic culture",
                "Covers essential info (policies, IT, support, academic expectations), timing and engagement.");
            Row("LSW 4.3", "4.3.1", "Induction/Orientation programme schedule and materials");
            Row("LSW 4.3", "4.3.2", "Learner feedback on induction");
            Row("LSW 4.3", "4.3.3", "Attendance records for induction events");
            Row("LSW 4.3", "4.3.4", "Online induction module content (if applicable)");

            // LSW 4.4
            Part("LSW 4.4",
                "Adequate, accessible, and effective academic support services (e.g., tutoring, library services, IT support, language support) are available to learners",
                "Range, accessibility, quality and effectiveness of academic support; resourcing and promotion to learners.");
            Row("LSW 4.4", "4.4.1", "Service descriptions and operating hours for library");
            Row("LSW 4.4", "4.4.2", "IT support, academic support centre");
            Row("LSW 4.4", "4.4.3", "Usage statistics for support services");
            Row("LSW 4.4", "4.4.4", "Learner feedback on support services");
            Row("LSW 4.4", "4.4.5", "Staffing information for support services");
            Row("LSW 4.4", "4.4.6", "Policy on Academic Support");

            // LSW 4.5
            Part("LSW 4.5",
                "Appropriate psycho-social support services (e.g., counselling, career guidance, health services, support for learners with disabilities) are available and promoted",
                "Non-academic support: confidentiality, referral pathways, qualified staff, accessibility (incl. disabilities), awareness.");
            Row("LSW 4.5", "4.5.1", "Policy on Learner Support/Wellbeing");
            Row("LSW 4.5", "4.5.2", "Policy/Procedure for Supporting Learners with Disabilities");
            Row("LSW 4.5", "4.5.3", "Information brochures/webpages (counselling, health, careers)");
            Row("LSW 4.5", "4.5.4", "Records of awareness campaigns/workshops");
            Row("LSW 4.5", "4.5.5", "Confidential usage statistics (where appropriate)");
            Row("LSW 4.5", "4.5.6", "Staff qualifications for support roles");
            Row("LSW 4.5", "4.5.7", "Referral protocols");

            // LSW 4.6
            Part("LSW 4.6",
                "Systems are in place to monitor learner progress, identify at-risk learners, and provide timely interventions",
                "Proactive measures: tracking, identification, interventions, timeliness, records.");
            Row("LSW 4.6", "4.6.1", "LMS data/reports on attendance/progress");
            Row("LSW 4.6", "4.6.2", "Policy/Procedure for identifying and supporting at-risk learners");
            Row("LSW 4.6", "4.6.3", "Records of interventions/support plans (anonymised)");
            Row("LSW 4.6", "4.6.4", "Reports on retention/progression/completion rates");
            Row("LSW 4.6", "4.6.5", "Minutes of student progress review meetings");

            // LSW 4.7
            Part("LSW 4.7",
                "Learner records are managed accurately, securely, confidentially (in line with POPIA), and systematically throughout the learner lifecycle, including certification upon successful completion",
                "Integrity and security of data: capture, storage, access control, archiving, POPIA, certification accuracy/timeliness.");
            Row("LSW 4.7", "4.7.1", "Learner Records Management Policy/Procedure");
            Row("LSW 4.7", "4.7.2", "POPIA Compliance Policy relating to learner data");
            Row("LSW 4.7", "4.7.3", "Access control logs/permissions for learner database/LMS");
            Row("LSW 4.7", "4.7.4", "Sample learner record showing accuracy/completeness");
            Row("LSW 4.7", "4.7.5", "Data backup and security procedures documentation");
            Row("LSW 4.7", "4.7.6", "Certificate issuance procedure and sample certificate");
            Row("LSW 4.7", "4.7.7", "Training records for staff on POPIA/data handling");

            // LSW 4.8
            Part("LSW 4.8",
                "Fair and transparent policies and procedures exist for handling learner complaints and appeals",
                "Clear steps, timelines, impartiality, record-keeping and communication of outcomes; information provided to learners.");
            Row("LSW 4.8", "4.8.1", "Learner Complaints Policy & Procedure");
            Row("LSW 4.8", "4.8.2", "Learner Academic Appeals Policy & Procedure");
            Row("LSW 4.8", "4.8.3", "Standard forms for complaints/appeals");
            Row("LSW 4.8", "4.8.4", "Register/log of complaints/appeals and outcomes (anonymised)");
            Row("LSW 4.8", "4.8.5", "Communication templates for complainants/appellants");
            Row("LSW 4.8", "4.8.6", "Information provided to learners about these procedures");

            return true; // seeded now (47 rows total)
        }

        private static bool EnsureSceSeed(QaSCE sce)
        {
            if (sce == null) return false;
            sce.Parts ??= new List<GelPart>();
            sce.Rows ??= new List<GelRow>();
            if (sce.Rows.Count > 0) return false; // already seeded

            void Part(string code, string title, string desc) => sce.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action) => sce.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action });

            // SCE 5.1
            Part("SCE 5.1",
                "Staff (academic, administrative, support) possess appropriate qualifications, expertise, experience, and (where required) professional registration/accreditation for their roles",
                "Assesses if staff meet the required standards for their jobs, both formal qualifications and practical experience/skills. For academic staff, this includes subject matter and potentially pedagogical expertise. For assessors/moderators, requires relevant SETA/QCTO registration.");
            Row("SCE 5.1", "5.1.1", "Staff files containing: CVs, certified copies of qualifications (SAQA verification for foreign qualifications)");
            Row("SCE 5.1", "5.1.2", "Professional registration certificates (e.g. SACE, HPCSA), SETA/QCTO Assessor/Moderator registration details");
            Row("SCE 5.1", "5.1.3", "Job descriptions outlining required qualifications/experience");

            // SCE 5.2
            Part("SCE 5.2",
                "Recruitment, selection, and induction processes are fair, transparent, and effective in appointing competent staff aligned with institutional values",
                "Examines how staff are hired and onboarded: equal opportunity, clear job descriptions/adverts, structured interviews/criteria, background checks (where appropriate), and comprehensive induction covering policies, procedures, systems, and culture.");
            Row("SCE 5.2", "5.2.1", "Recruitment & Selection Policy/Procedure");
            Row("SCE 5.2", "5.2.2", "Sample job adverts & job descriptions");
            Row("SCE 5.2", "5.2.3", "Standard interview questions/scoring sheets");
            Row("SCE 5.2", "5.2.4", "Records of selection panel composition");
            Row("SCE 5.2", "5.2.5", "Induction programme materials/checklist");
            Row("SCE 5.2", "5.2.6", "New staff feedback on induction process");
            Row("SCE 5.2", "5.2.7", "Equal Opportunities/Employment Equity Policy & reports (if applicable)");

            // SCE 5.3
            Part("SCE 5.3",
                "A systematic approach to performance management is implemented, including regular feedback, clear expectations, and alignment with institutional objectives",
                "Assesses how staff performance is managed and developed: documented processes, goal setting linked to institutional/departmental objectives, regular feedback, appraisal documentation, and links to development/recognition.");
            Row("SCE 5.3", "5.3.1", "Performance Management Policy/Procedure");
            Row("SCE 5.3", "5.3.2", "Performance appraisal forms/templates");
            Row("SCE 5.3", "5.3.3", "Records of completed appraisals (sample, anonymised if needed)");
            Row("SCE 5.3", "5.3.4", "Staff handbook outlining performance expectations");
            Row("SCE 5.3", "5.3.5", "Evidence of goal-setting processes");
            Row("SCE 5.3", "5.3.6", "Training materials for managers on appraisals/feedback");

            // SCE 5.4
            Part("SCE 5.4",
                "Continuous professional development (CPD) opportunities are identified, planned, supported, and evaluated to enhance staff competence, including pedagogical skills for academic staff",
                "Examines commitment to staff growth: needs identification (e.g., performance reviews, strategy), plan/budget, access/support, diverse CPD (courses, workshops, mentoring, conferences), and impact evaluation.");
            Row("SCE 5.4", "5.4.1", "Staff Development Policy/Procedure");
            Row("SCE 5.4", "5.4.2", "Training Needs Analysis (TNA) records/summaries");
            Row("SCE 5.4", "5.4.3", "Annual Staff Development Plan & Budget");
            Row("SCE 5.4", "5.4.4", "Records of internal/external training attended by staff");
            Row("SCE 5.4", "5.4.5", "CPD evaluation forms/reports");
            Row("SCE 5.4", "5.4.6", "Performance appraisal records showing development plan discussion");
            Row("SCE 5.4", "5.4.7", "Evidence of pedagogical training for academic staff");
            Row("SCE 5.4", "5.4.8", "Mentoring programme documentation (if applicable)");

            // SCE 5.5
            Part("SCE 5.5",
                "Staff are empowered, recognised, and rewarded for their contributions, fostering a positive organisational culture and high levels of engagement",
                "Assesses efforts to motivate and retain staff: delegation, involvement in decision-making, recognition schemes, fair remuneration/benefits, and initiatives promoting a supportive work environment.");
            Row("SCE 5.5", "5.5.1", "Staff survey results (engagement, satisfaction)");
            Row("SCE 5.5", "5.5.2", "Employee Value Proposition documentation");
            Row("SCE 5.5", "5.5.3", "Remuneration & Benefits policy/structure");
            Row("SCE 5.5", "5.5.4", "Records of staff recognition programmes/awards");
            Row("SCE 5.5", "5.5.5", "Minutes of staff meetings showing participation/input");
            Row("SCE 5.5", "5.5.6", "Examples of delegated authority");
            Row("SCE 5.5", "5.5.7", "Communication strategy regarding staff contributions");
            Row("SCE 5.5", "5.5.8", "Exit interview analysis (anonymised themes)");

            // SCE 5.6
            Part("SCE 5.6",
                "Sufficient numbers of appropriately qualified staff are appointed to support effective programme delivery, administration, and learner support services",
                "Assesses workload management and resourcing: staff-to-learner ratios (especially academic), administrative support capacity, specialist support availability, and processes for determining staffing needs.");
            Row("SCE 5.6", "5.6.1", "Staff organogram; staffing establishment data (headcount vs budgeted positions)");
            Row("SCE 5.6", "5.6.2", "Staff workload models/policies (if available)");
            Row("SCE 5.6", "5.6.3", "Analysis of staff-learner ratios per programme");
            Row("SCE 5.6", "5.6.4", "User feedback on adequacy of support staff (learner/academic staff surveys)");
            Row("SCE 5.6", "5.6.5", "Reports on turnaround times for administrative processes");
            Row("SCE 5.6", "5.6.6", "Relevant QCTO/CHE/Umalusi criteria on staffing levels");

            // SCE 5.7
            Part("SCE 5.7",
                "Effective internal communication mechanisms ensure staff are informed about institutional strategy, policies, and performance",
                "Assesses internal information flow: channels (meetings, emails, intranet, newsletters), clarity and timeliness, opportunities for two-way communication, and ensuring staff understand key directions and policy updates.");
            Row("SCE 5.7", "5.7.1", "Internal Communication Strategy/Policy");
            Row("SCE 5.7", "5.7.2", "Samples of internal communications (newsletters, emails, intranet posts)");
            Row("SCE 5.7", "5.7.3", "Minutes of all-staff or departmental meetings");
            Row("SCE 5.7", "5.7.4", "Staff feedback survey results related to communication effectiveness");
            Row("SCE 5.7", "5.7.5", "Organisation chart showing reporting lines");

            return true; // 43 criteria seeded
        }
        private static bool EnsureRleSeed(QaRLE rle)
        {
            if (rle == null) return false;
            rle.Parts ??= new List<GelPart>();
            rle.Rows ??= new List<GelRow>();
            if (rle.Rows.Count > 0) return false; // already seeded

            void Part(string code, string title, string desc) => rle.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action, string? requirement = null)
                => rle.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action, Requirement = requirement });

            // RLE 6.1 — Financial resources & controls (8)
            Part("RLE 6.1",
                "Financial resources are sufficient for institutional sustainability and are managed effectively, ethically, and transparently, with appropriate budgeting and financial controls",
                "Assesses financial health and management practices: planning/budgeting, adequacy of funding, monitoring & reporting, internal controls, ethical handling, adherence to standards, and audits.");
            Row("RLE 6.1", "6.1.1", "Audited Financial Statements (Annual)");
            Row("RLE 6.1", "6.1.2", "Management Accounts (monthly/quarterly)");
            Row("RLE 6.1", "6.1.3", "Annual Budget and budget monitoring reports");
            Row("RLE 6.1", "6.1.4", "Financial Policies & Procedures (procurement, payments, asset management)");
            Row("RLE 6.1", "6.1.5", "Internal control documentation");
            Row("RLE 6.1", "6.1.6", "External Audit Management Letter & responses");
            Row("RLE 6.1", "6.1.7", "Minutes of Finance Committee/Governing Body meetings discussing finances");
            Row("RLE 6.1", "6.1.8", "Evidence of financial sustainability planning (reserves policy, forecasts)");

            // RLE 6.2 — Physical learning environment & OHSA (9)
            Part("RLE 6.2",
                "The physical learning environment (classrooms, workshops, labs, common areas) is safe, accessible, conducive to learning, adequately equipped, and compliant with OHSA regulations",
                "Suitability for teaching, safety features, accessibility, cleanliness, upkeep.");
            Row("RLE 6.2", "6.2.1", "Site inspection reports/checklists");
            Row("RLE 6.2", "6.2.2", "OHSA Compliance Certificate/Audit Report (if available)");
            Row("RLE 6.2", "6.2.3", "Documented emergency evacuation procedures & drill records");
            Row("RLE 6.2", "6.2.4", "Fire equipment service records");
            Row("RLE 6.2", "6.2.5", "Maintenance logs/schedule for buildings");
            Row("RLE 6.2", "6.2.6", "Photos/videos of facilities");
            Row("RLE 6.2", "6.2.7", "Accessibility audit (if conducted)");
            Row("RLE 6.2", "6.2.8", "Learner/Staff feedback surveys on facilities");
            Row("RLE 6.2", "6.2.9", "Timetables showing room utilisation");

            // RLE 6.3 — Learning resources (8)
            Part("RLE 6.3",
                "Sufficient and relevant learning resources (e.g., library services, databases, textbooks, journals, equipment, software) are available, accessible, maintained, and regularly updated",
                "Adequacy and currency of physical/online resources, equipment/software & consumables; accessibility and maintenance.");
            Row("RLE 6.3", "6.3.1", "Library catalogue & usage statistics");
            Row("RLE 6.3", "6.3.2", "E-resource subscription list & usage statistics");
            Row("RLE 6.3", "6.3.3", "Inventory lists for equipment/software per programme");
            Row("RLE 6.3", "6.3.4", "Resource acquisition policy/procedures");
            Row("RLE 6.3", "6.3.5", "Budget allocation for learning resources");
            Row("RLE 6.3", "6.3.6", "Maintenance logs for equipment");
            Row("RLE 6.3", "6.3.7", "Learner/Staff feedback on resource adequacy and accessibility");
            Row("RLE 6.3", "6.3.8", "Programme validation documents listing required resources");

            // RLE 6.4 — ICT infrastructure (9)
            Part("RLE 6.4",
                "Information and Communication Technology (ICT) infrastructure is adequate, reliable, secure, and effectively supports teaching, learning, assessment, administration, and communication",
                "Network reliability & coverage, devices/labs, LMS & admin systems, cybersecurity, backup/DR, and IT support.");
            Row("RLE 6.4", "6.4.1", "ICT Strategy/Policy");
            Row("RLE 6.4", "6.4.2", "Network diagrams & specifications");
            Row("RLE 6.4", "6.4.3", "Wi-Fi coverage maps/reports");
            Row("RLE 6.4", "6.4.4", "LMS platform details & usage reports");
            Row("RLE 6.4", "6.4.5", "Inventory of computer hardware/software");
            Row("RLE 6.4", "6.4.6", "Cybersecurity policy & procedures (firewalls, anti-virus, access control)");
            Row("RLE 6.4", "6.4.7", "Data backup & disaster recovery plans/test results");
            Row("RLE 6.4", "6.4.8", "IT support helpdesk statistics (response times, issue resolution)");
            Row("RLE 6.4", "6.4.9", "User feedback (staff/learners) on ICT services");

            // RLE 6.5 — Planned maintenance & upgrades (6)
            Part("RLE 6.5",
                "Processes are in place for the planned maintenance and upgrading of physical infrastructure, equipment, and learning resources",
                "Proactive asset management: scheduled maintenance, replacement cycles, budgets, fault reporting, SLAs.");
            Row("RLE 6.5", "6.5.1", "Asset Management Policy/Register");
            Row("RLE 6.5", "6.5.2", "Planned maintenance schedules (buildings, equipment)");
            Row("RLE 6.5", "6.5.3", "IT hardware/software replacement plan");
            Row("RLE 6.5", "6.5.4", "Budget allocation for maintenance & capital replacement");
            Row("RLE 6.5", "6.5.5", "Records of fault reporting and resolution");
            Row("RLE 6.5", "6.5.6", "Service Level Agreements (SLAs) with maintenance providers");

            return true; // 40 criteria seeded
        }

        private static bool EnsureQmiSeed(QaQMI qmi)
        {
            if (qmi == null) return false;
            qmi.Parts ??= new List<GelPart>();
            qmi.Rows ??= new List<GelRow>();
            if (qmi.Rows.Count > 0) return false; // already seeded

            void Part(string code, string title, string desc) => qmi.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action, string? requirement = null)
                => qmi.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action, Requirement = requirement });

            // QMI 7.1 — QMS/EOMS implemented (6)
            Part("QMI 7.1",
                "A documented Quality Management System (QMS) / Educational Organisation Management System (EOMS), aligned with ISO 21001 principles and regulatory requirements, is effectively implemented and maintained.",
                "Looks for a formal system (manual, policies, procedures), alignment with ISO 21001, integration across the institution and evidence of use.");
            Row("QMI 7.1", "7.1.1", "Quality Manual / EOMS Documentation");
            Row("QMI 7.1", "7.1.2", "Documented Quality Policy & Objectives");
            Row("QMI 7.1", "7.1.3", "Key quality-related policies and procedures");
            Row("QMI 7.1", "7.1.4", "Organisation chart showing quality responsibilities");
            Row("QMI 7.1", "7.1.5", "Records of QMS/EOMS training for staff");
            Row("QMI 7.1", "7.1.6", "Evidence of QMS processes being followed across departments");

            // QMI 7.2 — Monitoring & evaluation system (6)
            Part("QMI 7.2",
                "Regular monitoring and evaluation of educational provision, support services, and organisational processes are conducted using diverse data sources.",
                "Systematic gathering and analysis of performance data (pass rates, retention, satisfaction, complaints, etc.) from multiple methods.");
            Row("QMI 7.2", "7.2.1", "Monitoring & Evaluation Framework/Policy");
            Row("QMI 7.2", "7.2.2", "Schedule of monitoring activities");
            Row("QMI 7.2", "7.2.3", "Examples of data collection tools (surveys, interview guides)");
            Row("QMI 7.2", "7.2.4", "Analysis reports (learner feedback, assessment results, retention/completion, staff & stakeholder input)");
            Row("QMI 7.2", "7.2.5", "Programme review reports incorporating monitoring data");
            Row("QMI 7.2", "7.2.6", "Minutes where monitoring data is discussed");

            // QMI 7.3 — Feedback management (5)
            Part("QMI 7.3",
                "Learner satisfaction and other stakeholder feedback are systematically collected, analysed, and used to inform quality improvements.",
                "Regular surveys/focus groups; analysis and clear evidence of changes made from feedback.");
            Row("QMI 7.3", "7.3.1", "Feedback collection policy/schedule");
            Row("QMI 7.3", "7.3.2", "Standard feedback questionnaires/survey instruments");
            Row("QMI 7.3", "7.3.3", "Reports analysing feedback results (quantitative & qualitative)");
            Row("QMI 7.3", "7.3.4", "Examples of improvements implemented from feedback");
            Row("QMI 7.3", "7.3.5", "‘You said, we did’ communications");

            // QMI 7.4 — Internal audit (5)
            Part("QMI 7.4",
                "Internal audit processes are conducted periodically to verify conformance to the QMS/EOMS requirements and planned arrangements.",
                "Planned audits by trained auditors using checklists; reports and follow-up of findings.");
            Row("QMI 7.4", "7.4.1", "Internal Audit Policy/Procedure; Annual Internal Audit Plan/Schedule");
            Row("QMI 7.4", "7.4.2", "Internal Auditor training records");
            Row("QMI 7.4", "7.4.3", "Internal Audit Checklists/Work Papers");
            Row("QMI 7.4", "7.4.4", "Completed Internal Audit Reports");
            Row("QMI 7.4", "7.4.5", "Records of corrective actions & verification of effectiveness");

            // QMI 7.5 — Management review (4)
            Part("QMI 7.5",
                "Formal management reviews of the QMS/EOMS are conducted regularly by top management to ensure suitability, adequacy, effectiveness, and strategic alignment.",
                "Planned reviews with defined agenda, decisions, actions and follow-up.");
            Row("QMI 7.5", "7.5.1", "Management Review Procedure");
            Row("QMI 7.5", "7.5.2", "Schedule for Management Reviews");
            Row("QMI 7.5", "7.5.3", "Agendas/Minutes showing required inputs, decisions and actions");
            Row("QMI 7.5", "7.5.4", "Action logs and evidence of follow-up");

            // QMI 7.6 — Non-conformities & corrective action (5)
            Part("QMI 7.6",
                "A systematic approach exists for identifying, analysing, and addressing non-conformities and implementing corrective actions to prevent recurrence.",
                "Logging, root-cause analysis, actions, and effectiveness verification.");
            Row("QMI 7.6", "7.6.1", "Non-conformity & Corrective Action Procedure");
            Row("QMI 7.6", "7.6.2", "Register/log of non-conformities and corrective actions");
            Row("QMI 7.6", "7.6.3", "Records of root cause analysis");
            Row("QMI 7.6", "7.6.4", "Evidence of implemented corrective actions");
            Row("QMI 7.6", "7.6.5", "Records verifying effectiveness (follow-up audits, monitoring)");

            // QMI 7.7 — Continuous improvement culture (6)
            Part("QMI 7.7",
                "A culture of continuous improvement is fostered, encouraging innovation and responsiveness to changing needs.",
                "Evidence that improvement is valued and acted upon, including innovation and benchmarking.");
            Row("QMI 7.7", "7.7.1", "Staff/learner suggestion schemes & actions taken");
            Row("QMI 7.7", "7.7.2", "Examples of innovations implemented");
            Row("QMI 7.7", "7.7.3", "Benchmarking activities and subsequent actions");
            Row("QMI 7.7", "7.7.4", "Agendas/minutes on external trends and proactive discussion");
            Row("QMI 7.7", "7.7.5", "Communication promoting improvement/innovation");
            Row("QMI 7.7", "7.7.6", "Staff survey results related to empowerment/improvement culture");

            return true; // 37 criteria
        }

        private static bool EnsureSecSeed(QaSEC sec)
        {
            if (sec == null) return false;
            sec.Parts ??= new List<GelPart>();
            sec.Rows ??= new List<GelRow>();
            if (sec.Rows.Count > 0) return false; // already seeded

            void Part(string code, string title, string desc) => sec.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action, string? requirement = null)
                => sec.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action, Requirement = requirement });

            // SEC 8.1 — Identify stakeholders & needs (5)
            Part("SEC 8.1",
                "Key stakeholders (learners, staff, alumni, employers, industry bodies, funders, community, regulators) are identified, and their needs and expectations considered in planning and operations",
                "Formal identification and processes (surveys/consultations/advisory boards) feed into strategic & operational planning.");
            Row("SEC 8.1", "8.1.1", "Stakeholder Analysis/Mapping document");
            Row("SEC 8.1", "8.1.2", "Records of stakeholder surveys, consultations, focus groups");
            Row("SEC 8.1", "8.1.3", "Minutes of Industry Advisory Board meetings");
            Row("SEC 8.1", "8.1.4", "Analysis reports summarising stakeholder needs/expectations");
            Row("SEC 8.1", "8.1.5", "Evidence in plans showing consideration of stakeholder input");

            // SEC 8.2 — Engagement mechanisms (4)
            Part("SEC 8.2",
                "Effective strategies and mechanisms are implemented for proactive, regular, and meaningful engagement with key stakeholders",
                "Two-way engagement suitable for each group (employers, alumni, learner forums, funders, community).");
            Row("SEC 8.2", "8.2.1", "Stakeholder Engagement Strategy/Plan");
            Row("SEC 8.2", "8.2.2", "Communication plan outlining activities per stakeholder group");
            Row("SEC 8.2", "8.2.3", "Records of engagement activities (invites/attendance, minutes, newsletters, web sections)");
            Row("SEC 8.2", "8.2.4", "Feedback mechanisms specifically for stakeholders");

            // SEC 8.3 — Published information quality (7)
            Part("SEC 8.3",
                "Information published about the institution and its programmes is accurate, objective, up-to-date, accessible, and sufficient for decisions",
                "Processes ensure factual correctness (accreditation, fees, outcomes), clarity, currency and accessibility.");
            Row("SEC 8.3", "8.3.1", "Website content review");
            Row("SEC 8.3", "8.3.2", "Prospectus/brochure review");
            Row("SEC 8.3", "8.3.3", "Procedure for updating public information");
            Row("SEC 8.3", "8.3.4", "Internal sign-off process for publications/website content");
            Row("SEC 8.3", "8.3.5", "Checks for consistency across platforms");
            Row("SEC 8.3", "8.3.6", "Accessibility compliance evidence for website (if applicable)");
            Row("SEC 8.3", "8.3.7", "Accreditation status displayed correctly as per rules");

            // SEC 8.4 — Partnerships & employability (7)
            Part("SEC 8.4",
                "Partnerships with employers, industry, and the community are actively sought and managed to enhance relevance, WIL and employability",
                "Proactive relationships and tracking of outcomes (relevance, WIL success, employment).");
            Row("SEC 8.4", "8.4.1", "Records of industry partnerships/collaborations (MOUs, agreements)");
            Row("SEC 8.4", "8.4.2", "Minutes of Industry Advisory Committees");
            Row("SEC 8.4", "8.4.3", "Guest lecturer register/records");
            Row("SEC 8.4", "8.4.4", "WIL placement records/database");
            Row("SEC 8.4", "8.4.5", "Graduate destination surveys/statistics");
            Row("SEC 8.4", "8.4.6", "Records of community engagement projects");
            Row("SEC 8.4", "8.4.7", "Policy on external partnerships");

            // SEC 8.5 — External communication & reputation (6)
            Part("SEC 8.5",
                "External communication strategies effectively manage the institution's reputation and relationship with the broader public and media",
                "Brand management, press/social media, and crisis communication planning.");
            Row("SEC 8.5", "8.5.1", "External Communication/Marketing Strategy");
            Row("SEC 8.5", "8.5.2", "Branding guidelines");
            Row("SEC 8.5", "8.5.3", "Sample press releases/media coverage");
            Row("SEC 8.5", "8.5.4", "Social media policy & platform management evidence");
            Row("SEC 8.5", "8.5.5", "Crisis Communication Plan");
            Row("SEC 8.5", "8.5.6", "Website ‘News’ section content");

            return true; // 29 criteria
        }
        private static bool EnsureLcrSeed(QaLCR lcr)
        {
            if (lcr == null) return false;
            lcr.Parts ??= new List<GelPart>();
            lcr.Rows ??= new List<GelRow>();
            if (lcr.Rows.Count > 0) return false; // already seeded

            void Part(string code, string title, string desc) => lcr.Parts.Add(new GelPart { PartCode = code, Title = title, Description = desc });
            void Row(string part, string code, string action, string? requirement = null)
                => lcr.Rows.Add(new GelRow { PartCode = part, Code = code, Action = action, Requirement = requirement });

            // LCR 9.1 — Legal compliance system (7)
            Part("LCR 9.1",
                "Systems are in place to identify, monitor, and ensure compliance with all relevant South African legislation and regulations (e.g., Higher Education Act, NQF Act, SDA, OHSA, POPIA, BCEA, LRA)",
                "Institutional approach to legal compliance: staying updated, implementing policies/procedures, training, monitoring, addressing non-compliance.");
            Row("LCR 9.1", "9.1.1", "Compliance Management Policy/Framework");
            Row("LCR 9.1", "9.1.2", "Legal Register identifying applicable legislation and compliance status");
            Row("LCR 9.1", "9.1.3", "Records of legal updates received/reviewed");
            Row("LCR 9.1", "9.1.4", "Policies and procedures aligned with key legislation (OHSA, POPIA, HR policies for BCEA/LRA)");
            Row("LCR 9.1", "9.1.5", "Staff training records on key compliance areas (e.g., POPIA, OHSA)");
            Row("LCR 9.1", "9.1.6", "Internal compliance checklists/audits");
            Row("LCR 9.1", "9.1.7", "Reports to management/governance on compliance matters");

            // LCR 9.2 — Accreditation compliance/reporting (5)
            Part("LCR 9.2",
                "Compliance with specific accreditation requirements and reporting timelines of relevant Quality Councils (QCTO, CHE, Umalusi) and SETAs is maintained",
                "Ongoing requirements beyond initial accreditation: reports, change notifications, standards, monitoring/re-accreditation.");
            Row("LCR 9.2", "9.2.1", "Current Accreditation Letters/Certificates from QCTO/CHE/Umalusi/SETAs");
            Row("LCR 9.2", "9.2.2", "Copies of annual reports/data submissions made to accrediting bodies");
            Row("LCR 9.2", "9.2.3", "Records of communication with accrediting bodies");
            Row("LCR 9.2", "9.2.4", "Evidence of participation in monitoring visits or re-accreditation processes");
            Row("LCR 9.2", "9.2.5", "Internal procedures for managing accreditation compliance/reporting");

            // LCR 9.3 — POPIA compliance (9)
            Part("LCR 9.3",
                "Policies and procedures related to information management comply with the Protection of Personal Information Act (POPIA)",
                "Information Officer, awareness, privacy notices, consent, DSARs, security, breach response, operator contracts.");
            Row("LCR 9.3", "9.3.1", "POPIA Compliance Policy");
            Row("LCR 9.3", "9.3.2", "Appointment letter for Information Officer");
            Row("LCR 9.3", "9.3.3", "Privacy Notices (staff, learners, website)");
            Row("LCR 9.3", "9.3.5", "Consent forms/records");
            Row("LCR 9.3", "9.3.6", "Procedure for handling Data Subject Access Requests & records of requests handled");
            Row("LCR 9.3", "9.3.7", "Staff training records on POPIA");
            Row("LCR 9.3", "9.3.8", "Information security policies/procedures");
            Row("LCR 9.3", "9.3.9", "Data breach incident response plan");
            Row("LCR 9.3", "9.3.10", "Contracts/DPAs with third-party operators");

            // LCR 9.4 — Statutory/regulatory reporting (4)
            Part("LCR 9.4",
                "Accurate and timely statutory and regulatory reporting is submitted as required (e.g., to DHET, DoEL, SARS, CIPC)",
                "Mandatory reports such as HETMIS, WSP/ATR, tax returns, and CIPC annual returns.");
            Row("LCR 9.4", "9.4.1", "Copies of key statutory reports submitted (e.g., WSP/ATR, HETMIS submission confirmation)");
            Row("LCR 9.4", "9.4.2", "SARS returns, CIPC annual return confirmation");
            Row("LCR 9.4", "9.4.3", "Internal calendar/checklist for statutory reporting deadlines");
            Row("LCR 9.4", "9.4.4", "Procedures for compiling and submitting statutory reports");

            // LCR 9.5 — Integrated reporting (4)
            Part("LCR 9.5",
                "Integrated reporting or similar reporting mechanisms are used to provide stakeholders with a holistic view of performance",
                "Connect strategy, governance, performance and outlook across multiple capitals.");
            Row("LCR 9.5", "9.5.1", "Annual Integrated Report (if produced)");
            Row("LCR 9.5", "9.5.2", "Annual Report covering governance, strategy, educational and financial performance, social impact, stakeholder relations");
            Row("LCR 9.5", "9.5.3", "Reporting framework used (e.g., IIRC Framework, GRI Standards)");
            Row("LCR 9.5", "9.5.4", "Evidence linking strategy, risks, performance and outlook in reporting");

            return true; // total 29 criteria
        }

    }
}
