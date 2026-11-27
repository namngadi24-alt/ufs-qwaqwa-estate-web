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
    [Authorize]
    public class MaintenanceController : Controller
    {
        private readonly AppDbContext _db;
        private readonly FileStorage _files;
        public MaintenanceController(AppDbContext db, FileStorage files) { _db = db; _files = files; }
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task<List<Room>> MyRoomsAsync() =>
            await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId && a.IsActive).Select(a => a.Room).ToListAsync();

        // ------------------------------------------------------------------ customer
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Index()
        {
            ViewBag.HasRoom = (await MyRoomsAsync()).Count > 0;
            var list = await _db.maintenanceRequests.AsNoTracking().Include(m => m.Room).ThenInclude(r => r.Accommodation)
                .Where(m => m.userId == CurrentUserId).OrderByDescending(m => m.DateSubmitted).ToListAsync();
            return View(list);
        }

        [Authorize(Roles = "Customer")]
        [BlockWhenSuspended]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var rooms = await MyRoomsAsync();
            if (rooms.Count == 0)
            {
                TempData["Error"] = "You can report maintenance issues once your accommodation application is approved and a room is allocated.";
                return RedirectToAction("Index", "Customer");
            }
            return View(new MaintenanceFormViewModel { MyRooms = rooms, RoomId = rooms.Count == 1 ? rooms[0].RoomId : null });
        }

        [Authorize(Roles = "Customer")]
        [BlockWhenSuspended]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MaintenanceFormViewModel model)
        {
            var rooms = await MyRoomsAsync();
            model.MyRooms = rooms;
            if (rooms.Count == 0) return RedirectToAction("Index", "Customer");
            if (model.RoomId != null && !rooms.Any(r => r.RoomId == model.RoomId))
                ModelState.AddModelError(nameof(model.RoomId), "Please choose one of your own rooms.");
            if (model.Category != null && !MaintenanceCategories.All.Contains(model.Category))
                ModelState.AddModelError(nameof(model.Category), "Please select a valid type of problem.");

            string? photoPath = null;
            if (ModelState.IsValid)
            {
                var (path, error) = await _files.SaveImageAsync(model.Photo, "maintenance");
                if (error != null) ModelState.AddModelError(nameof(model.Photo), error);
                photoPath = path;
            }
            if (!ModelState.IsValid) return View(model);

            var request = new MaintenanceRequest
            {
                userId = CurrentUserId, RoomId = model.RoomId!.Value, Category = model.Category,
                Description = model.Description.Trim(), PhotoPath = photoPath
            };
            _db.maintenanceRequests.Add(request);
            Notify.To(_db, CurrentUserId, $"Your maintenance request ({model.Category}) was submitted. You will be notified when its status changes.", "success");
            await Notify.ToStaffAsync(_db, $"New maintenance request: {model.Category} - {User.FindFirstValue(ClaimTypes.Name)}.", "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = "Maintenance request submitted.";
            return RedirectToAction(nameof(Index));
        }

        // ------------------------------------------------------------------ staff
        [Authorize(Roles = "Admin,FinancialOfficer")]
        public async Task<IActionResult> Manage(string? status)
        {
            var q = _db.maintenanceRequests.AsNoTracking().Include(m => m.user).Include(m => m.Room).ThenInclude(r => r.Accommodation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(m => m.Status == status);
            ViewBag.Status = status ?? "All";
            return View(await q.OrderBy(m => m.Status == MaintenanceStatuses.Closed || m.Status == MaintenanceStatuses.Resolved).ThenByDescending(m => m.DateSubmitted).ToListAsync());
        }

        [Authorize(Roles = "Admin,FinancialOfficer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, string status, string? staffNote)
        {
            var request = await _db.maintenanceRequests.Include(m => m.Room).FirstOrDefaultAsync(m => m.MaintenanceRequestId == id);
            if (request == null) return NotFound();
            if (!MaintenanceStatuses.All.Contains(status))
            {
                TempData["Error"] = "Invalid status.";
                return RedirectToAction(nameof(Manage));
            }
            staffNote = staffNote?.Trim();
            if (staffNote?.Length > 500) staffNote = staffNote[..500];
            if (request.Status == status && request.StaffNote == staffNote)
            {
                TempData["Warning"] = "Nothing changed.";
                return RedirectToAction(nameof(Manage));
            }

            request.Status = status;
            request.StaffNote = staffNote;
            request.DateUpdated = DateTime.UtcNow;
            request.UpdatedBy = User.FindFirstValue(ClaimTypes.Name);
            Notify.To(_db, request.userId,
                $"Your maintenance request ({request.Category}) is now: {status}." + (string.IsNullOrWhiteSpace(staffNote) ? "" : $" Note: {staffNote}"),
                status is MaintenanceStatuses.Resolved or MaintenanceStatuses.Closed ? "success" : "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = "Request updated and the customer was notified.";
            return RedirectToAction(nameof(Manage));
        }
    }
}
