using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    /// <summary>
    /// Short-lived in-memory cache for data that every visitor needs and that rarely changes
    /// (accommodations, photos, rooms, tariffs). This is what makes the home, browse, details and
    /// application pages fast. Availability (who is in a room) is NOT cached - it is always read fresh.
    /// The cache is cleared automatically whenever an administrator or officer changes the data.
    /// </summary>
    public class CatalogService
    {
        private const string AccommodationsKey = "catalog:accommodations";
        private const string RoomsKey = "catalog:rooms";
        private const string TariffsKey = "catalog:tariffs";
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;

        public CatalogService(AppDbContext db, IMemoryCache cache) { _db = db; _cache = cache; }

        public async Task<List<Accommodation>> GetAccommodationsAsync() =>
            await _cache.GetOrCreateAsync(AccommodationsKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Ttl;
                return await _db.accommodations.AsNoTracking().Include(a => a.Photos)
                    .OrderBy(a => a.AccommodationName).ToListAsync();
            }) ?? new List<Accommodation>();

        public async Task<List<Room>> GetRoomsAsync() =>
            await _cache.GetOrCreateAsync(RoomsKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Ttl;
                return await _db.rooms.AsNoTracking().Include(r => r.Accommodation)
                    .OrderBy(r => r.RoomNumber).ToListAsync();
            }) ?? new List<Room>();

        public async Task<List<Tariff>> GetTariffsAsync() =>
            await _cache.GetOrCreateAsync(TariffsKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Ttl;
                return await _db.tariffs.AsNoTracking().Include(t => t.Changes).ToListAsync();
            }) ?? new List<Tariff>();

        /// <summary>Call after any change to accommodations, rooms, photos or tariffs.</summary>
        public void Invalidate()
        {
            _cache.Remove(AccommodationsKey);
            _cache.Remove(RoomsKey);
            _cache.Remove(TariffsKey);
        }
    }
}
