using Microsoft.EntityFrameworkCore;
using UFSWebApp.Models;

namespace UFSWebApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Accommodation> accommodations { get; set; }
        public DbSet<AccommodationPhoto> accommodationPhotos { get; set; }
        public DbSet<Room> rooms { get; set; }
        public DbSet<RoomAllocation> roomAllocations { get; set; }
        public DbSet<ResidenceExitRequest> residenceExitRequests { get; set; }
        public DbSet<AccommodationApplication> applications { get; set; }
        public DbSet<ApplicationRoom> applicationRooms { get; set; }
        public DbSet<Account> accounts { get; set; }
        public DbSet<Tariff> tariffs { get; set; }
        public DbSet<TariffChange> tariffChanges { get; set; }
        public DbSet<Charge> charges { get; set; }
        public DbSet<Payment> payments { get; set; }
        public DbSet<PaymentAllocation> paymentAllocations { get; set; }
        public DbSet<ElectricityTransaction> electricityTransactions { get; set; }
        public DbSet<WaterReading> waterReadings { get; set; }
        public DbSet<MaintenanceRequest> maintenanceRequests { get; set; }
        public DbSet<PasswordResetRequest> passwordResetRequests { get; set; }
        public DbSet<Notification> notifications { get; set; }
        public DbSet<Report> reports { get; set; }
        public DbSet<Review> reviews { get; set; }
        public DbSet<Role> roles { get; set; }
        public DbSet<User> users { get; set; }
        public DbSet<Appeal> appeals { get; set; }
        public DbSet<ApiToken> apiTokens { get; set; }

        public override int SaveChanges()
        {
            EnsurePersonTypes();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnsurePersonTypes();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            EnsurePersonTypes();
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EnsurePersonTypes();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void EnsurePersonTypes()
        {
            foreach (var entry in ChangeTracker.Entries<User>()
                         .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                if (string.IsNullOrWhiteSpace(entry.Entity.PersonType))
                    entry.Entity.PersonType = "Staff";
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Tariff>().Property(t => t.Rate).HasColumnType("decimal(18,2)");
            builder.Entity<TariffChange>().Property(t => t.OldRate).HasColumnType("decimal(18,2)");
            builder.Entity<TariffChange>().Property(t => t.NewRate).HasColumnType("decimal(18,2)");
            builder.Entity<Charge>().Property(c => c.Quantity).HasColumnType("decimal(18,2)");
            builder.Entity<Charge>().Property(c => c.Rate).HasColumnType("decimal(18,2)");
            builder.Entity<Charge>().Property(c => c.Amount).HasColumnType("decimal(18,2)");
            builder.Entity<Charge>().Property(c => c.AmountPaid).HasColumnType("decimal(18,2)");
            builder.Entity<Charge>().Ignore(c => c.Balance).Ignore(c => c.IsPaid);
            builder.Entity<Payment>().Property(p => p.Amount).HasColumnType("decimal(18,2)");
            builder.Entity<PaymentAllocation>().Property(p => p.Amount).HasColumnType("decimal(18,2)");
            builder.Entity<Account>().Property(a => a.ElectricityUnits).HasColumnType("decimal(18,2)");
            builder.Entity<ElectricityTransaction>().Property(e => e.Units).HasColumnType("decimal(18,2)");
            builder.Entity<ElectricityTransaction>().Property(e => e.Rate).HasColumnType("decimal(18,2)");
            builder.Entity<ElectricityTransaction>().Property(e => e.Amount).HasColumnType("decimal(18,2)");
            builder.Entity<ElectricityTransaction>().Property(e => e.BalanceAfter).HasColumnType("decimal(18,2)");
            builder.Entity<ElectricityTransaction>().Property(e => e.MetroToken).HasMaxLength(20);
            builder.Entity<ElectricityTransaction>().HasIndex(e => e.MetroToken).IsUnique().HasFilter("[MetroToken] IS NOT NULL");
            builder.Entity<WaterReading>().Property(w => w.UsageKl).HasColumnType("decimal(18,2)");
            builder.Entity<WaterReading>().Property(w => w.Rate).HasColumnType("decimal(18,2)");
            builder.Entity<WaterReading>().Property(w => w.Amount).HasColumnType("decimal(18,2)");
            builder.Entity<Report>().Property(r => r.TotalCollected).HasColumnType("decimal(18,2)");
            builder.Entity<Report>().Property(r => r.TotalPending).HasColumnType("decimal(18,2)");
            builder.Entity<User>().Ignore(u => u.FullName);
            builder.Entity<Room>().Ignore(r => r.Label);
            builder.Entity<AccommodationApplication>().Ignore(a => a.Reference);

            builder.Entity<Account>().HasIndex(a => a.AccountNumber).IsUnique();
            builder.Entity<Account>().HasIndex(a => a.userId).IsUnique();
            builder.Entity<User>().HasIndex(u => u.UserEmail).IsUnique();
            builder.Entity<User>().HasIndex(u => u.IdentificationNumber).IsUnique();
            builder.Entity<Room>().HasIndex(r => new { r.AccommodationId, r.RoomNumber }).IsUnique();

            builder.Entity<User>().HasOne(u => u.role).WithMany(r => r.users)
                .HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Room>().HasOne(r => r.Accommodation).WithMany(a => a.Rooms)
                .HasForeignKey(r => r.AccommodationId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<AccommodationPhoto>().HasOne(p => p.Accommodation).WithMany(a => a.Photos)
                .HasForeignKey(p => p.AccommodationId).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RoomAllocation>().HasOne(a => a.user).WithMany(u => u.Allocations)
                .HasForeignKey(a => a.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<RoomAllocation>().HasOne(a => a.Room).WithMany(r => r.Allocations)
                .HasForeignKey(a => a.RoomId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<RoomAllocation>().HasOne(a => a.Application).WithMany()
                .HasForeignKey(a => a.ApplicationId).OnDelete(DeleteBehavior.NoAction);

            builder.Entity<ResidenceExitRequest>().HasOne(r => r.RoomAllocation).WithMany()
                .HasForeignKey(r => r.RoomAllocationId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ResidenceExitRequest>().HasOne(r => r.user).WithMany()
                .HasForeignKey(r => r.userId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ResidenceExitRequest>().HasIndex(r => new { r.RoomAllocationId, r.Status });

            builder.Entity<AccommodationApplication>().HasOne(a => a.user).WithMany(u => u.Applications)
                .HasForeignKey(a => a.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<ApplicationRoom>().HasOne(a => a.Application).WithMany(a => a.Rooms)
                .HasForeignKey(a => a.AccommodationApplicationId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<ApplicationRoom>().HasOne(a => a.Room).WithMany()
                .HasForeignKey(a => a.RoomId).OnDelete(DeleteBehavior.Restrict);

            builder.Entity<TariffChange>().HasOne(c => c.Tariff).WithMany(t => t.Changes)
                .HasForeignKey(c => c.TariffId).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Account>().HasOne(a => a.user).WithMany(u => u.accounts)
                .HasForeignKey(a => a.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Charge>().HasOne(c => c.Account).WithMany(a => a.Charges)
                .HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Charge>().HasOne(c => c.Room).WithMany()
                .HasForeignKey(c => c.RoomId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<Payment>().HasOne(p => p.account).WithMany(a => a.payments)
                .HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentAllocation>().HasOne(p => p.Payment).WithMany(p => p.Allocations)
                .HasForeignKey(p => p.PaymentId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentAllocation>().HasOne(p => p.Charge).WithMany(c => c.Allocations)
                .HasForeignKey(p => p.ChargeId).OnDelete(DeleteBehavior.NoAction);

            builder.Entity<ElectricityTransaction>().HasOne(e => e.Account).WithMany()
                .HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<ElectricityTransaction>().HasOne(e => e.Payment).WithMany()
                .HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<WaterReading>().HasOne(w => w.Account).WithMany()
                .HasForeignKey(w => w.AccountId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<WaterReading>().HasOne(w => w.Room).WithMany()
                .HasForeignKey(w => w.RoomId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<WaterReading>().HasOne(w => w.Charge).WithMany()
                .HasForeignKey(w => w.ChargeId).OnDelete(DeleteBehavior.NoAction);

            builder.Entity<MaintenanceRequest>().HasOne(m => m.user).WithMany(u => u.MaintenanceRequests)
                .HasForeignKey(m => m.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<MaintenanceRequest>().HasOne(m => m.Room).WithMany()
                .HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PasswordResetRequest>().HasOne(r => r.user).WithMany()
                .HasForeignKey(r => r.userId).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Appeal>().HasOne(a => a.user).WithMany(u => u.appeals)
                .HasForeignKey(a => a.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Notification>().HasOne(n => n.user).WithMany(u => u.notifications)
                .HasForeignKey(n => n.userId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<Review>().HasOne(r => r.user).WithMany(u => u.reviews)
                .HasForeignKey(r => r.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<ApiToken>().HasIndex(t => t.TokenHash).IsUnique();
            builder.Entity<ApiToken>().Property(t => t.TokenHash).HasMaxLength(64);
            builder.Entity<ApiToken>().HasOne(t => t.user).WithMany()
                .HasForeignKey(t => t.userId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Report>().HasOne(r => r.user).WithMany(u => u.report)
                .HasForeignKey(r => r.userId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
