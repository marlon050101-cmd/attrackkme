using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class TeacherSubjectAssignment
    {
        public string TeacherSubjectId { get; set; } = string.Empty;
        
        [Required]
        public string TeacherId { get; set; } = string.Empty;
        
        [Required]
        public string SubjectId { get; set; } = string.Empty;
        
        public string SubjectName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string? Strand { get; set; }
        public string? Section { get; set; }
        /// <summary>TeacherId of the advisor (GuidanceCounselor) for this class.</summary>
        public string? AdvisorId { get; set; }
        public string? AdvisorName { get; set; }
        public TimeSpan ScheduleStart { get; set; }
        public TimeSpan ScheduleEnd { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TeacherSubjectRequest
    {
        [Required]
        public string TeacherId { get; set; } = string.Empty;
        
        [Required]
        public string SubjectId { get; set; } = string.Empty;
        
        [Required]
        public TimeSpan ScheduleStart { get; set; }
        
        [Required]
        public TimeSpan ScheduleEnd { get; set; }
        
        [Required]
        public string Section { get; set; } = string.Empty;
        /// <summary>Optional. TeacherId of the advisor (GuidanceCounselor) for this class.</summary>
        public string? AdvisorId { get; set; }
        public int GradeLevel { get; set; }
        public string? Strand { get; set; }
    }

    public class TeacherSubjectResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public TeacherSubjectAssignment? Assignment { get; set; }
    }

    public class SubjectFilter
    {
        public int? GradeLevel { get; set; }
        public string? Strand { get; set; }
        public string? SearchTerm { get; set; }
    }

    public class TeacherInfo
    {
        public string TeacherId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public string SchoolId { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string Section { get; set; } = string.Empty;
        public string? Strand { get; set; }
    }

    public class NewSubjectRequest
    {
        [Required(ErrorMessage = "Subject name is required")]
        [StringLength(100, ErrorMessage = "Subject name cannot exceed 100 characters")]
        public string SubjectName { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Grade level is required")]
        [Range(7, 12, ErrorMessage = "Grade level must be between 7 and 12")]
        public int GradeLevel { get; set; }
        
        [StringLength(10, ErrorMessage = "Strand cannot exceed 10 characters")]
        public string? Strand { get; set; }
        
        [Required(ErrorMessage = "Schedule start time is required")]
        public TimeSpan ScheduleStart { get; set; }
        
        [Required(ErrorMessage = "Schedule end time is required")]
        public TimeSpan ScheduleEnd { get; set; }
    }

    public class StudentSubjectInfo
    {
        public string SubjectId { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string? Strand { get; set; }
        public TimeSpan ScheduleStart { get; set; }
        public TimeSpan ScheduleEnd { get; set; }
        public string? TeacherName { get; set; }
        public string? TeacherId { get; set; }
        public bool HasTeacher { get; set; }
    }

    public class SubjectSectionInfo
    {
        public string SectionId { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public int StudentCount { get; set; }
        public string SubjectId { get; set; } = string.Empty;
    }

    public class StudentDisplayInfo
    {
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string Section { get; set; } = string.Empty;
        public string? Strand { get; set; }
        public string ParentsNumber { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public string? QRCodeData { get; set; }
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
