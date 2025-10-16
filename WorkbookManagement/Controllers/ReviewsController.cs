using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WorkbookManagement.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "SuperAdmin")]
    public class ReviewsController : Controller
    {
        // GET: /Admin/Reviews -> redirects to your existing Admin/Submissions list
        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Submissions", new { area = "Admin" });
        }

        // (Optional) If you have a /Admin/Submissions/Review/{id} action,
        // you can add a friendly alias route here too:
        [HttpGet("Admin/Reviews/Review/{id}")]
        public IActionResult Review(int id)
        {
            return RedirectToAction("Review", "Submissions", new { area = "Admin", id });
        }
    }
}
