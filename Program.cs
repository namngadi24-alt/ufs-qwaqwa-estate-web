using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<OfficerAuthorisationFilter>();
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
}).AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

var appCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(appCulture);
    options.SupportedCultures = new[] { appCulture };
    options.SupportedUICultures = new[] { appCulture };
});

// Database: pooled contexts + automatic retry on transient SQL errors (good for hosted SQL Server).
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null)));

builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

// Keep sign-in cookies valid across restarts/recycles when a key folder is configured (set DataProtection:KeysPath when hosting).
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    if (!Path.IsPathRooted(keysPath)) keysPath = Path.Combine(builder.Environment.ContentRootPath, keysPath);
    builder.Services.AddDataProtection().SetApplicationName("UFSEstateAccommodation")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddSingleton<PaymentGatewaySimulation>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<UtilityService>();
builder.Services.AddScoped<TariffService>();
builder.Services.AddScoped<AccommodationService>();
builder.Services.AddScoped<FileStorage>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<ApiTokenService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        // The Free MonsterASP.NET deployment is being submitted over HTTP.
        // SameAsRequest keeps the auth cookie usable over HTTP while still
        // marking it Secure automatically if the app is later served over HTTPS.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationHandler.SchemeName, null);

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// JSON errors for the mobile API instead of an HTML error page.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex) when (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
    {
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Api").LogError(ex, "Unhandled API error");
        ctx.Response.Clear();
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new { message = "Something went wrong. Please try again." });
    }
});

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (builder.Configuration.GetValue("Hosting:UseHttpsRedirection", true))
{
    if (app.Environment.IsDevelopment())
        app.UseWhen(c => !c.Request.Path.StartsWithSegments("/api"), branch => branch.UseHttpsRedirection());
    else
        app.UseHttpsRedirection();
}

app.UseResponseCompression();
app.UseRequestLocalization();
app.UseStaticFiles(new StaticFileOptions
{
    // Browsers keep CSS, JS, fonts and pictures for a week, so pages stop re-downloading them on every click.
    OnPrepareResponse = ctx =>
    {
        var days = ctx.Context.Request.Path.StartsWithSegments("/uploads") ? 1 : 7;
        ctx.Context.Response.Headers.CacheControl = $"public,max-age={days * 86400}";
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (AppDbContext db) =>
    Results.Ok(new { status = "ok", database = await db.Database.CanConnectAsync(), time = DateTime.UtcNow }));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

SeedData.Seed(app, builder.Configuration);

// Warm the cache and the EF model so the first visitor does not wait.
try
{
    using var scope = app.Services.CreateScope();
    var catalog = scope.ServiceProvider.GetRequiredService<CatalogService>();
    await catalog.GetAccommodationsAsync();
    await catalog.GetRoomsAsync();
    await catalog.GetTariffsAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Cache warm-up skipped.");
}

app.Run();
