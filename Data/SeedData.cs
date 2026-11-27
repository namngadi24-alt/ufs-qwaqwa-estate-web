using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Data
{
    public static class SeedData
    {
        public static readonly string[] RoleNames = { "Admin", "FinancialOfficer", "Customer" };

        public static void Seed(IApplicationBuilder app, IConfiguration config)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            PrepareDatabase(db, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database"));

            foreach (var roleName in RoleNames)
                if (!db.roles.Any(r => r.RoleName == roleName))
                    db.roles.Add(new Role { RoleName = roleName });
            db.SaveChanges();

            SeedTariffs(db);
            SeedAccommodation(db);

            var demo = config.GetValue<bool>("DemoAccounts:Enabled");
            SeedAdmin(db, config, demo);
            if (demo) SeedDemoData(db);
        }

        /// <summary>
        /// Applies EF migrations (the normal way to create/upgrade the database on a host).
        /// If the database was created earlier without migrations, it simply keeps using it.
        /// </summary>
        private static void PrepareDatabase(AppDbContext db, ILogger log)
        {
            if (db.Database.GetMigrations().Any())
            {
                try { db.Database.Migrate(); return; }
                catch (Exception ex) { log.LogWarning(ex, "Migrations could not be applied; falling back to EnsureCreated."); }
            }
            db.Database.EnsureCreated();
        }

        // ---------------------------------------------------------------- tariffs
        private static void SeedTariffs(AppDbContext db)
        {
            if (db.tariffs.Any()) return;
            var start = new DateTime(2026, 1, 1);
            Tariff T(string cat, string name, decimal rate, string unit, string? roomType = null) =>
                new() { Category = cat, Name = name, Rate = rate, Unit = unit, RoomType = roomType, EffectiveFrom = start, UpdatedBy = "System (initial tariff)" };
            db.tariffs.AddRange(
                T(TariffCategories.Rent, "Rent - " + RoomTypes.Single, 1850m, "per month", RoomTypes.Single),
                T(TariffCategories.Rent, "Rent - " + RoomTypes.Sharing, 1250m, "per month", RoomTypes.Sharing),
                T(TariffCategories.Rent, "Rent - " + RoomTypes.Bachelor, 2900m, "per month", RoomTypes.Bachelor),
                T(TariffCategories.Rent, "Rent - " + RoomTypes.TwoBed, 4200m, "per month", RoomTypes.TwoBed),
                T(TariffCategories.Rent, "Rent - " + RoomTypes.Townhouse, 5600m, "per month", RoomTypes.Townhouse),
                T(TariffCategories.Water, "Water", 38.50m, "per kilolitre (kl)"),
                T(TariffCategories.Electricity, "Electricity", 2.85m, "per unit (kWh)"),
                T(TariffCategories.Refuse, "Refuse collection", 85m, "per room per month"),
                T(TariffCategories.Parking, "Parking (additional vehicle)", 120m, "per additional vehicle per month"));
            db.SaveChanges();
        }

        // ---------------------------------------------------------------- accommodation
        private record RoomSpec(string Prefix, string Type, int Capacity, string Facilities, int From, int To);
        private record AccommodationSpec(string Slug, string Name, string Location, string Type, string Description, string Rules, RoomSpec[] Rooms);

        private static readonly AccommodationSpec[] AccommodationList =
        {
            new("maluti-heights", "Maluti Heights Flats", "Estate Block A, UFS QwaQwa Campus", "Flats", "Modern student flats a five-minute walk from the lecture halls. Furnished single rooms, sharing rooms and bachelor flats with fibre Wi-Fi, a study lounge and 24-hour security.", "No overnight guests without written approval. Quiet hours 22:00-06:00. Smoking is not allowed indoors. Report damage within 48 hours.", new RoomSpec[] { new("A1",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi, Shared bathroom",1,4), new("A1",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Wardrobes, Wi-Fi, Shared bathroom",5,6), new("A2",RoomTypes.Bachelor,1,"Bed, Kitchenette, Private bathroom, Wi-Fi, Desk",1,3) }),
            new("drakensberg-court", "Drakensberg Court", "Estate Block B, UFS QwaQwa Campus", "Flats", "Spacious two-bedroom flats with a kitchen and lounge, suitable for staff members or senior students sharing. Secure parking and a small communal garden.", "Maximum two adults per bedroom. No pets. Parking is free for the first vehicle; additional vehicles are charged monthly.", new RoomSpec[] { new("B",RoomTypes.TwoBed,3,"2 Bedrooms, Kitchen, Lounge, Private bathroom, Wi-Fi, Covered parking",1,6) }),
            new("setsing-townhouses", "Setsing Townhouses", "Estate Row C, UFS QwaQwa Campus", "Townhouses", "Three-bedroom townhouses with a private garden and carport, ideal for staff families. Each unit has its own water and electricity meters.", "Gardens must be kept tidy. No commercial activities. Additional vehicles are charged monthly.", new RoomSpec[] { new("T",RoomTypes.Townhouse,5,"3 Bedrooms, 2 Bathrooms, Kitchen, Lounge, Garden, Carport",1,5) }),
            new("horizon-residence", "Horizon Residence", "On Campus", "Flats", "The largest on-campus residence: bright single and sharing rooms over six floors with a study lounge on every floor, laundry and a cafeteria downstairs.", "Residence rules apply. Visitors sign in at reception until 20:00. Quiet hours 22:00-06:00.", new RoomSpec[] { new("HR",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi, Floor lounge",1,8), new("HR",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Wi-Fi, Floor lounge",9,12) }),
            new("nova-residence", "Nova Residence", "On Campus", "Flats", "A modern on-campus residence with single rooms, en-suite bachelor units and a rooftop study deck.", "No cooking in rooms except bachelor units. Quiet hours 22:00-06:00.", new RoomSpec[] { new("NV",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi",1,6), new("NV",RoomTypes.Bachelor,1,"Bed, Kitchenette, En-suite bathroom, Wi-Fi",7,10) }),
            new("qakaza", "Qakaza", "On Campus", "Flats", "Friendly mid-sized residence close to the sports fields with sharing rooms and a games room.", "Residence rules apply. No loud music after 21:00.", new RoomSpec[] { new("QK",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Wardrobes, Wi-Fi",1,6), new("QK",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi",7,10) }),
            new("campus-heights", "Campus Heights", "On Campus", "Flats", "Compact high-rise flats with views of the Maluti mountains. Bachelor units and single rooms with a communal braai area.", "No gas appliances. Balcony braais only in the braai area.", new RoomSpec[] { new("CH",RoomTypes.Bachelor,1,"Bed, Kitchenette, Private bathroom, Wi-Fi, Balcony",1,5), new("CH",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi",6,9) }),
            new("aurora-residence", "Aurora Residence", "On Campus", "Flats", "Quiet, study-focused residence with single rooms, a silent library and 24-hour security.", "Silent study hours 19:00-22:00 on weekdays.", new RoomSpec[] { new("AU",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi, Study lounge",1,8), new("AU",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Wi-Fi",9,10) }),
            new("block-d", "Block D", "Off Campus, Phuthaditjhaba", "Flats", "Affordable off-campus flats with a regular shuttle to campus. Sharing and single rooms with shared kitchens.", "Shared kitchens must be cleaned after use. Quiet hours 22:00-06:00.", new RoomSpec[] { new("BD",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Shared kitchen, Wi-Fi",1,5), new("BD",RoomTypes.Single,1,"Bed, Desk, Shared kitchen, Wi-Fi",6,9) }),
            new("block-dc", "Block DC", "Off Campus, Phuthaditjhaba", "Flats", "Neighbouring block to Block D with bachelor flats and two-bedroom flats suited to small groups.", "No pets without approval. Parking bays are numbered.", new RoomSpec[] { new("DC",RoomTypes.Bachelor,1,"Bed, Kitchenette, Private bathroom, Wi-Fi",1,4), new("DC",RoomTypes.TwoBed,3,"2 Bedrooms, Kitchen, Lounge, Wi-Fi",5,8) }),
            new("masakhane-house", "Masakhane House", "Off Campus, Setsing", "Townhouses", "A shared house feel: three-bedroom townhouses with lounges and gardens, perfect for a group of friends or a staff family.", "Tenants maintain the garden. Occupants must be registered on the lease.", new RoomSpec[] { new("MH",RoomTypes.Townhouse,5,"3 Bedrooms, 2 Bathrooms, Kitchen, Lounge, Garden",1,4) }),
            new("horizon-gardens", "Horizon Gardens", "Off Campus, Setsing", "Townhouses", "Garden townhouses in a gated complex with a children-friendly lawn and covered parking.", "Gated access hours 05:00-23:00. Visitors parking only in marked bays.", new RoomSpec[] { new("HG",RoomTypes.Townhouse,5,"3 Bedrooms, 2 Bathrooms, Kitchen, Garden, Covered parking",1,6) }),
            new("golden-gate-towers", "Golden Gate Towers", "Kestell Road, Phuthaditjhaba", "Flats", "Purpose-built student apartment tower with Wi-Fi, study rooms, a gym and lifts. Single, sharing and bachelor units.", "Key cards must be carried at all times. Quiet hours 22:00-06:00.", new RoomSpec[] { new("GT",RoomTypes.Single,1,"Bed, Desk, Wardrobe, Wi-Fi, Study room access",1,8), new("GT",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Wi-Fi",9,12), new("GT",RoomTypes.Bachelor,1,"Bed, Kitchenette, Private bathroom, Wi-Fi",13,16) }),
            new("golden-gate-lofts", "Golden Gate Lofts", "Kestell Road, Phuthaditjhaba", "Flats", "Stylish open-plan lofts for older students and staff, with high ceilings, kitchenettes and large windows.", "No sub-letting. Noise limits apply after 22:00.", new RoomSpec[] { new("GL",RoomTypes.Bachelor,1,"Open-plan loft, Kitchenette, Private bathroom, Wi-Fi",1,6), new("GL",RoomTypes.TwoBed,3,"2 Bedrooms, Kitchen, Lounge, Wi-Fi",7,9) }),
            new("mountain-view-cottages", "Mountain View Cottages", "Tseki, QwaQwa", "Townhouses", "Family-friendly cottages with mountain views, ideal for staff members. Each cottage has its own garden and fireplace.", "Fires only in the fireplace. Gardens must be kept tidy.", new RoomSpec[] { new("MV",RoomTypes.Townhouse,5,"3 Bedrooms, 2 Bathrooms, Kitchen, Lounge, Fireplace, Garden",1,5) }),
            new("sentinel-flats", "Sentinel Flats", "Tseki, QwaQwa", "Flats", "Quiet two-bedroom and bachelor flats a short drive from campus, popular with staff.", "No pets. Parking bays are numbered.", new RoomSpec[] { new("SF",RoomTypes.TwoBed,3,"2 Bedrooms, Kitchen, Lounge, Wi-Fi, Parking",1,5), new("SF",RoomTypes.Bachelor,1,"Bed, Kitchenette, Private bathroom, Wi-Fi",6,9) }),
            new("riverside-commune", "Riverside Commune", "Riverside, Phuthaditjhaba", "Flats", "Shared-living flats around a courtyard within walking distance of campus. Sharing and single rooms with communal kitchens.", "Courtyard quiet hours from 22:00. Cleaning roster applies.", new RoomSpec[] { new("RC",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Communal kitchen, Wi-Fi",1,6), new("RC",RoomTypes.Single,1,"Bed, Desk, Communal kitchen, Wi-Fi",7,12) }),
            new("elands-river-flats", "Elands River Flats", "Riverside, Phuthaditjhaba", "Flats", "Bachelor and two-bedroom flats near the river walk, with secure parking and fibre internet.", "No braais on balconies. Quiet hours 22:00-06:00.", new RoomSpec[] { new("ER",RoomTypes.Bachelor,1,"Bed, Kitchenette, Private bathroom, Wi-Fi, Parking",1,5), new("ER",RoomTypes.TwoBed,3,"2 Bedrooms, Kitchen, Lounge, Wi-Fi, Parking",6,8) }),
            new("campus-key-lodge", "Campus Key Lodge", "Mabolela, Phuthaditjhaba", "Flats", "Budget-friendly, NSFAS-accredited rooms for students: single and sharing rooms with a communal kitchen and study area.", "NSFAS-accredited. Quiet hours 22:00-06:00. Visitors leave by 21:00.", new RoomSpec[] { new("CK",RoomTypes.Sharing,2,"2 Beds, 2 Desks, Communal kitchen, Wi-Fi",1,8), new("CK",RoomTypes.Single,1,"Bed, Desk, Communal kitchen, Wi-Fi",9,14) }),
            new("kgotso-house", "Kgotso House", "Mabolela, Phuthaditjhaba", "Townhouses", "Peaceful single-storey houses (three bedrooms) with gardens - a calm option for staff and postgraduate students.", "Gardens must be kept tidy. Maximum occupants per house as per lease.", new RoomSpec[] { new("KG",RoomTypes.Townhouse,5,"3 Bedrooms, 2 Bathrooms, Kitchen, Lounge, Garden",1,4) }),
        };

        private static void SeedAccommodation(AppDbContext db)
        {
            var existing = db.accommodations.Select(a => a.AccommodationName).ToHashSet();
            foreach (var spec in AccommodationList)
            {
                if (existing.Contains(spec.Name)) continue;
                var a = new Accommodation
                {
                    AccommodationName = spec.Name, AccommodationLocation = spec.Location, AccommodationType = spec.Type,
                    Description = spec.Description, Rules = spec.Rules
                };
                db.accommodations.Add(a);
                db.SaveChanges();
                foreach (var r in spec.Rooms)
                    for (var i = r.From; i <= r.To; i++)
                        db.rooms.Add(new Room { AccommodationId = a.AccommodationId, RoomNumber = $"{r.Prefix}{i:D2}", RoomType = r.Type, Capacity = r.Capacity, Facilities = r.Facilities });
                db.accommodationPhotos.Add(new AccommodationPhoto { AccommodationId = a.AccommodationId, Path = $"/images/accommodation/{spec.Slug}-1.svg", Caption = $"{spec.Name} - exterior" });
                db.accommodationPhotos.Add(new AccommodationPhoto { AccommodationId = a.AccommodationId, Path = $"/images/accommodation/{spec.Slug}-2.svg", Caption = $"{spec.Name} - inside" });
                db.SaveChanges();
            }
        }

        // ---------------------------------------------------------------- users
        private static User AddUser(AppDbContext db, string number, string first, string last, string phone, string roleName,
            string personType, string password, bool demo, bool authorised = true)
        {
            var role = db.roles.First(r => r.RoleName == roleName);
            var user = new User
            {
                IdentificationNumber = number,
                UserName = first,
                UserSurname = last,
                UserEmail = $"{number}@ufs4life.ac.za",
                PhoneNumber = phone,
                PersonType = personType,
                RoleId = role.RoleId,
                IsAuthorised = authorised,
                IsDemo = demo
            };
            user.PassWord = new PasswordHasher<User>().HashPassword(user, password);
            db.users.Add(user);
            db.SaveChanges();
            if (roleName == "Customer")
            {
                db.accounts.Add(new Account { userId = user.UserId, AccountNumber = CustomerAccounts.NewAccountNumber(db), AccountStatus = AccountStatuses.Active });
                db.SaveChanges();
            }
            return user;
        }

        private static void SeedAdmin(AppDbContext db, IConfiguration config, bool demo)
        {
            var number = config["SeedAdmin:IdentificationNumber"];
            var password = config["SeedAdmin:Password"];
            if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(password)) return;
            if (db.users.Any(u => u.IdentificationNumber == number)) return;
            AddUser(db, number, config["SeedAdmin:FName"] ?? "System", config["SeedAdmin:Surname"] ?? "Administrator",
                config["SeedAdmin:Phone"] ?? "0580000000", "Admin", "Staff", password, demo);
        }

        private static string DemoToken(AppDbContext db)
        {
            while (true)
            {
                var digits = new char[20];
                for (var i = 0; i < 20; i++) digits[i] = (char)('0' + Random.Shared.Next(0, 10));
                if (digits[0] == '0') digits[0] = '7';
                var token = new string(digits);
                if (!db.electricityTransactions.Any(e => e.MetroToken == token) && !db.electricityTransactions.Local.Any(e => e.MetroToken == token)) return token;
            }
        }

        // ---------------------------------------------------------------- demo data
        private static void SeedDemoData(AppDbContext db)
        {
            if (db.users.Any(u => u.IdentificationNumber == "10000002")) return;

            var officer = AddUser(db, "10000002", "Lerato", "Dlamini", "0825550102", "FinancialOfficer", "Staff", "DemoOfficer@2026", true);
            var staff = AddUser(db, "10000003", "Sipho", "Nkosi", "0825550103", "Customer", "Staff", "DemoStaff@2026", true);
            var student = AddUser(db, "2024123456", "Naledi", "Khumalo", "0715550104", "Customer", "Student", "DemoStudent@2026", true);
            var student2 = AddUser(db, "2024765432", "Kabelo", "Molefe", "0715550105", "Customer", "Student", "DemoStudent2@2026", true);

            var billing = new BillingService(db, new CatalogService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
            var utilities = new UtilityService(db, billing);
            var today = DateTime.Today;
            var thisMonth = BillingService.MonthStart(today);

            AccommodationApplication Approve(User u, string roomNumber, DateTime moveIn, int vehicles, string prefType)
            {
                var room = db.rooms.First(r => r.RoomNumber == roomNumber);
                var app = new AccommodationApplication
                {
                    userId = u.UserId, PreferredRoomType = prefType, NumberOfVehicles = vehicles, PlannedMoveInDate = moveIn,
                    Status = ApplicationStatuses.Approved, DateSubmitted = moveIn.AddDays(-20).ToUniversalTime(),
                    DateDecided = moveIn.AddDays(-14).ToUniversalTime(), DecidedBy = "Lerato Dlamini", DecisionNote = "Approved.",
                    Rooms = new List<ApplicationRoom> { new() { RoomId = room.RoomId } }
                };
                db.applications.Add(app);
                db.SaveChanges();
                db.roomAllocations.Add(new RoomAllocation { userId = u.UserId, RoomId = room.RoomId, ApplicationId = app.AccommodationApplicationId, MoveInDate = moveIn });
                u.NumberOfVehicles = vehicles;
                db.SaveChanges();
                Notify.To(db, u.UserId, $"Your accommodation application {app.Reference} was approved! Room {roomNumber} has been allocated to you.", "success");
                db.SaveChanges();
                return app;
            }

            // Mid-month move-ins so the "billed at month-end" policy can be demonstrated.
            Approve(student, "A101", thisMonth.AddMonths(-2).AddDays(14), 0, RoomTypes.Single);
            Approve(staff, "T01", thisMonth.AddMonths(-3).AddDays(9), 2, RoomTypes.Townhouse);

            // Kabelo: a pending application so the officer can approve / reject it live.
            var pendingRoom = db.rooms.First(r => r.RoomNumber == "A105");
            db.applications.Add(new AccommodationApplication
            {
                userId = student2.UserId, PreferredRoomType = RoomTypes.Sharing, NumberOfVehicles = 0,
                PlannedMoveInDate = today.AddDays(20), Notes = "Prefer a ground-floor room if possible.",
                Rooms = new List<ApplicationRoom> { new() { RoomId = pendingRoom.RoomId } }
            });
            db.SaveChanges();
            Notify.To(db, student2.UserId, "Your accommodation application was submitted. You will be notified when it has been reviewed.", "success");
            db.SaveChanges();

            // Generate charges from the tariffs, then pay some of them.
            foreach (var u in new[] { student, staff }) billing.EnsureChargesAsync(u.UserId).GetAwaiter().GetResult();

            void PayCharges(User u, Func<Charge, bool> pick, string holder, string last4, DateTime? when = null)
            {
                var acc = db.accounts.First(a => a.userId == u.UserId);
                var charges = db.charges.Where(c => c.AccountId == acc.AccountId).ToList().Where(pick).Where(c => !c.IsPaid).ToList();
                if (charges.Count == 0) return;
                var payment = new Payment
                {
                    AccountId = acc.AccountId, Amount = charges.Sum(c => c.Balance), PaymentStatus = PaymentStatuses.Successful,
                    PaymentPurpose = PaymentPurposes.Bills, CardHolder = holder, CardBrand = "Visa", CardLast4 = last4, CardExpiry = "09/29",
                    DateOfPay = (when ?? DateTime.UtcNow).ToUniversalTime(),
                    GatewayTransactionReference = $"SIM-DEMO-{Random.Shared.Next(100000, 999999)}",
                    ReceiptNumber = $"UFS-RCP-DEMO-{Random.Shared.Next(1000, 9999)}"
                };
                foreach (var c in charges)
                {
                    payment.Allocations.Add(new PaymentAllocation { Charge = c, Amount = c.Balance });
                    c.AmountPaid = c.Amount;
                }
                db.payments.Add(payment);
                db.SaveChanges();
            }

            PayCharges(student, c => c.BillingPeriod < thisMonth, "NALEDI KHUMALO", "4242", today.AddDays(-20));
            PayCharges(staff, c => c.BillingPeriod < thisMonth || (c.ChargeType == ChargeTypes.Rent && c.BillingPeriod == thisMonth), "SIPHO NKOSI", "1881", today.AddDays(-8));

            // A failed payment attempt (declined card) for the history screen.
            var studentAcc = db.accounts.First(a => a.userId == student.UserId);
            db.payments.Add(new Payment
            {
                AccountId = studentAcc.AccountId, Amount = 1850m, PaymentStatus = PaymentStatuses.Failed, PaymentPurpose = PaymentPurposes.Bills,
                CardHolder = "NALEDI KHUMALO", CardBrand = "Visa", CardLast4 = "0002", CardExpiry = "09/29",
                GatewayTransactionReference = "SIM-DEMO-DECLINED", FailureReason = "The card was declined by the issuing bank.", DateOfPay = DateTime.UtcNow.AddDays(-21)
            });
            db.SaveChanges();

            // A pending payment (paid by card, waiting for the officer/admin to accept it).
            var staffAcc = db.accounts.First(a => a.userId == staff.UserId);
            var parkingCharge = db.charges.ToList().FirstOrDefault(c => c.AccountId == staffAcc.AccountId && c.ChargeType == ChargeTypes.Parking && c.BillingPeriod == thisMonth && !c.IsPaid);
            if (parkingCharge != null)
            {
                var pend = new Payment
                {
                    AccountId = staffAcc.AccountId, Amount = parkingCharge.Balance, PaymentStatus = PaymentStatuses.Pending, PaymentPurpose = PaymentPurposes.Bills,
                    CardHolder = "SIPHO NKOSI", CardBrand = "Visa", CardLast4 = "1881", CardExpiry = "09/29", DateOfPay = DateTime.UtcNow.AddHours(-3),
                    GatewayTransactionReference = $"SIM-DEMO-{Random.Shared.Next(100000, 999999)}"
                };
                pend.Allocations.Add(new PaymentAllocation { Charge = parkingCharge, Amount = parkingCharge.Balance });
                db.payments.Add(pend);
                Notify.To(db, staff.UserId, $"Payment submitted: R{pend.Amount:N2} for Parking. Status: PENDING until a Financial Officer or the Agent Head accepts it.", "warning");
                db.SaveChanges();
            }

            // Electricity purchases and consumption, water usage.
            void Buy(User u, decimal units, DateTime when)
            {
                var acc = db.accounts.First(a => a.userId == u.UserId);
                var rate = 2.85m; var amount = Math.Round(units * rate, 2);
                var pay = new Payment
                {
                    AccountId = acc.AccountId, Amount = amount, PaymentStatus = PaymentStatuses.Successful, PaymentPurpose = PaymentPurposes.Electricity,
                    CardHolder = $"{u.UserName} {u.UserSurname}".ToUpper(), CardBrand = "Mastercard", CardLast4 = "4444", CardExpiry = "11/28",
                    DateOfPay = when.ToUniversalTime(), GatewayTransactionReference = $"SIM-DEMO-{Random.Shared.Next(100000, 999999)}",
                    ReceiptNumber = $"UFS-RCP-DEMO-{Random.Shared.Next(1000, 9999)}"
                };
                acc.ElectricityUnits += units;
                db.payments.Add(pay);
                db.electricityTransactions.Add(new ElectricityTransaction
                {
                    AccountId = acc.AccountId, TransactionType = ElectricityTypes.Purchase, Units = units, Rate = rate, Amount = amount,
                    BalanceAfter = acc.ElectricityUnits, Payment = pay, Date = when.ToUniversalTime(), Note = "Prepaid purchase", MetroToken = DemoToken(db)
                });
                db.SaveChanges();
            }
            Buy(student, 50, today.AddDays(-12));
            Buy(staff, 150, today.AddDays(-10));

            var by = officer.FullName;
            var prevMonth = thisMonth.AddMonths(-1);
            utilities.RecordWaterAsync(student.UserId, prevMonth, 4.5m, by).GetAwaiter().GetResult();
            utilities.RecordWaterAsync(staff.UserId, prevMonth, 9.2m, by).GetAwaiter().GetResult();
            utilities.RecordElectricityConsumptionAsync(student.UserId, 38m, by, "Meter reading").GetAwaiter().GetResult();
            utilities.RecordElectricityConsumptionAsync(staff.UserId, 60m, by, "Meter reading").GetAwaiter().GetResult();

            // Maintenance requests in different states.
            MaintenanceRequest M(User u, string roomNo, string cat, string desc, string status, string? note)
            {
                var room = db.rooms.First(r => r.RoomNumber == roomNo);
                return new MaintenanceRequest
                {
                    userId = u.UserId, RoomId = room.RoomId, Category = cat, Description = desc, Status = status, StaffNote = note,
                    UpdatedBy = status == MaintenanceStatuses.Submitted ? null : by,
                    DateSubmitted = DateTime.UtcNow.AddDays(-6), DateUpdated = DateTime.UtcNow.AddDays(-2)
                };
            }
            db.maintenanceRequests.AddRange(
                M(student, "A101", "Plumbing / leak", "The tap in the bathroom basin keeps dripping and the water pressure is very low.", MaintenanceStatuses.InProgress, "Plumber booked for Thursday."),
                M(student, "A101", "Furniture / fittings", "The desk drawer handle is broken.", MaintenanceStatuses.Resolved, "Handle replaced."),
                M(staff, "T01", "Electrical", "The kitchen light switch sparks when switched on.", MaintenanceStatuses.Submitted, null));
            db.SaveChanges();

            // A review so the officer reviews screen has data.
            db.reviews.Add(new Review { userId = staff.UserId, RatingScore = 4, ReviewDescription = "Easy to pay online, the dashboard is clear.", DateSubmitted = DateTime.UtcNow.AddDays(-3) });
            db.SaveChanges();
        }
    }
}
