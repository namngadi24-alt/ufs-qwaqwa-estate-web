using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public class Review
    {
        public int ReviewId { get; set; }

        [Required(ErrorMessage = "Please share your feedback.")]
        public string ReviewDescription { get; set; }

        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
        public int RatingScore { get; set; }
        public DateTime DateSubmitted { get; set; }

        public int userId { get; set; }

        public User? user { get; set; }
    }
}
