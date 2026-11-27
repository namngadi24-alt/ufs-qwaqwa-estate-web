using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public class PaymentOutcome
    {
        public bool Success { get; set; }
        public Payment? Payment { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public static PaymentOutcome Fail(params string[] errors) => new() { Errors = errors.ToList() };
    }

    public class PaymentService
    {
        public const decimal MinElectricityUnits = 1m;
        public const decimal MaxElectricityUnits = 1000m;
        private readonly AppDbContext _db;
        private readonly BillingService _billing;
        private readonly PaymentGatewaySimulation _gateway;

        public PaymentService(AppDbContext db, BillingService billing, PaymentGatewaySimulation gateway)
        {
            _db = db; _billing = billing; _gateway = gateway;
        }

        private async Task<(Account? account, string? error)> LoadPayerAsync(int userId)
        {
            var account = await _db.accounts.Include(a => a.user).FirstOrDefaultAsync(a => a.userId == userId);
            if (account == null) return (null, "Account not found.");
            if (account.AccountStatus != AccountStatuses.Active)
                return (null, "Your account is suspended. Please submit an appeal before making payments.");
            var hasRoom = await _db.roomAllocations.AnyAsync(a => a.userId == userId && a.IsActive);
            if (!hasRoom) return (null, "You can only make payments once you have been allocated accommodation.");
            return (account, null);
        }

        private static string NewReceipt() => $"UFS-RCP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

        private static Payment BuildPayment(Account account, decimal amount, string purpose, CardCheck card, CardInput input,
            GatewayResult gateway, bool ok, string successStatus = PaymentStatuses.Successful)
        {
            return new Payment
            {
                AccountId = account.AccountId,
                Amount = amount,
                DateOfPay = DateTime.UtcNow,
                PaymentStatus = ok ? successStatus : PaymentStatuses.Failed,
                PaymentPurpose = purpose,
                PaymentMethod = "Card",
                CardHolder = input.CardHolder?.Trim(),
                CardBrand = card.Brand,
                CardLast4 = card.Last4,
                CardExpiry = card.Expiry,
                GatewayTransactionReference = gateway.TransactionReference,
                ReceiptNumber = ok && successStatus == PaymentStatuses.Successful ? NewReceipt() : null,
                FailureReason = ok ? null : gateway.Message
            };
        }

        public async Task<PaymentOutcome> PayBillsAsync(int userId, IEnumerable<string>? selectedKeys, CardInput input)
        {
            var (account, error) = await LoadPayerAsync(userId);
            if (account == null) return PaymentOutcome.Fail(error!);

            var keys = (selectedKeys ?? Enumerable.Empty<string>()).Distinct().ToList();
            if (keys.Count == 0) return PaymentOutcome.Fail("Please select at least one charge to pay.");

            var card = CardValidator.Validate(input);
            if (!card.IsValid) return new PaymentOutcome { Errors = card.Errors };

            var payable = await _billing.GetPayableAsync(userId);
            var chosen = payable.Items.Where(i => keys.Contains(i.Key) && !i.IsPaid).ToList();
            if (chosen.Count != keys.Count)
                return PaymentOutcome.Fail("One or more selected charges are no longer available. Please refresh the page and try again.");

            var total = chosen.Sum(i => i.Amount);
            if (total <= 0) return PaymentOutcome.Fail("The total amount must be greater than zero.");

            var gateway = await _gateway.ChargeAsync(total, "bills", card.Digits);
            var payment = BuildPayment(account, total, PaymentPurposes.Bills, card, input, gateway, gateway.Approved, PaymentStatuses.Pending);

            if (!gateway.Approved)
            {
                _db.payments.Add(payment);
                Notify.To(_db, userId, $"Your payment of R{total:N2} was unsuccessful: {gateway.Message} No money was taken.", "danger");
                await _db.SaveChangesAsync();
                return new PaymentOutcome { Payment = payment, Errors = { gateway.Message } };
            }

            var outcome = new PaymentOutcome { Success = true, Payment = payment };
            var existingIds = chosen.Where(i => i.ChargeId != null).Select(i => i.ChargeId!.Value).ToList();
            var tracked = await _db.charges.Where(c => existingIds.Contains(c.ChargeId)).ToListAsync();
            var today = DateTime.Today;

            foreach (var item in chosen)
            {
                Charge charge;
                if (item.IsAdvance)
                {
                    charge = new Charge
                    {
                        AccountId = account.AccountId,
                        RoomId = item.RoomId == 0 ? null : item.RoomId,
                        ChargeType = item.ChargeType,
                        Description = item.Description,
                        BillingPeriod = item.Period,
                        IssueDate = today,
                        DueDate = today.AddDays(BillingService.DueDays),
                        Quantity = item.Quantity,
                        QuantityUnit = item.QuantityUnit,
                        Rate = item.Rate,
                        Amount = item.Amount,
                        AmountPaid = 0m,
                        IsAdvance = true
                    };
                    _db.charges.Add(charge);
                    outcome.Warnings.Add($"You had already paid {item.ChargeType} for the current month, so this payment was applied to {item.Period:MMMM yyyy}.");
                }
                else
                {
                    charge = tracked.First(c => c.ChargeId == item.ChargeId);
                }
                payment.Allocations.Add(new PaymentAllocation { Charge = charge, Amount = item.Amount });
            }

            _db.payments.Add(payment);
            Notify.To(_db, userId,
                $"Payment submitted: R{total:N2} paid by {card.Brand} card ending {card.Last4} for {string.Join(", ", chosen.Select(c => c.ChargeType).Distinct())}. " +
                "Status: PENDING until a Financial Officer or the Agent Head accepts it. You will be notified.", "warning");
            await Notify.ToStaffAsync(_db, $"A payment of R{total:N2} from {account.user.FullName} is waiting to be accepted (Payments & transactions).", "info");
            await _db.SaveChangesAsync();
            return outcome;
        }

        public async Task<PaymentOutcome> BuyElectricityAsync(int userId, decimal units, CardInput input)
        {
            var (account, error) = await LoadPayerAsync(userId);
            if (account == null) return PaymentOutcome.Fail(error!);

            units = Math.Round(units, 2);
            if (units < MinElectricityUnits || units > MaxElectricityUnits)
                return PaymentOutcome.Fail($"Please enter between {MinElectricityUnits:0} and {MaxElectricityUnits:0} units.");

            var card = CardValidator.Validate(input);
            if (!card.IsValid) return new PaymentOutcome { Errors = card.Errors };

            var tariffs = await _billing.LoadTariffsAsync();
            var tariff = BillingService.Find(tariffs, TariffCategories.Electricity);
            if (tariff == null) return PaymentOutcome.Fail("The electricity rate has not been configured yet.");
            var rate = BillingService.RateOn(tariff, DateTime.Today);
            var amount = Math.Round(units * rate, 2);

            var gateway = await _gateway.ChargeAsync(amount, "electricity", card.Digits);
            var payment = BuildPayment(account, amount, PaymentPurposes.Electricity, card, input, gateway, gateway.Approved);
            _db.payments.Add(payment);

            if (!gateway.Approved)
            {
                Notify.To(_db, userId, $"Your electricity purchase of R{amount:N2} was unsuccessful: {gateway.Message} No money was taken.", "danger");
                await _db.SaveChangesAsync();
                return new PaymentOutcome { Payment = payment, Errors = { gateway.Message } };
            }

            account.ElectricityUnits += units;
            var token = await NewMetroTokenAsync();
            payment.ReceiptNumber ??= NewReceipt();
            _db.electricityTransactions.Add(new ElectricityTransaction
            {
                AccountId = account.AccountId,
                TransactionType = ElectricityTypes.Purchase,
                Units = units,
                Rate = rate,
                Amount = amount,
                BalanceAfter = account.ElectricityUnits,
                Payment = payment,
                MetroToken = token,
                Note = "Prepaid purchase"
            });
            Notify.To(_db, userId,
                $"Electricity purchase successful: {units:N2} units at R{rate:N2} per unit (R{amount:N2}). Your Metro Token: {MetroToken.Format(token)}. New balance: {account.ElectricityUnits:N2} units.",
                "success");
            await _db.SaveChangesAsync();
            return new PaymentOutcome { Success = true, Payment = payment };
        }

        /// <summary>Generates a unique 20-digit Metro Token.</summary>
        public async Task<string> NewMetroTokenAsync()
        {
            while (true)
            {
                var chars = new char[20];
                var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
                for (var i = 0; i < 20; i++) chars[i] = (char)('0' + bytes[i] % 10);
                if (chars[0] == '0') chars[0] = '1';
                var token = new string(chars);
                if (!await _db.electricityTransactions.AnyAsync(e => e.MetroToken == token)) return token;
            }
        }

        public async Task<(bool ok, string message)> AcceptAsync(int paymentId, string decidedBy)
        {
            var payment = await _db.payments.Include(p => p.account).ThenInclude(a => a.user)
                .Include(p => p.Allocations).ThenInclude(a => a.Charge)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
            if (payment == null) return (false, "Payment not found.");
            if (payment.PaymentStatus != PaymentStatuses.Pending) return (false, "Only pending payments can be accepted.");

            foreach (var a in payment.Allocations) a.Charge.AmountPaid = Math.Min(a.Charge.Amount, a.Charge.AmountPaid + a.Amount);
            payment.PaymentStatus = PaymentStatuses.Successful;
            payment.ReceiptNumber ??= NewReceipt();
            payment.DecidedBy = decidedBy; payment.DateDecided = DateTime.UtcNow;
            Notify.To(_db, payment.account.userId,
                $"Payment successful: your payment of R{payment.Amount:N2} was accepted by {decidedBy}. Receipt {payment.ReceiptNumber}.", "success");
            await _db.SaveChangesAsync();
            return (true, $"Payment of R{payment.Amount:N2} from {payment.account.user.FullName} accepted.");
        }

        public async Task<(bool ok, string message)> RejectAsync(int paymentId, string? note, string decidedBy)
        {
            note = note?.Trim();
            if (string.IsNullOrWhiteSpace(note) || note.Length < 5) return (false, "Please give a reason for rejecting (at least 5 characters). The customer will see it.");
            var payment = await _db.payments.Include(p => p.account).ThenInclude(a => a.user)
                .Include(p => p.Allocations).ThenInclude(a => a.Charge)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
            if (payment == null) return (false, "Payment not found.");
            if (payment.PaymentStatus != PaymentStatuses.Pending) return (false, "Only pending payments can be rejected.");

            // Keep the allocations for audit/history. Since this payment is no longer pending,
            // PendingChargeIdsAsync stops reserving these charges and they remain payable.
            payment.PaymentStatus = PaymentStatuses.Failed;
            payment.FailureReason = $"Rejected by {decidedBy}: {note}";
            payment.DecidedBy = decidedBy; payment.DateDecided = DateTime.UtcNow;
            Notify.To(_db, payment.account.userId,
                $"Payment unsuccessful: your payment of R{payment.Amount:N2} was not accepted. Reason: {note} The charges are still outstanding.", "danger");
            await _db.SaveChangesAsync();
            return (true, $"Payment rejected and {payment.account.user.FullName} was notified.");
        }
    }
}
