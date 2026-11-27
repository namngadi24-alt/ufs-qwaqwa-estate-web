using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers
{
    /// <summary>Accommodation, rooms and photographs (Administrator / Agent Head).</summary>
    [Authorize(Roles = "Admin")]
    public class ManageAccommodationController : Controller
    {
        private readonly AppDbContext _db;
        private readonly FileStorage _files;
        private readonly AccommodationService _rooms;
        private readonly CatalogService _catalog;
        public ManageAccommodationController(AppDbContext db, FileStorage files, AccommodationService rooms, CatalogService catalog) { _db = db; _files = files; _rooms = rooms; _catalog = catalog; }

        public async Task<IActionResult> Index()
        {
            ViewBag.Rooms = await _rooms.RoomsAsync();
            return View(await _db.accommodations.AsNoTracking().Include(a => a.Photos).OrderBy(a => a.AccommodationName).ToListAsync());
        }

        [HttpGet]
        public IActionResult Create() => View("Edit", new Accommodation());

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var a = await _db.accommodations.AsNoTracking().FirstOrDefaultAsync(x => x.AccommodationId == id);
            return a == null ? NotFound() : View(a);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(Accommodation model)
        {
            ModelState.Remove(nameof(model.Rooms)); ModelState.Remove(nameof(model.Photos));
            if (!AccommodationTypes.All.Contains(model.AccommodationType)) ModelState.AddModelError(nameof(model.AccommodationType), "Please choose a valid type.");
            if (!ModelState.IsValid) return View("Edit", model);

            if (model.AccommodationId == 0) _db.accommodations.Add(model);
            else
            {
                var existing = await _db.accommodations.FirstOrDefaultAsync(a => a.AccommodationId == model.AccommodationId);
                if (existing == null) return NotFound();
                existing.AccommodationName = model.AccommodationName; existing.AccommodationLocation = model.AccommodationLocation;
                existing.AccommodationType = model.AccommodationType; existing.Description = model.Description;
                existing.Rules = model.Rules; existing.IsPublished = model.IsPublished;
            }
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            TempData["Success"] = "Accommodation saved.";
            return RedirectToAction(nameof(Rooms), new { id = model.AccommodationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var a = await _db.accommodations.FirstOrDefaultAsync(x => x.AccommodationId == id);
            if (a == null) return NotFound();
            var hasHistory = await _db.roomAllocations.AnyAsync(x => x.Room.AccommodationId == id) ||
                             await _db.applicationRooms.AnyAsync(x => x.Room.AccommodationId == id) ||
                             await _db.maintenanceRequests.AnyAsync(x => x.Room.AccommodationId == id);
            if (hasHistory)
            {
                TempData["Error"] = "This accommodation has allocation, application or maintenance history, so it cannot be deleted. Hide it from the public site instead.";
                return RedirectToAction(nameof(Index));
            }
            _db.accommodations.Remove(a);
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            TempData["Success"] = "Accommodation deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ------------------------------------------------------------------ rooms and photos
        public async Task<IActionResult> Rooms(int id)
        {
            var a = await _db.accommodations.AsNoTracking().Include(x => x.Photos).FirstOrDefaultAsync(x => x.AccommodationId == id);
            if (a == null) return NotFound();
            ViewBag.Rooms = await _rooms.RoomsAsync(id);
            return View(a);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddRoom(int accommodationId, string roomNumber, string roomType, int capacity, string? facilities)
        {
            roomNumber = roomNumber?.Trim() ?? "";
            if (roomNumber.Length == 0 || roomNumber.Length > 20) TempData["Error"] = "Enter a room number (up to 20 characters).";
            else if (!RoomTypes.All.Contains(roomType)) TempData["Error"] = "Please choose a valid room type.";
            else if (capacity < 1 || capacity > 6) TempData["Error"] = "Capacity must be between 1 and 6.";
            else if (await _db.rooms.AnyAsync(r => r.AccommodationId == accommodationId && r.RoomNumber == roomNumber)) TempData["Error"] = $"Room {roomNumber} already exists here.";
            else
            {
                _db.rooms.Add(new Room { AccommodationId = accommodationId, RoomNumber = roomNumber, RoomType = roomType, Capacity = capacity, Facilities = facilities?.Trim() });
                await _db.SaveChangesAsync();
            _catalog.Invalidate();
                TempData["Success"] = $"Room {roomNumber} added.";
            }
            return RedirectToAction(nameof(Rooms), new { id = accommodationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRoomMaintenance(int roomId)
        {
            var room = await _db.rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
            if (room == null) return NotFound();
            room.IsUnderMaintenance = !room.IsUnderMaintenance;
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            TempData["Success"] = $"Room {room.RoomNumber} is now {(room.IsUnderMaintenance ? "marked under maintenance (not available)" : "available again")}.";
            return RedirectToAction(nameof(Rooms), new { id = room.AccommodationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRoom(int roomId)
        {
            var room = await _db.rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
            if (room == null) return NotFound();
            if (await _db.roomAllocations.AnyAsync(a => a.RoomId == roomId) || await _db.applicationRooms.AnyAsync(a => a.RoomId == roomId) ||
                await _db.maintenanceRequests.AnyAsync(m => m.RoomId == roomId) || await _db.charges.AnyAsync(c => c.RoomId == roomId))
            {
                TempData["Error"] = "This room has history and cannot be deleted. Mark it as under maintenance instead.";
            }
            else
            {
                _db.rooms.Remove(room);
                await _db.SaveChangesAsync();
            _catalog.Invalidate();
                TempData["Success"] = "Room deleted.";
            }
            return RedirectToAction(nameof(Rooms), new { id = room.AccommodationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhoto(int accommodationId, IFormFile? photo, string? caption)
        {
            if (photo == null) TempData["Error"] = "Please choose a photo to upload.";
            else
            {
                var (path, error) = await _files.SaveImageAsync(photo, "accommodation");
                if (error != null) TempData["Error"] = error;
                else
                {
                    _db.accommodationPhotos.Add(new AccommodationPhoto { AccommodationId = accommodationId, Path = path!, Caption = caption?.Trim() });
                    await _db.SaveChangesAsync();
            _catalog.Invalidate();
                    TempData["Success"] = "Photo added.";
                }
            }
            return RedirectToAction(nameof(Rooms), new { id = accommodationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePhoto(int photoId)
        {
            var p = await _db.accommodationPhotos.FirstOrDefaultAsync(x => x.AccommodationPhotoId == photoId);
            if (p == null) return NotFound();
            _db.accommodationPhotos.Remove(p);
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            TempData["Success"] = "Photo removed.";
            return RedirectToAction(nameof(Rooms), new { id = p.AccommodationId });
        }
    }
}
