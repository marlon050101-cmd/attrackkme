namespace AttrackSharedClass.Models
{
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Token { get; set; }
        public UserInfo? User { get; set; }
    }

    public class UserInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserType UserType { get; set; }
        public string? TeacherId { get; set; }
        public string? StudentId { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;

        // Validation fields for strict student-teacher matching
        public string? SchoolId { get; set; }
        public int? GradeLevel { get; set; }
        public string? Section { get; set; }
        public string? Strand { get; set; }
    }
}
