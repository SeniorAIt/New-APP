using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
// Bring your ApplicationUser into scope
using WorkbookManagement.Areas.Identity.Data;
using WorkbookManagement.Models;

namespace WorkbookManagement.Areas.Identity.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LogoutModel> _logger;

        public LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        // Support both GET and POST so direct navigation works too.
        public async Task<IActionResult> OnGet(string? returnUrl = null) => await SignOutAndRedirect(returnUrl);

        public async Task<IActionResult> OnPost(string? returnUrl = null) => await SignOutAndRedirect(returnUrl);

        private async Task<IActionResult> SignOutAndRedirect(string? returnUrl)
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");

            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            // Send them to Home or back to Login – pick your preference:
            return Redirect("~/"); // or: return RedirectToPage("./Login");
        }
    }
}