using System;
using System.ComponentModel.DataAnnotations;

namespace AttrackSharedClass.Models
{
    public class StudentWalkInLog
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Purpose is required.")]
        [StringLength(100)]
        public string Purpose { get; set; } = string.Empty;

        [StringLength(100)]
        public string PersonToVisit { get; set; } = string.Empty;

        public DateTime TimeIn { get; set; } = DateTime.Now;

        public DateTime? TimeOut { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Inside"; // Inside, Completed, etc.
    }
}
