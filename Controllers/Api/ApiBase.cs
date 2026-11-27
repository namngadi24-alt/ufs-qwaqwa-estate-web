using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers.Api
{
    /// <summary>Base class for the mobile (Android) REST API. Requests must send "Authorization: Bearer &lt;token&gt;".</summary>
    [ApiController]
    [Authorize(AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
    public abstract class ApiBaseController : ControllerBase
    {
        protected int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        protected string StaffName() =>
            $"{User.FindFirstValue(ClaimTypes.Name)} ({(User.IsInRole("Admin") ? "Agent Head" : "Financial Officer")})";

        protected IActionResult Fail(string message, int status = 400) => StatusCode(status, new { message });
    }

    public class CardDto
    {
        public string? CardHolder { get; set; }
        public string? CardNumber { get; set; }
        public string? Expiry { get; set; }
        public string? Cvv { get; set; }
        public CardInput ToInput() => new() { CardHolder = CardHolder, CardNumber = CardNumber, Expiry = Expiry, Cvv = Cvv };
    }

    public static class ApiMap
    {
        public static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        public static DateTime? UtcOrNull(DateTime? d) => d == null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);

        public static object User(User u) => new
        {
            userId = u.UserId, name = u.FullName, firstName = u.UserName, surname = u.UserSurname, email = u.UserEmail,
            role = u.role?.RoleName, identificationNumber = u.IdentificationNumber, phoneNumber = u.PhoneNumber,
            personType = u.PersonType, vehicles = u.NumberOfVehicles, isDemo = u.IsDemo
        };

        public static object Payment(Payment p, string? token = null) => new
        {
            paymentId = p.PaymentId, amount = p.Amount, status = p.PaymentStatus, purpose = p.PaymentPurpose,
            date = Utc(p.DateOfPay), cardBrand = p.CardBrand, cardLast4 = p.CardLast4, cardExpiry = p.CardExpiry, cardHolder = p.CardHolder,
            receiptNumber = p.ReceiptNumber, transactionReference = p.GatewayTransactionReference, failureReason = p.FailureReason,
            decidedBy = p.DecidedBy, dateDecided = UtcOrNull(p.DateDecided),
            metroToken = token, metroTokenFormatted = token == null ? null : MetroToken.Format(token),
            allocations = p.Allocations.Select(a => new { chargeId = a.ChargeId, description = a.Charge?.Description, period = a.Charge?.BillingPeriod, amount = a.Amount })
        };

        public static object Charge(Charge c) => new
        {
            chargeId = c.ChargeId, type = c.ChargeType, description = c.Description, period = c.BillingPeriod, issueDate = c.IssueDate,
            dueDate = c.DueDate, quantity = c.Quantity, quantityUnit = c.QuantityUnit, rate = c.Rate, amount = c.Amount,
            amountPaid = c.AmountPaid, balance = c.Balance, isPaid = c.IsPaid, overdue = !c.IsPaid && c.DueDate.Date < DateTime.Today
        };

        public static object Room(RoomInfo r) => new
        {
            roomId = r.Room.RoomId, roomNumber = r.Room.RoomNumber, accommodationId = r.Room.AccommodationId,
            accommodationName = r.Room.Accommodation?.AccommodationName, roomType = r.Room.RoomType, capacity = r.Room.Capacity,
            facilities = r.Room.Facilities, rent = r.Rent, status = r.Status, isAvailable = r.IsAvailable
        };

        public static object Application(AccommodationApplication a) => new
        {
            applicationId = a.AccommodationApplicationId, reference = a.Reference, status = a.Status, submitted = Utc(a.DateSubmitted),
            moveInDate = a.PlannedMoveInDate, vehicles = a.NumberOfVehicles, preferredRoomType = a.PreferredRoomType, notes = a.Notes,
            decidedBy = a.DecidedBy, dateDecided = UtcOrNull(a.DateDecided), decisionNote = a.DecisionNote,
            rooms = a.Rooms.Select(r => new { roomId = r.RoomId, roomNumber = r.Room?.RoomNumber, roomType = r.Room?.RoomType, accommodation = r.Room?.Accommodation?.AccommodationName })
        };

        public static object Maintenance(MaintenanceRequest m) => new
        {
            requestId = m.MaintenanceRequestId, category = m.Category, description = m.Description, photoUrl = m.PhotoPath, status = m.Status,
            submitted = Utc(m.DateSubmitted), updated = Utc(m.DateUpdated), staffNote = m.StaffNote, updatedBy = m.UpdatedBy,
            room = m.Room == null ? null : $"{m.Room.Accommodation?.AccommodationName} - Room {m.Room.RoomNumber}", roomId = m.RoomId
        };

        public static object Tariff(Tariff t) => new
        {
            tariffId = t.TariffId, category = t.Category, name = t.Name, roomType = t.RoomType, unit = t.Unit,
            currentRate = BillingService.RateOn(t, DateTime.Today), latestRate = t.Rate, effectiveFrom = t.EffectiveFrom,
            hasUpcomingChange = t.EffectiveFrom.Date > DateTime.Today
        };
    }
}
