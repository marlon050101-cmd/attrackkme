using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class TeacherDashboardData
    {
        public UserInfo? TeacherInfo { get; set; }
        public List<Student> Students { get; set; } = new();
        public List<Student> StudentsAtRisk { get; set; } = new();
        public int TotalStudents { get; set; }
        public int StudentsAtRiskCount { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public TeacherClassInfo? ClassInfo { get; set; }
    }

    public class TeacherClassInfo
    {
        public string TeacherId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string Section { get; set; } = string.Empty;
        public string SchoolId { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class StudentAttendanceInfo
    {
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public int AbsentDays { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime LastAttendance { get; set; }
        public bool IsPresent { get; set; }
    }

    public class TeacherAttendanceSummary
    {
        public int TotalStudents { get; set; }
        public int PresentToday { get; set; }
        public int AbsentToday { get; set; }
        public int StudentsAtRisk { get; set; }
        public double AttendanceRate { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
    }
}
