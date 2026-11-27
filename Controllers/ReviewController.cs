using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UFSWebApp.Infrastructure;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers
{
    [Authorize(Roles = "Customer")]
    [BlockWhenSuspended]
    public class ReviewsController : Controller
    {
        private readonly AppDbContext _db;
        public ReviewsController(AppDbContext db) => _db = db;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

        [HttpGet]
        public IActionResult Create() => View(new Review());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Review model)
        {
            if (!ModelState.IsValid) return View(model);

            model.userId = CurrentUserId;
            model.DateSubmitted = DateTime.UtcNow;
            _db.reviews.Add(model);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Thank you for your feedback!";
            return RedirectToAction("Index", "Customer");
        }
    }
}
