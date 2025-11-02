namespace AttrackSharedClass.Models
{
    public class StudentInfo
    {
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string Section { get; set; } = string.Empty;
        public string? Strand { get; set; }
        public string SchoolId { get; set; } = string.Empty;
        public string ParentsNumber { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AttendanceSummary
    {
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string Section { get; set; } = string.Empty;
        public string? Strand { get; set; }
        public string Gender { get; set; } = string.Empty;
        public int TotalDays { get; set; }
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public int LateDays { get; set; }
        public double AttendanceRate { get; set; }
        public DateTime? FirstAbsentDate { get; set; }
        public DateTime? LastAbsentDate { get; set; }
        public string AbsentDates { get; set; } = string.Empty;
    }

    public class GuidanceDashboardData
    {
        public int TotalStudents { get; set; }
        public int FlaggedStudents { get; set; }
        public int GradeLevelsAffected { get; set; }
        public int SectionsMonitored { get; set; }
        public List<AttendanceSummary> StudentsAtRisk { get; set; } = new();
        public List<StudentInfo> AllStudents { get; set; } = new();
    }
}
