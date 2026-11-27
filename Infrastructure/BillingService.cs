using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public record PayableItem(
        string Key, string ChargeType, string Description, DateTime Period, decimal Amount,
        bool IsAdvance, string? Note, DateTime? DueDate, int? ChargeId, int? RoomId,
        decimal Quantity, string? QuantityUnit, decimal Rate, bool IsPaid = false);

    public class PayableResult
    {
        public List<PayableItem> Items { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Dynamic billing. Rates come from the tariff table (never fixed per customer).
    /// Policy: rent and refuse are billed per approved room each month. The first month's charges are
    /// prorated from the planned move-in date and made payable as soon as the allocation is approved.
    /// Parking is billed monthly per additional vehicle from the allocation's first month, also prorated.
    /// Water is usage based (recorded by the Financial Officer).
    /// </summary>
    public class BillingService
    {
        public const int DueDays = 14;
        private readonly AppDbContext _db;
        private readonly CatalogService _catalog;
        public BillingService(AppDbContext db, CatalogService catalog) { _db = db; _catalog = catalog; }

        public static DateTime MonthStart(DateTime d) => new DateTime(d.Year, d.Month, 1);
        public static DateTime MonthEnd(DateTime d) => MonthStart(d).AddMonths(1).AddDays(-1);

        public static decimal RateOn(Tariff t, DateTime date)
        {
            var changes = t.Changes.OrderBy(c => c.EffectiveDate).ToList();
            if (changes.Count == 0 || date.Date >= t.EffectiveFrom.Date) return t.Rate;
            var applicable = changes.LastOrDefault(c => c.EffectiveDate.Date <= date.Date);
            return applicable?.NewRate ?? changes[0].OldRate;
        }

        public async Task<List<Tariff>> LoadTariffsAsync() => await _catalog.GetTariffsAsync();

        public static Tariff? Find(List<Tariff> tariffs, string category, string? roomType = null) =>
            tariffs.FirstOrDefault(t => t.Category == category &&
                                        (category != TariffCategories.Rent || t.RoomType == roomType));

        public static string Describe(string chargeType, Room? room, DateTime period) =>
            room == null
                ? $"{chargeType} - {period:MMMM yyyy}"
                : $"{chargeType} - {room.Accommodation?.AccommodationName} Room {room.RoomNumber} - {period:MMMM yyyy}";

        public async Task<int> EnsureChargesAsync(int userId)
        {
            var account = await _db.accounts.FirstOrDefaultAsync(a => a.userId == userId);
            if (account == null) return 0;
            var user = await _db.users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return 0;

            var allocations = await _db.roomAllocations
                .Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == userId && a.IsActive).ToListAsync();
            if (allocations.Count == 0) return 0;

            var tariffs = await LoadTariffsAsync();
            var existing = await _db.charges.Where(c => c.AccountId == account.AccountId)
                .Select(c => new { c.ChargeType, c.BillingPeriod, c.RoomId }).ToListAsync();
            var keys = existing.Select(e => $"{e.ChargeType}|{e.BillingPeriod:yyyyMM}|{e.RoomId}").ToHashSet();

            var today = DateTime.Today;
            var thisMonth = MonthStart(today);
            var created = new List<Charge>();

            foreach (var alloc in allocations)
            {
                var moveIn = alloc.MoveInDate.Date;
                var first = MonthStart(moveIn);
                // Make the first month available immediately after approval, even if its planned
                // move-in date is in a future month. Do not create later months ahead of time.
                var lastRentMonth = first > thisMonth ? first : thisMonth;
                for (var m = first; m <= lastRentMonth; m = m.AddMonths(1))
                {
                    var isFirst = m == first;
                    var issue = isFirst ? today : m;

                    var days = DateTime.DaysInMonth(m.Year, m.Month);
                    var occupied = isFirst ? days - moveIn.Day + 1 : days;
                    var factor = (decimal)occupied / days;
                    var unit = isFirst ? $"days of {days} (pro-rata)" : "month";

                    foreach (var (type, category) in new[] { (ChargeTypes.Rent, TariffCategories.Rent), (ChargeTypes.Refuse, TariffCategories.Refuse) })
                    {
                        var key = $"{type}|{m:yyyyMM}|{alloc.RoomId}";
                        if (keys.Contains(key)) continue;
                        var tariff = Find(tariffs, category, alloc.Room.RoomType);
                        if (tariff == null) continue;
                        var rate = RateOn(tariff, m);
                        created.Add(new Charge
                        {
                            AccountId = account.AccountId,
                            RoomId = alloc.RoomId,
                            ChargeType = type,
                            Description = Describe(type, alloc.Room, m) + (isFirst ? " (pro-rata from move-in)" : ""),
                            BillingPeriod = m,
                            IssueDate = issue,
                            DueDate = issue.AddDays(DueDays),
                            Quantity = isFirst ? occupied : 1,
                            QuantityUnit = unit,
                            Rate = rate,
                            Amount = Math.Round(rate * factor, 2)
                        });
                        keys.Add(key);
                    }
                }
            }

            var extraVehicles = Math.Max(0, user.NumberOfVehicles - 1);
            var parking = Find(tariffs, TariffCategories.Parking);
            if (extraVehicles > 0 && parking != null)
            {
                var firstParking = MonthStart(allocations.Min(a => a.MoveInDate));
                var lastParkingMonth = firstParking > thisMonth ? firstParking : thisMonth;
                for (var m = firstParking; m <= lastParkingMonth; m = m.AddMonths(1))
                {
                    var key = $"{ChargeTypes.Parking}|{m:yyyyMM}|";
                    if (keys.Contains(key)) continue;
                    var isFirstParkingMonth = m == firstParking;
                    var days = DateTime.DaysInMonth(m.Year, m.Month);
                    var moveIn = allocations.Min(a => a.MoveInDate.Date);
                    var occupiedDays = isFirstParkingMonth ? days - moveIn.Day + 1 : days;
                    var factor = (decimal)occupiedDays / days;
                    var rate = RateOn(parking, m);
                    created.Add(new Charge
                    {
                        AccountId = account.AccountId,
                        ChargeType = ChargeTypes.Parking,
                        Description = $"Parking for {extraVehicles} additional vehicle(s) - {m:MMMM yyyy}",
                        BillingPeriod = m,
                        IssueDate = isFirstParkingMonth ? today : m,
                        DueDate = (isFirstParkingMonth ? today : m).AddDays(DueDays),
                        Quantity = extraVehicles,
                        QuantityUnit = isFirstParkingMonth ? $"additional vehicle(s), {occupiedDays} of {days} days" : "additional vehicle(s)",
                        Rate = rate,
                        Amount = Math.Round(rate * extraVehicles * factor, 2)
                    });
                    keys.Add(key);
                }
            }

            if (created.Count > 0)
            {
                _db.charges.AddRange(created);
                Notify.To(_db, userId,
                    $"{created.Count} new charge(s) totalling R{created.Sum(c => c.Amount):N2} were added to your account. Please pay before the due date.", "info");
                await _db.SaveChangesAsync();
            }
            return created.Count;
        }

        /// <summary>Sends "due soon" and "overdue" reminders once per charge.</summary>
        public async Task SendRemindersAsync(int userId)
        {
            var account = await _db.accounts.FirstOrDefaultAsync(a => a.userId == userId);
            if (account == null) return;
            var today = DateTime.Today;
            var open = await _db.charges.Where(c => c.AccountId == account.AccountId && c.IssueDate <= today && c.Amount - c.AmountPaid > 0.004m)
                .ToListAsync();
            var changed = false;
            foreach (var c in open)
            {
                if (c.DueDate.Date < today && !c.OverdueNotified)
                {
                    Notify.To(_db, userId, $"Payment overdue: {c.Description} (R{c.Balance:N2}) was due on {c.DueDate:d MMMM yyyy} and is still outstanding.", "danger");
                    c.OverdueNotified = true; changed = true;
                }
                else if (c.DueDate.Date >= today && c.DueDate.Date <= today.AddDays(3) && !c.DueSoonNotified)
                {
                    Notify.To(_db, userId, $"Payment due soon: {c.Description} (R{c.Balance:N2}) is due on {c.DueDate:d MMMM yyyy}.", "warning");
                    c.DueSoonNotified = true; changed = true;
                }
            }
            if (changed) await _db.SaveChangesAsync();
        }

        /// <summary>Amount the customer has paid by card that is still waiting for a Financial Officer / Admin to accept it.</summary>
        public async Task<decimal> PendingAmountAsync(int userId) =>
            await _db.paymentAllocations
                .Where(a => a.Payment.account.userId == userId && a.Payment.PaymentStatus == PaymentStatuses.Pending)
                .SumAsync(a => (decimal?)a.Amount) ?? 0m;

        public async Task<List<int>> PendingChargeIdsAsync(int userId) =>
            await _db.paymentAllocations
                .Where(a => a.Payment.account.userId == userId && a.Payment.PaymentStatus == PaymentStatuses.Pending)
                .Select(a => a.ChargeId).ToListAsync();

        /// <summary>Outstanding balance, not counting money already paid and waiting for acceptance.</summary>
        public async Task<decimal> OutstandingAsync(int userId)
        {
            var today = DateTime.Today;
            var total = await _db.charges
                .Where(c => c.Account.userId == userId && c.IssueDate <= today)
                .SumAsync(c => (decimal?)(c.Amount - c.AmountPaid)) ?? 0m;
            return Math.Max(0m, total - await PendingAmountAsync(userId));
        }

        public async Task<PayableResult> GetPayableAsync(int userId)
        {
            await EnsureChargesAsync(userId);
            var result = new PayableResult();
            var account = await _db.accounts.AsNoTracking().FirstOrDefaultAsync(a => a.userId == userId);
            if (account == null) return result;

            var today = DateTime.Today;
            var charges = await _db.charges.AsNoTracking()
                .Include(c => c.Room).ThenInclude(r => r.Accommodation)
                .Where(c => c.AccountId == account.AccountId && c.IssueDate <= today).ToListAsync();

            var pendingIds = await PendingChargeIdsAsync(userId);
            var thisMonth = MonthStart(today);
            foreach (var c in charges.Where(c => !pendingIds.Contains(c.ChargeId) &&
                         (!c.IsPaid || c.BillingPeriod == thisMonth))
                         .OrderBy(c => c.BillingPeriod).ThenBy(c => c.ChargeType))
            {
                if (c.IsPaid)
                {
                    result.Items.Add(new PayableItem($"paid:{c.ChargeId}", c.ChargeType, c.Description,
                        c.BillingPeriod, c.Amount, false, "Paid for this month", null, c.ChargeId,
                        c.RoomId, c.Quantity, c.QuantityUnit, c.Rate, true));
                    continue;
                }
                var overdue = c.DueDate.Date < today;
                result.Items.Add(new PayableItem($"c:{c.ChargeId}", c.ChargeType, c.Description, c.BillingPeriod, c.Balance,
                    false, overdue ? $"Overdue since {c.DueDate:d MMM yyyy}" : $"Due {c.DueDate:d MMM yyyy}",
                    c.DueDate, c.ChargeId, c.RoomId, c.Quantity, c.QuantityUnit, c.Rate));
            }

            // Advance (next month) options appear only when this month's recurring charge is already paid.
            var user = await _db.users.AsNoTracking().FirstAsync(u => u.UserId == userId);
            var allocations = await _db.roomAllocations.AsNoTracking()
                .Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == userId && a.IsActive).ToListAsync();
            var tariffs = await LoadTariffsAsync();
            var next = thisMonth.AddMonths(1);

            foreach (var alloc in allocations)
            {
                foreach (var (type, category, code) in new[]
                         { (ChargeTypes.Rent, TariffCategories.Rent, "rent"), (ChargeTypes.Refuse, TariffCategories.Refuse, "refuse") })
                {
                    var current = charges.FirstOrDefault(c => c.ChargeType == type && c.RoomId == alloc.RoomId && c.BillingPeriod == thisMonth);
                    if (current == null || !current.IsPaid) continue;
                    if (charges.Any(c => c.ChargeType == type && c.RoomId == alloc.RoomId && c.BillingPeriod == next)) continue;
                    var tariff = Find(tariffs, category, alloc.Room.RoomType);
                    if (tariff == null) continue;
                    var rate = RateOn(tariff, next);
                    result.Items.Add(new PayableItem($"a:{code}:{alloc.RoomId}:{next:yyyy-MM}", type,
                        Describe(type, alloc.Room, next) + " (in advance)", next, Math.Round(rate, 2), true,
                        $"Already paid for {thisMonth:MMMM yyyy} - this will be for {next:MMMM yyyy}", null, null, alloc.RoomId, 1, "month", rate));
                    result.Warnings.Add($"You have already paid {type} for {alloc.Room.Label} for {thisMonth:MMMM yyyy}. Paying again will be applied to {next:MMMM yyyy}.");
                }
            }

            var extra = Math.Max(0, user.NumberOfVehicles - 1);
            var parking = Find(tariffs, TariffCategories.Parking);
            if (extra > 0 && parking != null)
            {
                var current = charges.FirstOrDefault(c => c.ChargeType == ChargeTypes.Parking && c.BillingPeriod == thisMonth);
                if (current != null && current.IsPaid &&
                    !charges.Any(c => c.ChargeType == ChargeTypes.Parking && c.BillingPeriod == next))
                {
                    var rate = RateOn(parking, next);
                    result.Items.Add(new PayableItem($"a:parking:0:{next:yyyy-MM}", ChargeTypes.Parking,
                        $"Parking for {extra} additional vehicle(s) - {next:MMMM yyyy} (in advance)", next, Math.Round(rate * extra, 2), true,
                        $"Already paid for {thisMonth:MMMM yyyy} - this will be for {next:MMMM yyyy}", null, null, null, extra, "additional vehicle(s)", rate));
                    result.Warnings.Add($"You have already paid Parking for {thisMonth:MMMM yyyy}. Paying again will be applied to {next:MMMM yyyy}.");
                }
            }
            return result;
        }
    }
}
