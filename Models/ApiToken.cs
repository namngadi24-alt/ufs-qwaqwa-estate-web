namespace UFSWebApp.Models
{
    public class ApiToken
    {
        public int ApiTokenId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresUtc { get; set; }

        public int userId { get; set; }
        public User? user { get; set; }
    }
}
