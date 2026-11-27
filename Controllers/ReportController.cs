using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers
{
    [Authorize(Roles = "Admin,FinancialOfficer")]
    public class ReportsController : Controller
    {
        private readonly AppDbContext _db;
        public ReportsController(AppDbContext db) => _db = db;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

        public async Task<IActionResult> Index()
        {
            IQueryable<Report> query = _db.reports.Include(r => r.user).OrderByDescending(r => r.DateGenerated);

            if (User.IsInRole("FinancialOfficer"))
                query = query.Where(r => r.userId == CurrentUserId);

            return View(await query.ToListAsync());
        }

        public async Task<IActionResult> Details(int id)
        {
            var report = await _db.reports.Include(r => r.user).FirstOrDefaultAsync(r => r.ReportId == id);
            if (report == null) return NotFound();

            if (User.IsInRole("FinancialOfficer") && report.userId != CurrentUserId)
                return Forbid();

            return View(report);
        }

        [Authorize(Roles = "FinancialOfficer,Admin")]
        [HttpGet]
        public IActionResult Create()
        {
            var snapshot = ComputeFinancialSnapshot();
            ViewBag.TotalCollected = snapshot.Collected;
            ViewBag.TotalPending = snapshot.Pending;
            ViewBag.PaymentCount = snapshot.Count;
            return View(new ReportViewModel());
        }

        [Authorize(Roles = "FinancialOfficer,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReportViewModel model, string action)
        {
            var snapshot = ComputeFinancialSnapshot();

            if (!ModelState.IsValid)
            {
                ViewBag.TotalCollected = snapshot.Collected;
                ViewBag.TotalPending = snapshot.Pending;
                ViewBag.PaymentCount = snapshot.Count;
                return View(model);
            }

            var report = new Report
            {
                ReportTitle = model.ReportTitle,
                Findings = model.Findings,
                Advice = model.Advice,
                Comments = model.Comments,
                ReportDescription = model.ReportDescription,
                TotalCollected = snapshot.Collected,
                TotalPending = snapshot.Pending,
                PaymentCount = snapshot.Count,
                userId = CurrentUserId,
                Status = action == "submit" ? "Submitted" : "Draft",
                DateSubmitted = action == "submit" ? DateTime.UtcNow : null
            };

            _db.reports.Add(report);
            await _db.SaveChangesAsync();

            TempData["Success"] = action == "submit" ? "Report submitted." : "Draft saved.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "FinancialOfficer,Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var report = await _db.reports.FirstOrDefaultAsync(r => r.ReportId == id);
            if (report == null) return NotFound();
            if (report.userId != CurrentUserId) return Forbid();

            if (report.Status == "Submitted")
            {
                TempData["Error"] = "This report has already been submitted and can no longer be edited.";
                return RedirectToAction(nameof(Details), new { id });
            }

            ViewBag.TotalCollected = report.TotalCollected;
            ViewBag.TotalPending = report.TotalPending;
            ViewBag.PaymentCount = report.PaymentCount;

            return View(new ReportViewModel
            {
                ReportId = report.ReportId,
                ReportTitle = report.ReportTitle,
                Findings = report.Findings,
                Advice = report.Advice,
                Comments = report.Comments,
                ReportDescription = report.ReportDescription,
                Status = report.Status
            });
        }

        [Authorize(Roles = "FinancialOfficer,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ReportViewModel model, string action)
        {
            var report = await _db.reports.FirstOrDefaultAsync(r => r.ReportId == model.ReportId);
            if (report == null) return NotFound();
            if (report.userId != CurrentUserId) return Forbid();

            if (report.Status == "Submitted")
            {
                TempData["Error"] = "This report has already been submitted and can no longer be edited.";
                return RedirectToAction(nameof(Details), new { id = report.ReportId });
            }

            var snapshot = ComputeFinancialSnapshot();

            if (!ModelState.IsValid)
            {
                ViewBag.TotalCollected = report.TotalCollected;
                ViewBag.TotalPending = report.TotalPending;
                ViewBag.PaymentCount = report.PaymentCount;
                return View(model);
            }

            report.ReportTitle = model.ReportTitle;
            report.Findings = model.Findings;
            report.Advice = model.Advice;
            report.Comments = model.Comments;
            report.ReportDescription = model.ReportDescription;
            report.TotalCollected = snapshot.Collected;
            report.TotalPending = snapshot.Pending;
            report.PaymentCount = snapshot.Count;

            if (action == "submit")
            {
                report.Status = "Submitted";
                report.DateSubmitted = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = action == "submit" ? "Report submitted." : "Draft updated.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "FinancialOfficer,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var report = await _db.reports.FirstOrDefaultAsync(r => r.ReportId == id);
            if (report == null) return NotFound();
            if (report.userId != CurrentUserId) return Forbid();

            if (report.Status == "Submitted")
            {
                TempData["Error"] = "Submitted reports cannot be deleted.";
                return RedirectToAction(nameof(Index));
            }

            _db.reports.Remove(report);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Draft deleted.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateSummaryReport()
        {
            var snapshot = ComputeFinancialSnapshot();

            var report = new Report
            {
                ReportTitle = $"Monthly Collections Summary - {DateTime.UtcNow:MMMM yyyy}",
                ReportDescription = "Automated system summary generated from the Admin dashboard.",
                Findings = $"Automated system summary: {snapshot.Count} payments recorded, R{snapshot.Collected:N2} collected, R{snapshot.Pending:N2} still outstanding on customer accounts.",
                Advice = "Follow up on outstanding balances and monitor payment compliance.",
                Comments = "Auto-generated from the Admin dashboard.",
                TotalCollected = snapshot.Collected,
                TotalPending = snapshot.Pending,
                PaymentCount = snapshot.Count,
                userId = CurrentUserId,
                Status = "Submitted",
                DateSubmitted = DateTime.UtcNow
            };

            _db.reports.Add(report);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Summary report generated.";
            return RedirectToAction(nameof(Index));
        }

        private (decimal Collected, decimal Pending, int Count) ComputeFinancialSnapshot()
        {
            var collected = _db.payments.Where(p => p.PaymentStatus == PaymentStatuses.Successful).Sum(p => (decimal?)p.Amount) ?? 0;
            var pending = _db.charges.Sum(c => (decimal?)(c.Amount - c.AmountPaid)) ?? 0;
            var count = _db.payments.Count();
            return (collected, pending, count);
        }
    }
}
