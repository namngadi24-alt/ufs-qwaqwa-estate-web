using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public class DashboardStats
    {
        public int TotalCustomers { get; set; }
        public int StudentCustomers { get; set; }
        public int StaffCustomers { get; set; }
        public int TotalFinancialOfficers { get; set; }
        public int ApplicationsTotal { get; set; }
        public int ApplicationsPending { get; set; }
        public int ApplicationsApproved { get; set; }
        public int ApplicationsRejected { get; set; }
        public int RoomsTotal { get; set; }
        public int RoomsOccupied { get; set; }
        public int RoomsAvailable { get; set; }
        public int RoomsUnderMaintenance { get; set; }
        public decimal TotalReceived { get; set; }
        public decimal Outstanding { get; set; }
        public int PaymentsSuccessful { get; set; }
        public int PaymentsFailed { get; set; }
        public int PaymentsPending { get; set; }
        public Dictionary<string, int> MaintenanceByStatus { get; set; } = new();
        public decimal WaterKl { get; set; }
        public decimal ElectricityConsumed { get; set; }
        public decimal ElectricityPurchased { get; set; }
        public int PendingResetRequests { get; set; }
    }

    public static class StatsService
    {
        /// <summary>Every figure is calculated from real records - nothing is sample data.</summary>
        public static async Task<DashboardStats> GetAsync(AppDbContext db)
        {
            var today = DateTime.Today;
            var s = new DashboardStats();
            s.TotalCustomers = await db.users.CountAsync(u => u.role.RoleName == "Customer");
            s.StudentCustomers = await db.users.CountAsync(u => u.role.RoleName == "Customer" && u.PersonType == "Student");
            s.StaffCustomers = await db.users.CountAsync(u => u.role.RoleName == "Customer" && u.PersonType == "Staff");
            s.TotalFinancialOfficers = await db.users.CountAsync(u => u.role.RoleName == "FinancialOfficer");

            s.ApplicationsTotal = await db.applications.CountAsync();
            s.ApplicationsPending = await db.applications.CountAsync(a => a.Status == ApplicationStatuses.Pending);
            s.ApplicationsApproved = await db.applications.CountAsync(a => a.Status == ApplicationStatuses.Approved);
            s.ApplicationsRejected = await db.applications.CountAsync(a => a.Status == ApplicationStatuses.Rejected);

            var rooms = await db.rooms.AsNoTracking().Select(r => new { r.RoomId, r.Capacity, r.IsUnderMaintenance }).ToListAsync();
            var occupancy = await db.roomAllocations.Where(a => a.IsActive).GroupBy(a => a.RoomId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            s.RoomsTotal = rooms.Count;
            s.RoomsUnderMaintenance = rooms.Count(r => r.IsUnderMaintenance);
            s.RoomsOccupied = rooms.Count(r => !r.IsUnderMaintenance && occupancy.GetValueOrDefault(r.RoomId) >= r.Capacity);
            s.RoomsAvailable = rooms.Count(r => !r.IsUnderMaintenance && occupancy.GetValueOrDefault(r.RoomId) < r.Capacity);

            s.TotalReceived = await db.payments.Where(p => p.PaymentStatus == PaymentStatuses.Successful).SumAsync(p => (decimal?)p.Amount) ?? 0m;
            s.Outstanding = await db.charges.Where(c => c.IssueDate <= today).SumAsync(c => (decimal?)(c.Amount - c.AmountPaid)) ?? 0m;
            s.PaymentsSuccessful = await db.payments.CountAsync(p => p.PaymentStatus == PaymentStatuses.Successful);
            s.PaymentsPending = await db.payments.CountAsync(p => p.PaymentStatus == PaymentStatuses.Pending);
            s.PaymentsFailed = await db.payments.CountAsync(p => p.PaymentStatus == PaymentStatuses.Failed);

            foreach (var status in MaintenanceStatuses.All)
                s.MaintenanceByStatus[status] = await db.maintenanceRequests.CountAsync(m => m.Status == status);

            s.WaterKl = await db.waterReadings.SumAsync(w => (decimal?)w.UsageKl) ?? 0m;
            s.ElectricityConsumed = await db.electricityTransactions.Where(e => e.TransactionType == ElectricityTypes.Consumption).SumAsync(e => (decimal?)e.Units) ?? 0m;
            s.ElectricityPurchased = await db.electricityTransactions.Where(e => e.TransactionType == ElectricityTypes.Purchase).SumAsync(e => (decimal?)e.Units) ?? 0m;
            s.PendingResetRequests = await db.passwordResetRequests.CountAsync(r => r.Status == ResetRequestStatuses.Pending);
            return s;
        }
    }
}
