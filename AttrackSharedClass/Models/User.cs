using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class User
    {
        [Key]
        public string UserId { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(100)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(100)]
        public string Password { get; set; } = string.Empty;
        
        [Required]
        public UserType UserType { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        
        public DateTime? LastLoginAt { get; set; }
        
        // Foreign key references
        public string? TeacherId { get; set; }
        public string? StudentId { get; set; }
    }

    public enum UserType
    {
        Admin = 1,
        Teacher = 2,
        Student = 3,
        GuidanceCounselor = 4
    }
}
