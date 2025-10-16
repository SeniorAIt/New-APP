using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "SuperAdmin")]
    public class CompaniesController : Controller
    {
        private readonly ApplicationDbContext _db;
        public CompaniesController(ApplicationDbContext db) => _db = db;

        // LIST with Search / Filter / Sort
        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] CompaniesIndexQueryVm q)
        {
            // base query
            var companies = _db.Companies.AsQueryable();

            // search by name/email/phone
            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                var s = q.Search.Trim();
                companies = companies.Where(c =>
                    c.Name.Contains(s) ||
                    (c.ContactEmail != null && c.ContactEmail.Contains(s)) ||
                    (c.ContactPhone != null && c.ContactPhone.Contains(s))
                );
            }

            // active-only toggle
            if (q.ActiveOnly == true)
            {
                companies = companies.Where(c => c.IsActive);
            }

            // sorting
            companies = q.Sort switch
            {
                "name_desc" => companies.OrderByDescending(c => c.Name),
                "created_asc" => companies.OrderBy(c => c.CreatedAt),
                "created_desc" => companies.OrderByDescending(c => c.CreatedAt),
                _ => companies.OrderBy(c => c.Name) // default Name ↑
            };

            var list = await companies
                .AsNoTracking()
                .ToListAsync();

            // keep the query in ViewBag for the view to persist inputs
            ViewBag.Query = q;

            return View(list);
        }

        // DETAILS (View)
        public async Task<IActionResult> Details(Guid id)
        {
            var c = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            return View(c);
        }

        // CREATE
        public IActionResult Create() => View(new Company());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Company model)
        {
            if (!ModelState.IsValid) return View(model);

            var exists = await _db.Companies.AnyAsync(c => c.Name == model.Name);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.Name), "A company with this name already exists.");
                return View(model);
            }

            // Assume default values
            model.IsActive = true;
            if (model.CreatedAt == default) model.CreatedAt = DateTime.UtcNow;

            _db.Add(model);
            await _db.SaveChangesAsync();
            TempData["ok"] = $"Company '{model.Name}' created.";
            return RedirectToAction(nameof(Index));
        }

        // EDIT
        public async Task<IActionResult> Edit(Guid id)
        {
            var c = await _db.Companies.FindAsync(id);
            if (c == null) return NotFound();
            return View(c);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("Id,Name,ContactEmail,ContactPhone,IsActive,CreatedAt")] Company model)
        {
            if (id != model.Id) return BadRequest();
            if (!ModelState.IsValid) return View(model);

            var c = await _db.Companies.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            // Unique name check (excluding current)
            var nameTaken = await _db.Companies.AnyAsync(x => x.Id != id && x.Name == model.Name);
            if (nameTaken)
            {
                ModelState.AddModelError(nameof(model.Name), "A company with this name already exists.");
                return View(model);
            }

            c.Name = model.Name;
            c.ContactEmail = model.ContactEmail;
            c.ContactPhone = model.ContactPhone;
            c.IsActive = model.IsActive;
            // keep original CreatedAt
            await _db.SaveChangesAsync();

            TempData["ok"] = $"Company '{c.Name}' updated.";
            return RedirectToAction(nameof(Index));
        }

        // SUSPEND
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Suspend(Guid id)
        {
            var c = await _db.Companies.FindAsync(id);
            if (c == null) { TempData["err"] = "Company not found."; return RedirectToAction(nameof(Index)); }
            if (!c.IsActive) { TempData["ok"] = "Company is already suspended."; return RedirectToAction(nameof(Index)); }

            c.IsActive = false;
            await _db.SaveChangesAsync();

            TempData["ok"] = $"Company '{c.Name}' suspended.";
            return RedirectToAction(nameof(Index));
        }

        // ACTIVATE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(Guid id)
        {
            var c = await _db.Companies.FindAsync(id);
            if (c == null) { TempData["err"] = "Company not found."; return RedirectToAction(nameof(Index)); }
            if (c.IsActive) { TempData["ok"] = "Company is already active."; return RedirectToAction(nameof(Index)); }

            c.IsActive = true;
            await _db.SaveChangesAsync();

            TempData["ok"] = $"Company '{c.Name}' activated.";
            return RedirectToAction(nameof(Index));
        }

        // DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var c = await _db.Companies.FindAsync(id);
            if (c == null) { TempData["err"] = "Company not found."; return RedirectToAction(nameof(Index)); }

            _db.Companies.Remove(c);
            await _db.SaveChangesAsync();
            TempData["ok"] = $"Company '{c.Name}' deleted.";
            return RedirectToAction(nameof(Index));
        }
    }

    // Simple query VM for Index (kept in this file for convenience)
    public class CompaniesIndexQueryVm
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }          // "", "name_desc", "created_asc", "created_desc"
        public bool? ActiveOnly { get; set; }      // true => only active
    }
}
