using System;

namespace AttrackSharedClass.Models
{
    public class AcademicPeriod
    {
        public string PeriodId { get; set; } = Guid.NewGuid().ToString();
        public string SchoolId { get; set; } = string.Empty;
        public string SchoolYear { get; set; } = string.Empty; // e.g., "2023-2024"
        public string Semester { get; set; } = string.Empty;   // e.g., "1st Semester", "Regular"
        public bool IsActive { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class CreatePeriodRequest
    {
        public string SchoolId { get; set; } = string.Empty;
        public string SchoolYear { get; set; } = string.Empty;
        public string Semester { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
