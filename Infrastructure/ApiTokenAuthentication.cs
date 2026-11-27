using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public class ApiTokenService
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
        private readonly AppDbContext _db;

        public ApiTokenService(AppDbContext db) => _db = db;

        public static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        public async Task<string> IssueAsync(int userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            _db.apiTokens.RemoveRange(await _db.apiTokens
                .Where(t => t.userId == userId && t.ExpiresUtc < DateTime.UtcNow).ToListAsync());

            _db.apiTokens.Add(new ApiToken
            {
                userId = userId,
                TokenHash = Hash(token),
                ExpiresUtc = DateTime.UtcNow.Add(Lifetime)
            });
            await _db.SaveChangesAsync();
            return token;
        }

        public async Task RevokeAsync(string token)
        {
            var hash = Hash(token);
            var row = await _db.apiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (row == null) return;
            _db.apiTokens.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ApiToken";
        private readonly AppDbContext _db;

        public ApiTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
            : base(options, logger, encoder) => _db = db;

        public static string? ReadToken(HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header.Substring("Bearer ".Length).Trim()
                : null;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = ReadToken(Request);
            if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();

            var hash = ApiTokenService.Hash(token);
            var row = await _db.apiTokens
                .Include(t => t.user).ThenInclude(u => u!.role)
                .FirstOrDefaultAsync(t => t.TokenHash == hash);

            if (row?.user == null || row.ExpiresUtc < DateTime.UtcNow)
                return AuthenticateResult.Fail("Your session has expired. Please log in again.");

            var user = row.user;
            var role = user.role?.RoleName ?? "Customer";
            if (user.IsLockedOut)
                return AuthenticateResult.Fail("This account is locked.");
            if (role == "FinancialOfficer" && !user.IsAuthorised)
                return AuthenticateResult.Fail("Your financial officer access has been revoked.");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, $"{user.UserName} {user.UserSurname}"),
                new Claim(ClaimTypes.Email, user.UserEmail),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new { message = "Please log in again." });
        }

        protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(new { message = "You do not have permission to do this." });
        }
    }
}
