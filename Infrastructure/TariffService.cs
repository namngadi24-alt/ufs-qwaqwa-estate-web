using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public class TariffService
    {
        private readonly AppDbContext _db;
        private readonly CatalogService _catalog;
        public TariffService(AppDbContext db, CatalogService catalog) { _db = db; _catalog = catalog; }

        public async Task<(bool ok, string message)> UpdateAsync(int tariffId, decimal newRate, DateTime effectiveDate, string changedBy)
        {
            var tariff = await _db.tariffs.FirstOrDefaultAsync(t => t.TariffId == tariffId);
            if (tariff == null) return (false, "Tariff not found.");
            newRate = Math.Round(newRate, 2);
            if (newRate <= 0 || newRate > 100000) return (false, "The rate must be greater than 0 and not more than R100 000.");
            if (effectiveDate.Date < DateTime.Today) return (false, "The effective date cannot be in the past.");
            if (effectiveDate.Date > DateTime.Today.AddYears(1)) return (false, "The effective date must be within the next 12 months.");
            if (newRate == tariff.Rate && effectiveDate.Date == tariff.EffectiveFrom.Date) return (false, "Nothing has changed.");

            var old = tariff.Rate;
            _db.tariffChanges.Add(new TariffChange
            {
                TariffId = tariff.TariffId, OldRate = old, NewRate = newRate,
                EffectiveDate = effectiveDate.Date, ChangedBy = changedBy
            });
            tariff.Rate = newRate;
            tariff.EffectiveFrom = effectiveDate.Date;
            tariff.LastUpdated = DateTime.UtcNow;
            tariff.UpdatedBy = changedBy;

            // Notify the customers this tariff applies to.
            IQueryable<int> affected;
            var occupants = _db.roomAllocations.Where(a => a.IsActive);
            if (tariff.Category == TariffCategories.Rent)
                affected = occupants.Where(a => a.Room.RoomType == tariff.RoomType).Select(a => a.userId);
            else if (tariff.Category == TariffCategories.Parking)
                affected = occupants.Where(a => a.user.NumberOfVehicles > 1).Select(a => a.userId);
            else
                affected = occupants.Select(a => a.userId);
            var ids = await affected.Distinct().ToListAsync();

            var verb = newRate >= old ? "increase" : "decrease";
            var text = $"Tariff change: {tariff.Name} will {verb} from R{old:N2} to R{newRate:N2} ({tariff.Unit}) effective {effectiveDate:d MMMM yyyy}.";
            foreach (var id in ids) Notify.To(_db, id, text, "warning");
            if (tariff.Category == TariffCategories.Electricity)
                await Notify.ToStaffAsync(_db, $"Electricity rate updated by {changedBy}: R{newRate:N2} per unit from {effectiveDate:d MMMM yyyy}.", "info");
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return (true, $"{tariff.Name} updated to R{newRate:N2} effective {effectiveDate:d MMMM yyyy}. {ids.Count} customer(s) notified.");
        }
    }
}
