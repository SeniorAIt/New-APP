using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WorkbookManagement.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "SuperAdmin")]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        public UsersController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        // ===== LIST with search/filter =====
        // GET: /Admin/Users?q=<email/company text>&companyId=<guid>
        public async Task<IActionResult> Index(string? q, Guid? companyId)
        {
            // base query
            IQueryable<ApplicationUser> query = _db.Users
                .Include(u => u.Company)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(u =>
                    u.Email!.Contains(term) ||
                    (u.Company != null && u.Company.Name.Contains(term)));
            }

            if (companyId.HasValue && companyId.Value != Guid.Empty)
            {
                query = query.Where(u => u.CompanyId == companyId);
            }

            var list = await query
                .OrderBy(u => u.Company!.Name)
                .ThenBy(u => u.Email)
                .ToListAsync();

            ViewBag.Search = q ?? string.Empty;
            ViewBag.CompanyId = companyId;
            ViewBag.Companies = await _db.Companies
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();

            return View(list);
        }

        // ===== CREATE =====
        public async Task<IActionResult> Create()
        {
            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            return View(new CreateUserVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserVm vm)
        {
            // Company is required unless creating a SuperAdmin
            if (!vm.IsSuperAdmin && (vm.CompanyId == null || vm.CompanyId == Guid.Empty))
            {
                ModelState.AddModelError(nameof(vm.CompanyId), "Please select a company.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                return View(vm);
            }

            var user = new ApplicationUser
            {
                UserName = vm.Email,
                Email = vm.Email,
                EmailConfirmed = true,
                CompanyId = vm.IsSuperAdmin ? null : vm.CompanyId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var createRes = await _users.CreateAsync(user, vm.Password);
            if (!createRes.Succeeded)
            {
                foreach (var e in createRes.Errors)
                    ModelState.AddModelError("", e.Description);

                ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
                return View(vm);
            }

            var role = vm.IsSuperAdmin ? "SuperAdmin" : "CompanyAdmin";
            await _users.AddToRoleAsync(user, role);

            TempData["ok"] = $"User '{user.Email}' created.";
            return RedirectToAction(nameof(Index));
        }

        // ===== DETAILS =====
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var u = await _db.Users
                .Include(x => x.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return NotFound();
            return View(u);
        }

        // ===== EDIT =====
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return NotFound();

            ViewBag.Companies = await _db.Companies
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();

            return View(u);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, [Bind("Id,CompanyId,IsActive")] ApplicationUser posted)
        {
            if (id != posted.Id) return BadRequest();

            var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return NotFound();

            u.CompanyId = posted.CompanyId;
            u.IsActive = posted.IsActive;

            // lock/unlock identity when toggling IsActive
            if (!u.IsActive)
            {
                u.LockoutEnabled = true;
                u.LockoutEnd = DateTimeOffset.MaxValue;
            }
            else
            {
                u.LockoutEnd = null;
                u.LockoutEnabled = false;
            }

            await _db.SaveChangesAsync();
            TempData["ok"] = "User updated.";
            return RedirectToAction(nameof(Index));
        }

        // ===== SUSPEND =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Suspend(string id)
        {
            var u = await _db.Users.FindAsync(id);
            if (u == null) { TempData["err"] = "User not found."; return RedirectToAction(nameof(Index)); }

            if (!u.IsActive)
            {
                TempData["ok"] = "User is already suspended.";
                return RedirectToAction(nameof(Index));
            }

            u.IsActive = false;
            u.LockoutEnabled = true;
            u.LockoutEnd = DateTimeOffset.MaxValue;

            await _db.SaveChangesAsync();
            TempData["ok"] = $"User '{u.Email}' suspended.";
            return RedirectToAction(nameof(Index));
        }

        // ===== ACTIVATE =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(string id)
        {
            var u = await _db.Users.FindAsync(id);
            if (u == null) { TempData["err"] = "User not found."; return RedirectToAction(nameof(Index)); }

            if (u.IsActive)
            {
                TempData["ok"] = "User is already active.";
                return RedirectToAction(nameof(Index));
            }

            u.IsActive = true;
            u.LockoutEnd = null;
            u.LockoutEnabled = false;

            await _db.SaveChangesAsync();
            TempData["ok"] = $"User '{u.Email}' activated.";
            return RedirectToAction(nameof(Index));
        }

        // ===== DELETE =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) { TempData["err"] = "User not found."; return RedirectToAction(nameof(Index)); }

            var result = await _users.DeleteAsync(u);
            if (!result.Succeeded)
            {
                TempData["err"] = "Failed to delete user: " + string.Join("; ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            TempData["ok"] = $"User '{u.Email}' deleted.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class CreateUserVm
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public Guid? CompanyId { get; set; }

        public bool IsSuperAdmin { get; set; } = false;
    }
}
