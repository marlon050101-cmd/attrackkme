using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    /// <summary>Per-subject attendance record: one per student per subject class per date.</summary>
    public class SubjectAttendanceRecord
    {
        public string SubjectAttendanceId { get; set; } = "";
        public string? ClassOfferingId { get; set; }
        public string? TeacherSubjectId { get; set; }
        public string StudentId { get; set; } = "";
        public string StudentName { get; set; } = "";
        public DateTime Date { get; set; }
        public string Status { get; set; } = "Present"; // Present, Absent, Late
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class MarkSubjectAttendanceRequest
    {
        [Required]
        public string TeacherSubjectId { get; set; } = "";
        [Required]
        public string StudentId { get; set; } = "";
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public string Status { get; set; } = "Present"; // Present, Absent, Late
        public string? Remarks { get; set; }
    }

    public class SubjectAttendanceBatchRequest
    {
        /// <summary>Use when class is from class_offering (advisor-created).</summary>
        public string? ClassOfferingId { get; set; }
        /// <summary>Use when class is from teachersubject (legacy).</summary>
        public string? TeacherSubjectId { get; set; }
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public List<SubjectAttendanceItem> Items { get; set; } = new();
    }

    public class SubjectAttendanceItem
    {
        public string StudentId { get; set; } = "";
        public string Status { get; set; } = "Present";
        public string? Remarks { get; set; }
    }

    public class SubjectAttendanceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>Advisor (GuidanceCounselor) for dropdowns.</summary>
    public class AdvisorInfo
    {
        public string TeacherId { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Email { get; set; }
    }
}
