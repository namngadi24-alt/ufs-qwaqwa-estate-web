using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models.ViewModels
{
    public class SuspendAccountViewModel
    {
        public int UserId { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }

        [Required(ErrorMessage = "Please give the reason for the suspension. The customer will see it.")]
        [StringLength(1000, MinimumLength = 5, ErrorMessage = "The reason must be between 5 and 1000 characters.")]
        [Display(Name = "Reason for suspension (sent to the customer)")]
        public string Reason { get; set; }
    }
}
