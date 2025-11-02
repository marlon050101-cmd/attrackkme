using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using ServerAtrrak.Services;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TeacherController : ControllerBase
    {
        private readonly TeacherService _teacherService;

        public TeacherController(TeacherService teacherService)
        {
            _teacherService = teacherService;
        }

        [HttpGet("dashboard/{teacherId}")]
        public async Task<ActionResult<TeacherDashboardData>> GetTeacherDashboard(string teacherId)
        {
            try
            
            {
                Console.WriteLine($"DEBUG: TeacherController - Getting dashboard for teacher: {teacherId}");
                var dashboardData = await _teacherService.GetTeacherDashboardDataAsync(teacherId);
                
                if (dashboardData == null)
                {
                    Console.WriteLine($"DEBUG: TeacherController - No dashboard data found for teacher: {teacherId}");
                    return NotFound(new { message = "Teacher not found or no class assigned" });
                }

                Console.WriteLine($"DEBUG: TeacherController - Found {dashboardData.Students?.Count ?? 0} students");
                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: TeacherController - Error: {ex.Message}");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("students/{teacherId}")]
        public async Task<ActionResult<List<Student>>> GetTeacherStudents(string teacherId)
        {
            try
            {
                var students = await _teacherService.GetTeacherStudentsAsync(teacherId);
                
                if (students == null)
                {
                    return NotFound(new { message = "Teacher not found or no students assigned" });
                }

                return Ok(students);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("info/{teacherId}")]
        public async Task<ActionResult<TeacherClassInfo>> GetTeacherInfo(string teacherId)
        {
            try
            {
                var classInfo = await _teacherService.GetTeacherClassInfoAsync(teacherId);
                
                if (classInfo == null)
                {
                    return NotFound(new { message = "Teacher not found or no class assigned" });
                }

                return Ok(classInfo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("class-info/{teacherId}")]
        public async Task<ActionResult<TeacherClassInfo>> GetTeacherClassInfo(string teacherId)
        {
            try
            {
                var classInfo = await _teacherService.GetTeacherClassInfoAsync(teacherId);
                
                if (classInfo == null)
                {
                    return NotFound(new { message = "Teacher not found or no class assigned" });
                }

                return Ok(classInfo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("attendance-summary/{teacherId}")]
        public async Task<ActionResult<TeacherAttendanceSummary>> GetAttendanceSummary(string teacherId)
        {
            try
            {
                var summary = await _teacherService.GetAttendanceSummaryAsync(teacherId);
                
                if (summary == null)
                {
                    return NotFound(new { message = "Teacher not found or no class assigned" });
                }

                return Ok(summary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("student-absences/{studentId}")]
        public async Task<ActionResult<int>> GetStudentAbsences(string studentId)
        {
            try
            {
                var absences = await _teacherService.GetStudentAbsenceCountAsync(studentId);
                return Ok(new { studentId, absences });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("debug/{teacherId}")]
        public async Task<ActionResult> DebugTeacherData(string teacherId)
        {
            try
            {
                Console.WriteLine($"DEBUG: Debug endpoint called for teacher: {teacherId}");
                
                // Get teacher info
                var teacherInfo = await _teacherService.GetTeacherClassInfoAsync(teacherId);
                Console.WriteLine($"DEBUG: Teacher info: {teacherInfo != null}");
                
                // Get students
                var students = await _teacherService.GetTeacherStudentsAsync(teacherId);
                Console.WriteLine($"DEBUG: Students count: {students?.Count ?? 0}");
                
                return Ok(new { 
                    teacherInfo = teacherInfo,
                    studentsCount = students?.Count ?? 0,
                    students = students?.Take(3) // Show first 3 students
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Debug endpoint error: {ex.Message}");
                return StatusCode(500, new { message = "Debug error", error = ex.Message });
            }
        }

        [HttpGet("test")]
        public ActionResult TestEndpoint()
        {
            Console.WriteLine("DEBUG: Test endpoint called");
            return Ok(new { 
                message = "Test endpoint working",
                timestamp = DateTime.Now,
                students = new[] {
                    new { StudentId = "test-1", FullName = "TEST STUDENT 1", GradeLevel = 7, Section = "PLATO" },
                    new { StudentId = "test-2", FullName = "TEST STUDENT 2", GradeLevel = 7, Section = "PLATO" }
                }
            });
        }
    }
}
