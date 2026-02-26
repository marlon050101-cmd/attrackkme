using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class DailyAttendanceRecord
    {
        public string StudentId { get; set; } = "";
        public string StudentName { get; set; } = "";
        public DateTime Date { get; set; }
        public string TimeIn { get; set; } = "";
        public string TimeOut { get; set; } = "";
        public string Status { get; set; } = "";
        public string Remarks { get; set; } = "";
    }

    public class DailyAttendanceStatus
    {
        public string Status { get; set; } = "Not Marked";
        public string? TimeIn { get; set; }
    }

    public class DailyTimeInRequest
    {
        [Required]
        public string StudentId { get; set; } = "";
        
        [Required]
        public DateTime Date { get; set; }
        
        [Required]
        public TimeSpan TimeIn { get; set; }

        [Required]
        public string TeacherId { get; set; } = "";
    }

    public class DailyTimeInResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Status { get; set; } = "Present";
        public string TimeIn { get; set; } = "";
    }

    public class DailyTimeOutRequest
    {
        [Required]
        public string StudentId { get; set; } = "";
        
        [Required]
        public DateTime Date { get; set; }
        
        [Required]
        public TimeSpan TimeOut { get; set; }

        [Required]
        public string TeacherId { get; set; } = "";
    }

    public class DailyTimeOutResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string TimeOut { get; set; } = "";
    }

    public class SmsQueueItem
    {
        public int Id { get; set; }
        public string PhoneNumber { get; set; } = "";
        public string Message { get; set; } = "";
        public string StudentId { get; set; } = "";
        public DateTime ScheduledAt { get; set; } = DateTime.Now;
        public bool IsSent { get; set; } = false;
        public DateTime? SentAt { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
