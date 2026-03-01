using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    /// <summary>Class slot created by advisor: section + subject + schedule. Subject teacher picks and assigns self (TeacherId).</summary>
    public class ClassOffering
    {
        public string ClassOfferingId { get; set; } = "";
        public string AdvisorId { get; set; } = "";
        public string? AdvisorName { get; set; }
        public string SubjectId { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public int GradeLevel { get; set; }
        public string Section { get; set; } = "";
        public string? Strand { get; set; }
        public TimeSpan ScheduleStart { get; set; }
        public TimeSpan ScheduleEnd { get; set; }
        /// <summary>Set when a subject teacher assigns themselves to teach this class.</summary>
        public string? TeacherId { get; set; }
        public string? TeacherName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateClassOfferingRequest
    {
        [Required]
        public string AdvisorId { get; set; } = "";
        [Required]
        public string SubjectId { get; set; } = "";
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
    }

    public class UpdateClassOfferingRequest
    {
        public TimeSpan? ScheduleStart { get; set; }
        public TimeSpan? ScheduleEnd { get; set; }
    }

    public class ClassOfferingResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public ClassOffering? ClassOffering { get; set; }
    }
}
