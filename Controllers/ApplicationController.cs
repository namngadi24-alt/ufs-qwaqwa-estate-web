using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers
{
    /// <summary>Customer accommodation applications (registration is separate - see AccountController).</summary>
    [Authorize(Roles = "Customer")]
    public class ApplicationController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AccommodationService _rooms;
        public ApplicationController(AppDbContext db, AccommodationService rooms) { _db = db; _rooms = rooms; }
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            var apps = await _db.applications.AsNoTracking()
                .Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId).OrderByDescending(a => a.DateSubmitted).ToListAsync();
            return View(apps);
        }

        [HttpGet]
        [BlockWhenSuspended]
        public async Task<IActionResult> Create(int? roomId)
        {
            var model = new ApplicationFormViewModel();
            if (roomId != null) model.RoomIds.Add(roomId.Value);
            model.MoveInDate = DateTime.Today.AddDays(14);
            await FillAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [BlockWhenSuspended]
        public async Task<IActionResult> Create(ApplicationFormViewModel model)
        {
            await FillAsync(model);
            if (!model.ProfileLoaded)
                ModelState.AddModelError(string.Empty, "Please press 'Load my profile details' so your profile information is used for this application.");
            if (!model.AcceptRules)
                ModelState.AddModelError(nameof(model.AcceptRules), "Please confirm that you have read the accommodation rules and conditions.");
            if (model.RoomIds == null || model.RoomIds.Count == 0)
                ModelState.AddModelError(nameof(model.RoomIds), "Please select at least one room.");
            // Profile information is always taken from the saved profile, never from the posted form.
            ModelState.Remove(nameof(model.FullName)); ModelState.Remove(nameof(model.IdentificationNumber));
            ModelState.Remove(nameof(model.PhoneNumber)); ModelState.Remove(nameof(model.Email));

            if (!ModelState.IsValid) return View(model);

            var (ok, message) = await _rooms.SubmitApplicationAsync(CurrentUserId, model.RoomIds, model.PreferredRoomType,
                model.NumberOfVehicles, model.MoveInDate!.Value, model.Notes);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }
            TempData["Success"] = message + " You will be notified once the Financial Officer or Agent Head has reviewed it.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var app = await _db.applications.FirstOrDefaultAsync(a => a.AccommodationApplicationId == id && a.userId == CurrentUserId);
            if (app == null) return NotFound();
            if (app.Status != ApplicationStatuses.Pending)
            {
                TempData["Error"] = "Only pending applications can be withdrawn.";
                return RedirectToAction(nameof(Index));
            }
            _db.applications.Remove(app);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Your application was withdrawn.";
            return RedirectToAction(nameof(Index));
        }

        private async Task FillAsync(ApplicationFormViewModel model)
        {
            var user = await _db.users.AsNoTracking().FirstAsync(u => u.UserId == CurrentUserId);
            model.PersonType = user.PersonType;
            model.AvailableRooms = (await _rooms.RoomsAsync()).Where(r => r.IsAvailable).OrderBy(r => r.Room.Accommodation.AccommodationName).ThenBy(r => r.Room.RoomNumber).ToList();
            // Values the "Load my profile" button copies into the form (nothing is pre-filled automatically).
            ViewBag.ProfileName = user.FullName;
            ViewBag.ProfileNumber = user.IdentificationNumber;
            ViewBag.ProfilePhone = user.PhoneNumber;
            ViewBag.ProfileEmail = user.UserEmail;
        }
    }
}
