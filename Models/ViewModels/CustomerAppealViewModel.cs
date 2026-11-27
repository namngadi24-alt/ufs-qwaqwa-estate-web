namespace UFSWebApp.Models.ViewModels
{
    public class CustomerAppealViewModel
    {
        public bool IsSuspended { get; set; }
        public string? SuspensionReason { get; set; }
        public DateTime? DateSuspended { get; set; }
        public bool HasPendingAppeal { get; set; }
        public List<Appeal> PreviousAppeals { get; set; } = new();

        public Appeal NewAppeal { get; set; } = new();
    }
}
