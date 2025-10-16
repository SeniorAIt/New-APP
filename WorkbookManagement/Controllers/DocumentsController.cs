using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles; // FileExtensionContentTypeProvider
using Microsoft.Extensions.Logging;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Controllers
{
    [Authorize]
    public class DocumentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DocumentsController> _logger;

        // Whitelist + size limit + MIME mapper
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg", ".gif"
        };
        private const long MaxUploadBytes = 25L * 1024 * 1024; // 25 MB
        private readonly FileExtensionContentTypeProvider _mime = new();

        public DocumentsController(ApplicationDbContext db, IWebHostEnvironment env, ILogger<DocumentsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsSuperAdmin => User.IsInRole("SuperAdmin");

        private async Task<Guid?> CurrentUserCompanyIdAsync()
        {
            return await _db.Users
                .Where(u => u.Id == CurrentUserId)
                .Select(u => u.CompanyId)
                .FirstOrDefaultAsync();
        }

        // Safe path builders (single source of truth)
        private string GetCompanyFolder(Guid companyId)
            => Path.Combine(_env.WebRootPath, "uploads", "companies", companyId.ToString());

        private string GetAbsolutePath(Guid companyId, string storedFileName)
            => Path.Combine(GetCompanyFolder(companyId), storedFileName);

        // GET: /Documents
        // SuperAdmin with no companyId => grouped cards by company
        // SuperAdmin with companyId OR normal user => flat list (with filters)
        [HttpGet]
        public async Task<IActionResult> Index(
            Guid? companyId = null,
            string? q = null,
            DocumentKind? kind = null,
            DateTime? from = null,
            DateTime? to = null)
        {
            var vm = new DocumentsIndexVm
            {
                IsSuperAdmin = IsSuperAdmin,
                FilterCompanyId = companyId,
                Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
                Kind = kind,
                From = from,
                To = to
            };

            // SuperAdmin company dropdown options
            if (IsSuperAdmin)
            {
                vm.Companies = await _db.Companies
                    .OrderBy(c => c.Name)
                    .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
                    .ToListAsync();
            }

            IQueryable<CompanyDocument> baseQuery = _db.CompanyDocuments
                .Include(d => d.Company)
                .Include(d => d.UploadedByUser)
                .OrderByDescending(d => d.UploadedAtUtc);

            if (IsSuperAdmin)
            {
                if (companyId.HasValue)
                {
                    // Flat view scoped to selected company + filters
                    var qFlat = baseQuery.Where(d => d.CompanyId == companyId.Value);

                    if (!string.IsNullOrWhiteSpace(vm.Q))
                    {
                        var like = $"%{vm.Q}%";
                        qFlat = qFlat.Where(d =>
                            EF.Functions.Like(d.OriginalFileName!, like));
                    }

                    if (vm.Kind.HasValue)
                    {
                        qFlat = qFlat.Where(d => d.DocumentType == vm.Kind.Value);
                    }

                    if (vm.From.HasValue)
                    {
                        qFlat = qFlat.Where(d => d.UploadedAtUtc >= vm.From.Value);
                    }

                    if (vm.To.HasValue)
                    {
                        // inclusive end-of-day
                        var end = vm.To.Value.Date.AddDays(1);
                        qFlat = qFlat.Where(d => d.UploadedAtUtc < end);
                    }

                    vm.Flat = await qFlat.AsNoTracking().ToListAsync();
                }
                else
                {
                    // Grouped cards across all companies (no filters here for simplicity)
                    var all = await baseQuery.AsNoTracking().ToListAsync();
                    vm.Groups = all
                        .GroupBy(d => new { d.CompanyId, Name = d.Company!.Name })
                        .OrderBy(g => g.Key.Name)
                        .Select(g => new CompanyGroupVm
                        {
                            CompanyId = g.Key.CompanyId,
                            CompanyName = g.Key.Name,
                            Docs = g.ToList()
                        })
                        .ToList();
                }
            }
            else
            {
                // Company user: flat list of their own company docs + filters
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null) return Forbid();

                var qFlat = baseQuery.Where(d => d.CompanyId == cid.Value);

                if (!string.IsNullOrWhiteSpace(vm.Q))
                {
                    var like = $"%{vm.Q}%";
                    qFlat = qFlat.Where(d =>
                        EF.Functions.Like(d.OriginalFileName!, like));
                }

                if (vm.Kind.HasValue)
                {
                    qFlat = qFlat.Where(d => d.DocumentType == vm.Kind.Value);
                }

                if (vm.From.HasValue)
                {
                    qFlat = qFlat.Where(d => d.UploadedAtUtc >= vm.From.Value);
                }

                if (vm.To.HasValue)
                {
                    var end = vm.To.Value.Date.AddDays(1);
                    qFlat = qFlat.Where(d => d.UploadedAtUtc < end);
                }

                vm.Flat = await qFlat.AsNoTracking().ToListAsync();
            }

            return View(vm);
        }

        // POST: /Documents/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file, DocumentKind documentType = DocumentKind.Unknown, Guid? companyId = null)
        {
            // basic checks
            if (file == null || file.Length == 0)
            {
                TempData["err"] = "Please choose a file.";
                return RedirectToAction(nameof(Index), new { companyId });
            }

            if (file.Length > MaxUploadBytes)
            {
                TempData["err"] = $"File too large. Max allowed size is {MaxUploadBytes / (1024 * 1024)} MB.";
                return RedirectToAction(nameof(Index), new { companyId });
            }

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
            {
                TempData["err"] = $"File type not allowed. Allowed: {string.Join(", ", AllowedExtensions)}";
                return RedirectToAction(nameof(Index), new { companyId });
            }

            // Decide company (scope)
            Guid cid;
            if (IsSuperAdmin && companyId.HasValue)
            {
                cid = companyId.Value;
            }
            else
            {
                var myCid = await CurrentUserCompanyIdAsync();
                if (myCid == null)
                {
                    TempData["err"] = "Your user is not linked to a company.";
                    return RedirectToAction(nameof(Index));
                }
                cid = myCid.Value;
            }

            // Physical folder: /wwwroot/uploads/companies/{companyId}
            var absFolder = GetCompanyFolder(cid);
            Directory.CreateDirectory(absFolder);

            // Stored filename and absolute path
            var storedFileName = $"{Guid.NewGuid():N}{ext}";
            var absPath = GetAbsolutePath(cid, storedFileName);

            // Save file to disk
            using (var stream = System.IO.File.Create(absPath))
            {
                await file.CopyToAsync(stream);
            }

            // Resolve safe/authoritative content type (from extension)
            if (!_mime.TryGetContentType(file.FileName, out var resolvedContentType))
                resolvedContentType = "application/octet-stream";

            // Prepare DB record
            var doc = new CompanyDocument
            {
                CompanyId = cid,
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = storedFileName,
                DocumentType = documentType, // enum
                ContentType = resolvedContentType,
                SizeBytes = file.Length,
                UploadedByUserId = CurrentUserId,
                UploadedAtUtc = DateTime.UtcNow
            };

            // Try save DB; if it fails, remove the physical file to avoid orphans
            try
            {
                _db.CompanyDocuments.Add(doc);
                await _db.SaveChangesAsync();

                // Audit log
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var ua = Request.Headers.UserAgent.ToString();
                _logger.LogInformation(
                    "Upload OK: CompanyId={CompanyId}, UserId={UserId}, Original='{Original}', Stored='{Stored}', Size={Size}, ContentType='{ContentType}', Type={Type}, IP={IP}, UA={UA}",
                    cid, CurrentUserId, doc.OriginalFileName, doc.StoredFileName, doc.SizeBytes, doc.ContentType, doc.DocumentType, ip, ua
                );

                TempData["ok"] = "File uploaded.";
            }
            catch (Exception ex)
            {
                // Roll back file if DB save fails
                try { if (System.IO.File.Exists(absPath)) System.IO.File.Delete(absPath); }
                catch (Exception delEx)
                {
                    _logger.LogWarning(delEx, "Upload rollback: failed to delete file after DB error. CompanyId={CompanyId}, File={File}", cid, absPath);
                }

                _logger.LogError(ex,
                    "Upload FAILED (DB). Rolled back file. CompanyId={CompanyId}, UserId={UserId}, Original='{Original}', Stored='{Stored}'",
                    cid, CurrentUserId, doc.OriginalFileName, doc.StoredFileName
                );

                TempData["err"] = "Upload failed. Please try again.";
            }

            Guid? backCompanyId = IsSuperAdmin ? cid : (Guid?)null;
            return RedirectToAction(nameof(Index), new { companyId = backCompanyId });
        }

        // GET: /Documents/View/{id}
        [HttpGet]
        public async Task<IActionResult> View(Guid id)
        {
            var doc = await _db.CompanyDocuments
                .Include(d => d.Company)
                .FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return NotFound();

            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null || cid.Value != doc.CompanyId) return Forbid();
            }

            var absPath = GetAbsolutePath(doc.CompanyId, doc.StoredFileName);
            if (!System.IO.File.Exists(absPath)) return NotFound();

            // Defensive MIME (null-safe)
            string contentType;
            if (!string.IsNullOrWhiteSpace(doc.ContentType) &&
                !string.Equals(doc.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                contentType = doc.ContentType!;
            }
            else
            {
                if (!_mime.TryGetContentType(doc.OriginalFileName ?? string.Empty, out var mapped))
                    mapped = "application/octet-stream";
                contentType = mapped;
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{(doc.OriginalFileName ?? "document")}\"";

            var stream = System.IO.File.OpenRead(absPath);
            return File(stream, contentType, enableRangeProcessing: true);
        }

        // GET: /Documents/Download/{id}
        [HttpGet]
        public async Task<IActionResult> Download(Guid id)
        {
            var doc = await _db.CompanyDocuments
                .Include(d => d.Company)
                .FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return NotFound();

            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null || cid.Value != doc.CompanyId) return Forbid();
            }

            var absPath = GetAbsolutePath(doc.CompanyId, doc.StoredFileName);
            if (!System.IO.File.Exists(absPath)) return NotFound();

            string? stored = doc.ContentType;
            string contentType;
            if (!string.IsNullOrWhiteSpace(stored) &&
                !string.Equals(stored, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                contentType = stored!;
            }
            else
            {
                if (!_mime.TryGetContentType(doc.OriginalFileName ?? string.Empty, out var mapped))
                    mapped = "application/octet-stream";
                contentType = mapped;
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";

            return PhysicalFile(
                physicalPath: absPath,
                contentType: contentType,
                fileDownloadName: doc.OriginalFileName ?? "download",
                enableRangeProcessing: true
            );
        }

        // NEW: GET /Documents/Edit/{id}
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var doc = await _db.CompanyDocuments
                .Include(d => d.UploadedByUser)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null) return NotFound();

            // Only SuperAdmin OR same-company user can view
            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null || cid.Value != doc.CompanyId) return Forbid();
            }

            return View(doc);
        }

        // NEW: POST /Documents/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, DocumentKind documentType, string? notes)
        {
            var doc = await _db.CompanyDocuments.FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return NotFound();

            // Only SuperAdmin OR same-company user can edit
            if (!IsSuperAdmin)
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null || cid.Value != doc.CompanyId) return Forbid();
            }

            // update allowed fields
            doc.DocumentType = documentType;
            doc.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            await _db.SaveChangesAsync();

            TempData["ok"] = "Document updated.";
            Guid? backCompanyId = IsSuperAdmin ? doc.CompanyId : (Guid?)null;
            return RedirectToAction(nameof(Index), new { companyId = backCompanyId });
        }

        // POST: /Documents/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var doc = await _db.CompanyDocuments.FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return NotFound();

            var currentUserId = CurrentUserId;

            // Only SuperAdmin or the original uploader can delete
            var canDelete = IsSuperAdmin || string.Equals(doc.UploadedByUserId, currentUserId, StringComparison.Ordinal);
            if (!canDelete) return Forbid();

            var absPath = GetAbsolutePath(doc.CompanyId, doc.StoredFileName);

            _db.CompanyDocuments.Remove(doc);
            await _db.SaveChangesAsync();

            try
            {
                if (System.IO.File.Exists(absPath))
                {
                    System.IO.File.Delete(absPath);
                    TempData["ok"] = "Document deleted.";
                }
                else
                {
                    TempData["ok"] = "Document deleted.";
                    TempData["warn"] = "The file was not found on disk (it may have been removed previously).";
                    _logger.LogWarning("Delete: file missing on disk. CompanyId={CompanyId}, File={File}", doc.CompanyId, absPath);
                }
            }
            catch (Exception ex)
            {
                TempData["ok"] = "Document deleted.";
                TempData["warn"] = "The database entry was removed, but the file could not be deleted from disk. An administrator has been notified.";
                _logger.LogError(ex, "Delete: failed to remove file from disk. CompanyId={CompanyId}, File={File}", doc.CompanyId, absPath);
            }

            Guid? backCompanyId = IsSuperAdmin ? doc.CompanyId : (Guid?)null;
            return RedirectToAction(nameof(Index), new { companyId = backCompanyId });
        }
    }

    // -------- View models for the Documents index page --------

    public class DocumentsIndexVm
    {
        public bool IsSuperAdmin { get; set; }
        public Guid? FilterCompanyId { get; set; }

        // Filters (echo back into the form)
        public string? Q { get; set; }
        public DocumentKind? Kind { get; set; }  // enum filter
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        // Data
        public List<CompanyGroupVm> Groups { get; set; } = new();
        public List<CompanyDocument> Flat { get; set; } = new();

        // SuperAdmin dropdown options
        public List<CompanyOption> Companies { get; set; } = new();
    }

    public class CompanyGroupVm
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public List<CompanyDocument> Docs { get; set; } = new();
    }

    public class CompanyOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
