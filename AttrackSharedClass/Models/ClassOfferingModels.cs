using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    /// <summary>Class slot created by adviser: section + subject + schedule. Subject teacher picks and assigns self (TeacherId).</summary>
    public class ClassOffering
    {
        public string ClassOfferingId { get; set; } = "";
        public string AdviserId { get; set; } = "";
        public string? AdviserName { get; set; }
        public string SubjectId { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public string? SubjectCode { get; set; }
        public int GradeLevel { get; set; }
        public string Section { get; set; } = "";
        public string? Strand { get; set; }
        public TimeSpan ScheduleStart { get; set; }
        public TimeSpan ScheduleEnd { get; set; }
        /// <summary>Comma-separated days e.g. "Monday,Wednesday,Friday"</summary>
        public string DayOfWeek { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday";
        /// <summary>Set when a subject teacher assigns themselves to teach this class.</summary>
        public string? TeacherId { get; set; }
        public string? TeacherName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateClassOfferingRequest
    {
        [Required]
        public string AdviserId { get; set; } = "";
        [Required]
        public string SubjectId { get; set; } = "";
        public string? SubjectName { get; set; }
        [Required]
        [Range(7, 12)]
        public int GradeLevel { get; set; }
        [Required]
        public string Section { get; set; } = "";
        public string? Strand { get; set; }
        [Required]
        public TimeSpan ScheduleStart { get; set; }
        [Required]
        public TimeSpan ScheduleEnd { get; set; }
        /// <summary>Comma-separated days e.g. "Monday,Wednesday,Friday"</summary>
        public string DayOfWeek { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday";
    }

    public class UpdateClassOfferingRequest
    {
        public TimeSpan? ScheduleStart { get; set; }
        public TimeSpan? ScheduleEnd { get; set; }
        public string? DayOfWeek { get; set; }
        public string? SubjectName { get; set; }
        public string? SubjectId { get; set; }
    }

    public class ClassOfferingResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public ClassOffering? ClassOffering { get; set; }
    }
}
