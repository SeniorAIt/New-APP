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
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _db;

        public CalendarController(ApplicationDbContext db)
        {
            _db = db;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: /Admin/Calendar
        public async Task<IActionResult> Index(Guid? companyId = null)
        {
            var q = _db.CalendarEvents
                .Include(e => e.Company)
                .Include(e => e.CreatedByUser)
                .OrderBy(e => e.StartUtc)
                .AsQueryable();

            if (companyId.HasValue)
                q = q.Where(e => e.CompanyId == companyId);

            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            ViewBag.FilterCompanyId = companyId;

            var items = await q.AsNoTracking().ToListAsync();
            return View(items);
        }

        // GET: /Admin/Calendar/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            return View(new CalendarEvent
            {
                AllDay = true,
                StartUtc = DateTime.UtcNow.Date
            });
        }

        // POST: /Admin/Calendar/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CalendarEvent model)
        {
            // Normalize time if needed
            if (!model.AllDay && !model.EndUtc.HasValue && model.StartUtc != default)
                model.EndUtc = model.StartUtc.AddHours(1);

            // Set server-managed field BEFORE validation, then clear its modelstate key
            model.CreatedByUserId = CurrentUserId;
            ModelState.Remove(nameof(CalendarEvent.CreatedByUserId));

            if (!ModelState.IsValid)
            {
                ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                return View(model);
            }

            model.Id = Guid.NewGuid();

            _db.CalendarEvents.Add(model);
            await _db.SaveChangesAsync();

            TempData["ok"] = "Event created.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Calendar/Edit/{id}
        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var e = await _db.CalendarEvents.FindAsync(id);
            if (e == null) return NotFound();
            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            return View(e);
        }

        // POST: /Admin/Calendar/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, CalendarEvent model)
        {
            if (id != model.Id) return BadRequest();

            // CreatedByUserId is server-managed and not posted; remove it from validation
            ModelState.Remove(nameof(CalendarEvent.CreatedByUserId));

            // Normalize again
            if (!model.AllDay && !model.EndUtc.HasValue && model.StartUtc != default)
                model.EndUtc = model.StartUtc.AddHours(1);

            if (!ModelState.IsValid)
            {
                ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                return View(model);
            }

            var e = await _db.CalendarEvents.FindAsync(id);
            if (e == null) return NotFound();

            e.Title = model.Title;
            e.Description = model.Description;
            e.AllDay = model.AllDay;
            e.StartUtc = model.StartUtc;
            e.EndUtc = model.EndUtc;
            e.CompanyId = model.CompanyId;
            e.Category = model.Category;

            await _db.SaveChangesAsync();

            TempData["ok"] = "Event updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Calendar/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var e = await _db.CalendarEvents.FindAsync(id);
            if (e == null) return NotFound();

            _db.CalendarEvents.Remove(e);
            await _db.SaveChangesAsync();

            TempData["ok"] = "Event deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
