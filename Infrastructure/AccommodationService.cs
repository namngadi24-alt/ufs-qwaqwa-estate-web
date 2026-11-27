using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public class RoomInfo
    {
        public Room Room { get; set; }
        public int Occupants { get; set; }
        public decimal? Rent { get; set; }
        public int FreeSpaces => Math.Max(0, Room.Capacity - Occupants);
        public bool IsAvailable => !Room.IsUnderMaintenance && FreeSpaces > 0;
        public string Status => Room.IsUnderMaintenance ? "Under maintenance" : IsAvailable ? "Available" : "Occupied";
    }

    public class AccommodationService
    {
        private readonly AppDbContext _db;
        private readonly CatalogService _catalog;
        public AccommodationService(AppDbContext db, CatalogService catalog) { _db = db; _catalog = catalog; }

        /// <summary>Rooms with rent and live occupancy. Room and tariff data come from the cache; only occupancy is queried.</summary>
        public async Task<List<RoomInfo>> RoomsAsync(int? accommodationId = null)
        {
            var rooms = await _catalog.GetRoomsAsync();
            if (accommodationId != null) rooms = rooms.Where(r => r.AccommodationId == accommodationId).ToList();

            var occupancy = await _db.roomAllocations.AsNoTracking().Where(a => a.IsActive)
                .GroupBy(a => a.RoomId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            var tariffs = await _catalog.GetTariffsAsync();
            var today = DateTime.Today;
            var rent = new Dictionary<string, decimal?>();
            foreach (var type in rooms.Select(r => r.RoomType).Distinct())
            {
                var t = BillingService.Find(tariffs, TariffCategories.Rent, type);
                rent[type] = t == null ? null : BillingService.RateOn(t, today);
            }

            return rooms.Select(r => new RoomInfo
            {
                Room = r,
                Occupants = occupancy.TryGetValue(r.RoomId, out var c) ? c : 0,
                Rent = rent.TryGetValue(r.RoomType, out var price) ? price : null
            }).ToList();
        }

        public async Task<(bool ok, string message)> SubmitApplicationAsync(int userId, List<int> roomIds, string? preferredType,
            int vehicles, DateTime moveIn, string? notes)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return (false, "User not found.");
            var status = await _db.accounts.Where(a => a.userId == userId).Select(a => a.AccountStatus).FirstOrDefaultAsync();
            if (AccountStatuses.IsSuspended(status)) return (false, "Your account is suspended, so you cannot apply right now.");

            roomIds = roomIds.Distinct().ToList();
            if (roomIds.Count == 0) return (false, "Please select at least one room.");
            if (vehicles < 0 || vehicles > 10) return (false, "Number of vehicles must be between 0 and 10.");
            if (moveIn.Date < DateTime.Today || moveIn.Date > DateTime.Today.AddMonths(12))
                return (false, "The move-in date must be between today and 12 months from now.");
            if (!string.IsNullOrEmpty(preferredType) && !RoomTypes.All.Contains(preferredType))
                return (false, "Please choose a valid room type.");

            var pendingRoomIds = await _db.applicationRooms
                .Where(a => a.Application.userId == userId && a.Application.Status == ApplicationStatuses.Pending)
                .Select(a => a.RoomId).ToListAsync();
            if (roomIds.Any(pendingRoomIds.Contains)) return (false, "You already have a pending application for one of the selected rooms.");

            var infos = (await RoomsAsync()).Where(r => roomIds.Contains(r.Room.RoomId)).ToList();
            if (infos.Count != roomIds.Count) return (false, "One of the selected rooms does not exist.");
            var unavailable = infos.FirstOrDefault(i => !i.IsAvailable);
            if (unavailable != null) return (false, $"{unavailable.Room.RoomNumber} is not available at the moment.");

            var application = new AccommodationApplication
            {
                userId = userId,
                PreferredRoomType = preferredType,
                NumberOfVehicles = vehicles,
                PlannedMoveInDate = moveIn.Date,
                Notes = notes?.Trim(),
                Rooms = roomIds.Select(id => new ApplicationRoom { RoomId = id }).ToList()
            };
            _db.applications.Add(application);
            await _db.SaveChangesAsync();
            Notify.To(_db, userId, $"Your accommodation application {application.Reference} was submitted. You will be notified when it has been reviewed.", "success");
            await Notify.ToStaffAsync(_db, $"New accommodation application {application.Reference} from {user.FullName} ({user.IdentificationNumber}) is waiting for review.", "info");
            await _db.SaveChangesAsync();
            return (true, $"Application {application.Reference} submitted.");
        }

        public async Task<(bool ok, string message)> DecideAsync(int applicationId, bool approve, string? note, string decidedBy)
        {
            var app = await _db.applications.Include(a => a.user).Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation)
                .FirstOrDefaultAsync(a => a.AccommodationApplicationId == applicationId);
            if (app == null) return (false, "Application not found.");
            if (app.Status != ApplicationStatuses.Pending) return (false, $"{app.Reference} has already been {app.Status.ToLower()}.");
            note = note?.Trim();

            if (!approve)
            {
                if (string.IsNullOrWhiteSpace(note) || note.Length < 5) return (false, "Please give a reason for rejecting (at least 5 characters). The customer will see it.");
                app.Status = ApplicationStatuses.Rejected;
                app.DateDecided = DateTime.UtcNow; app.DecidedBy = decidedBy; app.DecisionNote = note;
                Notify.To(_db, app.userId, $"Your accommodation application {app.Reference} was rejected. Reason: {note}", "danger");
                await _db.SaveChangesAsync();
                return (true, $"{app.Reference} rejected and the customer was notified.");
            }

            var roomIds = app.Rooms.Select(r => r.RoomId).ToList();
            var infos = (await RoomsAsync()).Where(r => roomIds.Contains(r.Room.RoomId)).ToList();
            var gone = infos.FirstOrDefault(i => !i.IsAvailable);
            if (gone != null) return (false, $"Room {gone.Room.RoomNumber} is no longer available. Reject the application or ask the customer to re-apply for another room.");

            foreach (var ar in app.Rooms)
            {
                _db.roomAllocations.Add(new RoomAllocation
                {
                    userId = app.userId, RoomId = ar.RoomId, ApplicationId = app.AccommodationApplicationId,
                    MoveInDate = app.PlannedMoveInDate, IsActive = true
                });
            }
            app.user.NumberOfVehicles = app.NumberOfVehicles;
            app.Status = ApplicationStatuses.Approved;
            app.DateDecided = DateTime.UtcNow; app.DecidedBy = decidedBy; app.DecisionNote = note;
            var labels = string.Join(", ", app.Rooms.Select(r => r.Room.Label));
            Notify.To(_db, app.userId,
                $"Your accommodation application {app.Reference} was approved! You have been allocated: {labels}. Move-in date: {app.PlannedMoveInDate:d MMMM yyyy}. " +
                "Your first rent and refuse charges are prorated from the move-in date and available to pay now. Parking is also charged for additional vehicles. You can now report maintenance issues and pay for electricity.", "success");
            await _db.SaveChangesAsync();
            return (true, $"{app.Reference} approved. {labels} allocated to {app.user.FullName}.");
        }
    }
}
