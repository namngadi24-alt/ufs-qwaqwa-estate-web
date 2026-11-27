using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers
{
    /// <summary>Public accommodation browsing - no account or login required.</summary>
    [AllowAnonymous]
    public class AccommodationController : Controller
    {
        private readonly CatalogService _catalog;
        private readonly AccommodationService _rooms;
        public AccommodationController(CatalogService catalog, AccommodationService rooms) { _catalog = catalog; _rooms = rooms; }

        public async Task<IActionResult> Index(string? type, string? roomType, bool availableOnly = false)
        {
            var accommodations = (await _catalog.GetAccommodationsAsync()).Where(a => a.IsPublished).ToList();
            if (!string.IsNullOrWhiteSpace(type)) accommodations = accommodations.Where(a => a.AccommodationType == type).ToList();

            var all = await _rooms.RoomsAsync();
            if (!string.IsNullOrWhiteSpace(roomType)) all = all.Where(r => r.Room.RoomType == roomType).ToList();
            if (availableOnly) all = all.Where(r => r.IsAvailable).ToList();
            if (!string.IsNullOrWhiteSpace(roomType) || availableOnly)
            {
                var ids = all.Select(r => r.Room.AccommodationId).Distinct().ToList();
                accommodations = accommodations.Where(a => ids.Contains(a.AccommodationId)).ToList();
            }

            ViewBag.Rooms = all;
            ViewBag.Type = type; ViewBag.RoomType = roomType; ViewBag.AvailableOnly = availableOnly;
            return View(accommodations);
        }

        public async Task<IActionResult> Details(int id)
        {
            var accommodation = (await _catalog.GetAccommodationsAsync()).FirstOrDefault(a => a.AccommodationId == id && a.IsPublished);
            if (accommodation == null) return NotFound();
            ViewBag.Rooms = await _rooms.RoomsAsync(id);
            ViewBag.Tariffs = await _catalog.GetTariffsAsync();
            return View(accommodation);
        }
    }
}
