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

        public string? FullName { get; set; }
        
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

        public bool IsApproved { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        
        public DateTime? LastLoginAt { get; set; }
        
        // Foreign key references
        public string? TeacherId { get; set; }
        public string? StudentId { get; set; }
        public string? SchoolId { get; set; }
    }

    public enum UserType
    {
        Admin = 1,
        /// <summary>Subject Teacher — picks class offerings created by Adviser and takes attendance.</summary>
        SubjectTeacher = 2,
        Student = 3,
        /// <summary>Guidance Counselor — monitors students, counseling records.</summary>
        GuidanceCounselor = 4,
        /// <summary>Adviser — creates section classes (subject + schedule), links students to section.</summary>
        Adviser = 5,
        /// <summary>Head — manages teachers and assigns roles.</summary>
        Head = 6
    }
}
