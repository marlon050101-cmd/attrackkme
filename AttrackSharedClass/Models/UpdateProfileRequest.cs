using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class UpdateProfileRequest
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
