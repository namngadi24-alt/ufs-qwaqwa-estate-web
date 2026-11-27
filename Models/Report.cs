namespace UFSWebApp.Models
{
    public class Report
    {
        public int ReportId { get; set; }
        public string ReportTitle { get; set; }

        public string Findings { get; set; }
        public string Advice { get; set; }
        public string Comments { get; set; }

        public string ReportDescription { get; set; }

        public string Status { get; set; } = "Draft";

        public decimal TotalCollected { get; set; }
        public decimal TotalPending { get; set; }
        public int PaymentCount { get; set; }

        public DateTime DateGenerated { get; set; } = DateTime.UtcNow;
        public DateTime? DateSubmitted { get; set; }

        public int userId { get; set; }
        public User user { get; set; }

    }
}
