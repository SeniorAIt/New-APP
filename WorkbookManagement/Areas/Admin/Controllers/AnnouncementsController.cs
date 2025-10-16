using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "SuperAdmin")]
    public class AnnouncementsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AnnouncementsController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // ===== Attachment config =====
        private const long MaxAttachmentBytes = 50L * 1024 * 1024; // 50 MB
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx", ".pptx", ".png", ".jpg", ".jpeg"
        };
        private static readonly HashSet<string> InlineViewableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg"
        };

        private static string SanitizeFileName(string originalName, string fallbackExt = ".bin")
        {
            var name = Path.GetFileNameWithoutExtension(originalName);
            var ext = Path.GetExtension(originalName);
            if (string.IsNullOrWhiteSpace(ext)) ext = fallbackExt;
            name = Regex.Replace(name, @"[^\w\-.]+", "_"); // keep letters/digits/_/-/.
            return name + ext.ToLowerInvariant();
        }

        private static bool LooksAllowedFile(IFormFile f) =>
            AllowedExtensions.Contains(Path.GetExtension(f.FileName));

        private static string GetContentTypeFromExtension(string ext) => (ext ?? "").ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };

        private static string ToFsPathUnderWebRoot(string webRootPath, string relativeWebPath)
        {
            var trimmed = relativeWebPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(webRootPath, trimmed);
        }

        private string EnsureUploadsFolder()
        {
            var uploads = Path.Combine(_env.WebRootPath, "uploads", "announcements");
            Directory.CreateDirectory(uploads);
            return uploads;
        }

        // GET: /Admin/Announcements
        [HttpGet]
        public async Task<IActionResult> Index(Guid? companyId = null)
        {
            var q = _db.Announcements
                .Include(a => a.Company) // legacy single-company pointer (kept for back-compat)
                .Include(a => a.AuthorUser)
                .Include(a => a.Targets).ThenInclude(t => t.Company)
                .OrderByDescending(a => a.CreatedAtUtc)
                .AsQueryable();

            if (companyId.HasValue)
            {
                // Show announcements that target this company OR are global (no targets)
                q = q.Where(a => a.Targets.Any(t => t.CompanyId == companyId.Value) || !a.Targets.Any());
            }

            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            ViewBag.FilterCompanyId = companyId;

            var items = await q.AsNoTracking().ToListAsync();
            return View(items);
        }

        // GET: /Admin/Announcements/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            ViewBag.SelectedCompanyIds = Array.Empty<Guid>();
            return View(new Announcement());
        }

        // POST: /Admin/Announcements/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Announcement model, IFormFile? attachment, [FromForm] List<Guid>? targetCompanyIds)
        {
            // Server-managed fields BEFORE validation
            model.AuthorUserId = CurrentUserId;
            model.CreatedAtUtc = DateTime.UtcNow;
            ModelState.Remove(nameof(Announcement.AuthorUserId));
            ModelState.Remove(nameof(Announcement.CreatedAtUtc));

            // Validate attachment if present
            if (attachment != null && attachment.Length > 0)
            {
                if (attachment.Length > MaxAttachmentBytes)
                    ModelState.AddModelError(nameof(attachment), "File is too large (max 50 MB).");

                if (!LooksAllowedFile(attachment))
                    ModelState.AddModelError(nameof(attachment), "Unsupported file type. Allowed: PDF, DOCX, XLSX, PPTX, PNG, JPG.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                ViewBag.SelectedCompanyIds = targetCompanyIds ?? new List<Guid>();
                return View(model);
            }

            model.Id = Guid.NewGuid();

            // Save attachment (optional)
            if (attachment != null && attachment.Length > 0)
            {
                var uploads = EnsureUploadsFolder();
                var ext = Path.GetExtension(attachment.FileName);
                var storedName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
                var fsPath = Path.Combine(uploads, storedName);

                using (var fs = System.IO.File.Create(fsPath))
                    await attachment.CopyToAsync(fs);

                model.AttachmentFileName = SanitizeFileName(attachment.FileName);
                model.AttachmentPath = $"/uploads/announcements/{storedName}";
                model.AttachmentContentType = GetContentTypeFromExtension(ext);
                model.AttachmentSizeBytes = attachment.Length;
                model.AttachmentUploadedAtUtc = DateTime.UtcNow;
            }

            // Targets: if none selected => Global; else add join rows
            if (targetCompanyIds != null && targetCompanyIds.Any())
            {
                foreach (var cid in targetCompanyIds.Distinct())
                    model.Targets.Add(new AnnouncementCompany { AnnouncementId = model.Id, CompanyId = cid });
            }

            _db.Announcements.Add(model);
            await _db.SaveChangesAsync();

            TempData["ok"] = "Announcement created.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Announcements/Edit/{id}
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var a = await _db.Announcements
                .Include(x => x.Targets)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();

            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            ViewBag.SelectedCompanyIds = a.Targets.Select(t => t.CompanyId).ToList();
            return View(a);
        }

        // POST: /Admin/Announcements/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, Announcement model, IFormFile? attachment, [FromForm] List<Guid>? targetCompanyIds)
        {
            if (id != model.Id) return BadRequest();

            ModelState.Remove(nameof(Announcement.AuthorUserId));
            ModelState.Remove(nameof(Announcement.CreatedAtUtc));

            if (attachment != null && attachment.Length > 0)
            {
                if (attachment.Length > MaxAttachmentBytes)
                    ModelState.AddModelError(nameof(attachment), "File is too large (max 50 MB).");

                if (!LooksAllowedFile(attachment))
                    ModelState.AddModelError(nameof(attachment), "Unsupported file type. Allowed: PDF, DOCX, XLSX, PPTX, PNG, JPG.");
            }

            var a = await _db.Announcements
                .Include(x => x.Targets)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                ViewBag.SelectedCompanyIds = targetCompanyIds ?? a.Targets.Select(t => t.CompanyId).ToList();
                return View(a);
            }

            a.Title = model.Title;
            a.Body = model.Body;
            a.ExpiresAtUtc = model.ExpiresAtUtc;

            // Replace attachment if a new one is uploaded
            if (attachment != null && attachment.Length > 0)
            {
                // Delete old file if present
                if (!string.IsNullOrWhiteSpace(a.AttachmentPath))
                {
                    var oldFs = ToFsPathUnderWebRoot(_env.WebRootPath, a.AttachmentPath);
                    if (System.IO.File.Exists(oldFs))
                        System.IO.File.Delete(oldFs);
                }

                var uploads = EnsureUploadsFolder();
                var ext = Path.GetExtension(attachment.FileName);
                var storedName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
                var fsPath = Path.Combine(uploads, storedName);

                using (var fs = System.IO.File.Create(fsPath))
                    await attachment.CopyToAsync(fs);

                a.AttachmentFileName = SanitizeFileName(attachment.FileName);
                a.AttachmentPath = $"/uploads/announcements/{storedName}";
                a.AttachmentContentType = GetContentTypeFromExtension(ext);
                a.AttachmentSizeBytes = attachment.Length;
                a.AttachmentUploadedAtUtc = DateTime.UtcNow;
            }

            // Update targets: clear and re-add
            a.Targets.Clear();
            if (targetCompanyIds != null && targetCompanyIds.Any())
            {
                foreach (var cid in targetCompanyIds.Distinct())
                    a.Targets.Add(new AnnouncementCompany { AnnouncementId = a.Id, CompanyId = cid });
            }

            await _db.SaveChangesAsync();
            TempData["ok"] = "Announcement updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Announcements/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var a = await _db.Announcements.FindAsync(id);
            if (a == null) return NotFound();

            // Delete file from disk if present
            if (!string.IsNullOrWhiteSpace(a.AttachmentPath))
            {
                var fs = ToFsPathUnderWebRoot(_env.WebRootPath, a.AttachmentPath);
                if (System.IO.File.Exists(fs))
                    System.IO.File.Delete(fs);
            }

            _db.Announcements.Remove(a);
            await _db.SaveChangesAsync();

            TempData["ok"] = "Announcement deleted.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Announcements/Open/{id}  -> inline (PDF/images) for SuperAdmins (for quick preview)
        [HttpGet]
        public async Task<IActionResult> Open(Guid id)
        {
            var a = await _db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a == null || string.IsNullOrWhiteSpace(a.AttachmentPath)) return NotFound();

            var fsPath = ToFsPathUnderWebRoot(_env.WebRootPath, a.AttachmentPath);
            if (!System.IO.File.Exists(fsPath)) return NotFound();

            var ext = Path.GetExtension(a.AttachmentPath);
            var canInline = InlineViewableExtensions.Contains(ext);
            if (!canInline)
            {
                return RedirectToAction(nameof(Download), new { id });
            }

            var stream = System.IO.File.OpenRead(fsPath);
            var contentType = a.AttachmentContentType ?? GetContentTypeFromExtension(ext);
            return File(stream, contentType, enableRangeProcessing: true);
        }

        // GET: /Admin/Announcements/Download/{id} -> force download (any type)
        [HttpGet]
        public async Task<IActionResult> Download(Guid id)
        {
            var a = await _db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a == null || string.IsNullOrWhiteSpace(a.AttachmentPath)) return NotFound();

            var fsPath = ToFsPathUnderWebRoot(_env.WebRootPath, a.AttachmentPath);
            if (!System.IO.File.Exists(fsPath)) return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(fsPath);
            var fileName = string.IsNullOrWhiteSpace(a.AttachmentFileName) ? "attachment" : a.AttachmentFileName;
            var ext = Path.GetExtension(a.AttachmentPath);
            var contentType = a.AttachmentContentType ?? GetContentTypeFromExtension(ext);
            return File(bytes, contentType, fileName);
        }
    }
}
