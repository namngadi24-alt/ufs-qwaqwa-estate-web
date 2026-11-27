using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models.ViewModels
{
    public class ReportViewModel
    {
        public int ReportId { get; set; }

        [Required, Display(Name = "Report title")]
        public string ReportTitle { get; set; }

        [Required, Display(Name = "Findings")]
        public string Findings { get; set; }

        [Required, Display(Name = "Advice")]
        public string Advice { get; set; }

        [Display(Name = "Comments")]
        public string? Comments { get; set; }

        [Display(Name = "Additional notes / inputs")]
        public string? ReportDescription { get; set; }

        public string? Status { get; set; }
    }
}
