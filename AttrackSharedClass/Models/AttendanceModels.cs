using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class AttendanceRequest
    {
        [Required]
        public string StudentId { get; set; } = string.Empty;
        
        public string? SubjectId { get; set; }
        
        [Required]
        public string TeacherId { get; set; } = string.Empty;
        
        public string Section { get; set; } = string.Empty;
        
        [Required]
        public string SchoolId { get; set; } = string.Empty;
        
        [Required]
        public DateTime Timestamp { get; set; }
        
        [Required]
        public string AttendanceType { get; set; } = "TimeIn";
        
        public string? Remarks { get; set; }
    }

    public class AttendanceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string Status { get; set; } = "Present";
        public string AttendanceType { get; set; } = "TimeIn";
        public string? Remarks { get; set; }
    }

    public class AttendanceRecord
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsValid { get; set; }
        public string Status { get; set; } = "Present";
        public string AttendanceType { get; set; } = "TimeIn";
        public string Message { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
    }

}
