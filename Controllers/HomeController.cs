using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using UFSWebApp.Infrastructure;

namespace UFSWebApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly CatalogService _catalog;
        private readonly ILogger<HomeController> _logger;
        public HomeController(CatalogService catalog, ILogger<HomeController> logger) { _catalog = catalog; _logger = logger; }

        public async Task<IActionResult> Index()
        {
            var featured = (await _catalog.GetAccommodationsAsync()).Where(a => a.IsPublished).ToList();
            return View(featured);
        }

        [AllowAnonymous]
        public IActionResult Error()
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            var exception = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error;
            if (exception != null)
                _logger.LogError(exception, "Unhandled web request failure. RequestId={RequestId}", requestId);
            return View(new ErrorViewModel { RequestId = requestId });
        }
    }

    public class ErrorViewModel
    {
        public string RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
