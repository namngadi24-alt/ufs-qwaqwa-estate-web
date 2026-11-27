using System.Text.RegularExpressions;

namespace UFSWebApp.Infrastructure
{
    public class CardInput
    {
        public string? CardHolder { get; set; }
        public string? CardNumber { get; set; }
        public string? Expiry { get; set; }
        public string? Cvv { get; set; }
    }

    public record CardCheck(List<string> Errors, string Brand, string Last4, string Expiry, string Digits)
    {
        public bool IsValid => Errors.Count == 0;
    }

    public static class CardValidator
    {
        private static readonly Regex ExpiryPattern = new(@"^(0[1-9]|1[0-2])\s*/\s*(\d{2})$", RegexOptions.Compiled);
        private static readonly Regex HolderPattern = new(@"^[A-Za-z][A-Za-z '\-\.]{1,59}$", RegexOptions.Compiled);

        /// <summary>
        /// The card type is worked out from the number's LENGTH (any digits are accepted):
        /// 15 digits = American Express; 16 digits = Visa if it starts with 4, otherwise Mastercard; 13 or 19 digits = Visa.
        /// </summary>
        public static string DetectBrand(string digits) => digits.Length switch
        {
            15 => "American Express",
            16 => digits.StartsWith("4") ? "Visa" : "Mastercard",
            13 or 19 => "Visa",
            _ => "Unknown"
        };

        public static CardCheck Validate(CardInput input)
        {
            var errors = new List<string>();
            var holder = input.CardHolder?.Trim() ?? "";
            if (holder.Length == 0) errors.Add("Please enter the name on the card.");
            else if (!HolderPattern.IsMatch(holder)) errors.Add("The name on the card may only contain letters, spaces, hyphens and apostrophes.");

            var digits = new string((input.CardNumber ?? "").Where(char.IsDigit).ToArray());
            var raw = (input.CardNumber ?? "").Replace(" ", "").Replace("-", "");
            var brand = "Unknown";
            if (digits.Length == 0) errors.Add("Please enter your card number.");
            else if (digits.Length != raw.Length) errors.Add("The card number may only contain digits.");
            else if (digits.Length < 13 || digits.Length > 19) errors.Add("The card number must be between 13 and 19 digits.");
            else
            {
                brand = DetectBrand(digits);
                if (brand == "Unknown")
                    errors.Add("The card number must have 13, 15, 16 or 19 digits (15 = American Express, 16 = Visa or Mastercard, 13/19 = Visa).");
            }

            var expiryText = "";
            var m = ExpiryPattern.Match(input.Expiry?.Trim() ?? "");
            if (!m.Success) errors.Add("Enter the expiry date as MM/YY (for example 08/29).");
            else
            {
                var month = int.Parse(m.Groups[1].Value);
                var year = 2000 + int.Parse(m.Groups[2].Value);
                expiryText = $"{month:D2}/{year % 100:D2}";
                var endOfMonth = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);
                if (endOfMonth < DateTime.Today) errors.Add("This card has expired. Please use a card that has not expired.");
                else if (year > DateTime.Today.Year + 12) errors.Add("The expiry date is too far in the future.");
            }

            var cvv = input.Cvv?.Trim() ?? "";
            var cvvLength = brand == "American Express" ? 4 : 3;
            if (cvv.Length == 0) errors.Add("Please enter the security code (CVV).");
            else if (!cvv.All(char.IsDigit) || cvv.Length != cvvLength)
                errors.Add($"The security code must be {cvvLength} digits.");

            var last4 = digits.Length >= 4 ? digits[^4..] : "";
            return new CardCheck(errors, brand, last4, expiryText, digits);
        }
    }

    public sealed record GatewayResult(bool Approved, string Message, string TransactionReference);

    /// <summary>
    /// Simulated payment provider. A payment is only recorded as successful after this returns Approved.
    /// Test cards: 4000 0000 0000 0002 is declined, 4000 0000 0000 9995 has insufficient funds.
    /// </summary>
    public sealed class PaymentGatewaySimulation
    {
        public Task<GatewayResult> ChargeAsync(decimal amount, string reference, string cardDigits)
        {
            if (amount <= 0) return Task.FromResult(new GatewayResult(false, "Invalid amount.", ""));
            var tx = $"SIM-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100000, 999999)}";
            if (cardDigits == "4000000000000002")
                return Task.FromResult(new GatewayResult(false, "The card was declined by the issuing bank.", tx));
            if (cardDigits == "4000000000009995")
                return Task.FromResult(new GatewayResult(false, "The card has insufficient funds.", tx));
            return Task.FromResult(new GatewayResult(true, "Approved", tx));
        }
    }
}

namespace UFSWebApp.Infrastructure
{
    public static class MetroToken
    {
        /// <summary>Formats a 20-digit token as 5 groups of 4 digits.</summary>
        public static string Format(string? token)
        {
            var t = token ?? "";
            return t.Length == 20 ? string.Join(" ", Enumerable.Range(0, 5).Select(i => t.Substring(i * 4, 4))) : t;
        }
    }
}
