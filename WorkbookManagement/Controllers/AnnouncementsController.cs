using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Data;

namespace WorkbookManagement.Controllers
{
    [Authorize]
    public class AnnouncementsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AnnouncementsController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db; _env = env;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        private async Task<Guid?> CurrentUserCompanyIdAsync() =>
            await _db.Users.Where(u => u.Id == CurrentUserId).Select(u => u.CompanyId).FirstOrDefaultAsync();

        private static string ToFsPathUnderWebRoot(string webRootPath, string relativeWebPath)
        {
            var trimmed = relativeWebPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(webRootPath, trimmed);
        }

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

        [HttpGet]
        public async Task<IActionResult> Open(Guid id)
        {
            var a = await _db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a == null || string.IsNullOrWhiteSpace(a.AttachmentPath)) return NotFound();

            // Multi-company targeting: if targets exist, enforce; otherwise it's global
            var targets = await _db.AnnouncementCompanies
                .Where(t => t.AnnouncementId == id)
                .Select(t => t.CompanyId)
                .ToListAsync();

            if (targets.Any())
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null || !targets.Contains(cid.Value)) return Forbid();
            }

            if (!a.IsActive(DateTime.UtcNow)) return Forbid();

            var fsPath = ToFsPathUnderWebRoot(_env.WebRootPath, a.AttachmentPath);
            if (!System.IO.File.Exists(fsPath)) return NotFound();

            var ext = Path.GetExtension(a.AttachmentPath);
            var contentType = a.AttachmentContentType ?? GetContentTypeFromExtension(ext);
            var stream = System.IO.File.OpenRead(fsPath);
            return File(stream, contentType, enableRangeProcessing: true);
        }

        [HttpGet]
        public async Task<IActionResult> Download(Guid id)
        {
            var a = await _db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a == null || string.IsNullOrWhiteSpace(a.AttachmentPath)) return NotFound();

            var targets = await _db.AnnouncementCompanies
                .Where(t => t.AnnouncementId == id)
                .Select(t => t.CompanyId)
                .ToListAsync();

            if (targets.Any())
            {
                var cid = await CurrentUserCompanyIdAsync();
                if (cid == null || !targets.Contains(cid.Value)) return Forbid();
            }

            if (!a.IsActive(DateTime.UtcNow)) return Forbid();

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
