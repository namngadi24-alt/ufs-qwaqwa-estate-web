using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    /// <summary>Records water usage and electricity consumption (by Financial Officer / Admin) and turns them into charges.</summary>
    public class UtilityService
    {
        public const decimal LowElectricityThreshold = 20m;
        private readonly AppDbContext _db;
        private readonly BillingService _billing;

        public UtilityService(AppDbContext db, BillingService billing)
        {
            _db = db; _billing = billing;
        }

        public async Task<(bool ok, string message)> RecordWaterAsync(int customerUserId, DateTime period, decimal usageKl, string recordedBy)
        {
            usageKl = Math.Round(usageKl, 2);
            if (usageKl <= 0 || usageKl > 1000) return (false, "Water usage must be between 0.01 and 1000 kl.");
            var thisMonth = BillingService.MonthStart(DateTime.Today);
            period = BillingService.MonthStart(period);
            if (period > thisMonth) return (false, "You cannot record usage for a future month.");

            var account = await _db.accounts.Include(a => a.user).FirstOrDefaultAsync(a => a.userId == customerUserId);
            if (account == null) return (false, "Customer account not found.");
            var alloc = await _db.roomAllocations.Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == customerUserId && a.IsActive).OrderBy(a => a.MoveInDate).FirstOrDefaultAsync();
            if (alloc == null) return (false, $"{account.user.FullName} has no allocated accommodation.");
            if (alloc.MoveInDate > BillingService.MonthEnd(period)) return (false, "The customer had not moved in during that month.");
            if (await _db.waterReadings.AnyAsync(w => w.AccountId == account.AccountId && w.BillingPeriod == period))
                return (false, $"Water usage for {period:MMMM yyyy} was already recorded for {account.user.FullName}.");

            var tariff = BillingService.Find(await _billing.LoadTariffsAsync(), TariffCategories.Water);
            if (tariff == null) return (false, "The water rate has not been configured.");
            var rate = BillingService.RateOn(tariff, period);
            var amount = Math.Round(usageKl * rate, 2);
            var today = DateTime.Today;

            var charge = new Charge
            {
                AccountId = account.AccountId,
                RoomId = alloc.RoomId,
                ChargeType = ChargeTypes.Water,
                Description = $"Water - {usageKl:N2} kl x R{rate:N2} - {period:MMMM yyyy}",
                BillingPeriod = period,
                IssueDate = today,
                DueDate = today.AddDays(BillingService.DueDays),
                Quantity = usageKl,
                QuantityUnit = "kl",
                Rate = rate,
                Amount = amount
            };
            _db.charges.Add(charge);
            _db.waterReadings.Add(new WaterReading
            {
                AccountId = account.AccountId,
                RoomId = alloc.RoomId,
                BillingPeriod = period,
                UsageKl = usageKl,
                Rate = rate,
                Amount = amount,
                RecordedBy = recordedBy,
                Charge = charge
            });
            Notify.To(_db, customerUserId,
                $"Water usage recorded for {period:MMMM yyyy}: {usageKl:N2} kl at R{rate:N2} per kl = R{amount:N2}. Due {charge.DueDate:d MMMM yyyy}.", "info");
            await _db.SaveChangesAsync();
            return (true, $"Recorded {usageKl:N2} kl for {account.user.FullName}: R{amount:N2}.");
        }

        public async Task<(bool ok, string message)> RecordElectricityConsumptionAsync(int customerUserId, decimal units, string recordedBy, string? note)
        {
            units = Math.Round(units, 2);
            if (units <= 0 || units > 5000) return (false, "Units consumed must be between 0.01 and 5000.");
            var account = await _db.accounts.Include(a => a.user).FirstOrDefaultAsync(a => a.userId == customerUserId);
            if (account == null) return (false, "Customer account not found.");
            var alloc = await _db.roomAllocations.Where(a => a.userId == customerUserId && a.IsActive)
                .OrderBy(a => a.MoveInDate).FirstOrDefaultAsync();
            if (alloc == null) return (false, $"{account.user.FullName} has no allocated accommodation.");

            var tariff = BillingService.Find(await _billing.LoadTariffsAsync(), TariffCategories.Electricity);
            if (tariff == null) return (false, "The electricity rate has not been configured.");
            var rate = BillingService.RateOn(tariff, DateTime.Today);
            var value = Math.Round(units * rate, 2);
            var message = $"Electricity usage recorded: {units:N2} units at R{rate:N2} per unit (R{value:N2}).";

            if (account.ElectricityUnits >= units)
            {
                account.ElectricityUnits -= units;
            }
            else
            {
                var shortfall = Math.Round(units - account.ElectricityUnits, 2);
                account.ElectricityUnits = 0;
                var today = DateTime.Today;
                _db.charges.Add(new Charge
                {
                    AccountId = account.AccountId,
                    RoomId = alloc.RoomId,
                    ChargeType = ChargeTypes.Electricity,
                    Description = $"Electricity shortfall - {shortfall:N2} units x R{rate:N2} - {today:MMMM yyyy}",
                    BillingPeriod = BillingService.MonthStart(today),
                    IssueDate = today,
                    DueDate = today.AddDays(BillingService.DueDays),
                    Quantity = shortfall,
                    QuantityUnit = "units",
                    Rate = rate,
                    Amount = Math.Round(shortfall * rate, 2)
                });
                message += $" Your balance was not enough, so {shortfall:N2} units (R{Math.Round(shortfall * rate, 2):N2}) were added as a charge.";
            }

            _db.electricityTransactions.Add(new ElectricityTransaction
            {
                AccountId = account.AccountId,
                TransactionType = ElectricityTypes.Consumption,
                Units = units,
                Rate = rate,
                Amount = value,
                BalanceAfter = account.ElectricityUnits,
                RecordedBy = recordedBy,
                Note = note
            });
            Notify.To(_db, customerUserId, message + $" Balance: {account.ElectricityUnits:N2} units.", "info");
            if (account.ElectricityUnits < LowElectricityThreshold)
                Notify.To(_db, customerUserId, $"Low electricity balance: only {account.ElectricityUnits:N2} units left. Buy more units from the Payments page.", "warning");
            await _db.SaveChangesAsync();
            return (true, $"Recorded {units:N2} units for {account.user.FullName}. New balance {account.ElectricityUnits:N2} units.");
        }

        /// <summary>Import lines in the format "studentOrStaffNumber,value".</summary>
        public async Task<List<(bool ok, string message)>> ImportAsync(string kind, string? text, DateTime period, string recordedBy)
        {
            var results = new List<(bool, string)>();
            var lines = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Take(500).ToList();
            if (lines.Count == 0) { results.Add((false, "No data to import.")); return results; }
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.TrimEntries);
                if (parts.Length < 2 || !decimal.TryParse(parts[1], System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    results.Add((false, $"Line \"{line}\" is not in the format number,value."));
                    continue;
                }
                var userId = await _db.users.Where(u => u.IdentificationNumber == parts[0] && u.role.RoleName == "Customer")
                    .Select(u => (int?)u.UserId).FirstOrDefaultAsync();
                if (userId == null) { results.Add((false, $"No customer found with number {parts[0]}.")); continue; }
                results.Add(kind == "water"
                    ? await RecordWaterAsync(userId.Value, period, value, recordedBy)
                    : await RecordElectricityConsumptionAsync(userId.Value, value, recordedBy, "Imported reading"));
            }
            return results;
        }
    }
}
