namespace UFSWebApp.Models
{
    public class Role
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; }

        public ICollection<User> users { get; set; }
    }
}
