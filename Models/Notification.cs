namespace UFSWebApp.Models
{
    public class Notification
    {
        public int notificationID { get; set; }
        public string Message { get; set; }
        public DateTime DateSent { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;

        /// <summary>info, success, warning or danger - drives the pop-up colour.</summary>
        public string Level { get; set; } = "info";

        /// <summary>Set once the pop-up toast has been shown to the user.</summary>
        public bool IsToasted { get; set; } = false;

        public int? userId { get; set; }
        public User user { get; set; }
    }
}
