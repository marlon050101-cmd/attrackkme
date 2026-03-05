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
                    students = students?.Take(3)
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Debug endpoint error: {ex.Message}");
                return StatusCode(500, new { message = "Debug error", error = ex.Message });
            }
        }

        [HttpPut("update-fullname/{teacherId}")]
        public async Task<ActionResult> UpdateTeacherFullName(string teacherId, [FromBody] UpdateFullNameRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.FullName))
                {
                    return BadRequest(new { message = "FullName cannot be empty" });
                }

                var result = await _teacherService.UpdateTeacherFullNameAsync(teacherId, request.FullName);
                
                if (result)
                {
                    return Ok(new { message = "Teacher FullName updated successfully", fullName = request.FullName });
                }
                else
                {
                    return NotFound(new { message = "Teacher not found" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Error updating teacher FullName: {ex.Message}");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }


        [HttpGet("pending/{schoolId}")]
        public async Task<ActionResult<List<PendingTeacherInfo>>> GetPendingTeachers(string schoolId)
        {
            try
            {
                var pendingTeachers = await _teacherService.GetPendingTeachersAsync(schoolId?.Trim() ?? "");
                return Ok(pendingTeachers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("debug-pending/{schoolId}")]
        public async Task<ActionResult> GetPendingDiagnostics(string schoolId)
        {
            try
            {
                var diagnostics = await _teacherService.GetTeacherApprovalDiagnosticsAsync(schoolId?.Trim() ?? "");
                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Diagnostic error", error = ex.Message });
            }
        }

        [HttpPut("approve/{userId}")]
        public async Task<ActionResult> ApproveTeacher(string userId, [FromQuery] UserType role)
        {
            try
            {
                var result = await _teacherService.ApproveTeacherAsync(userId, role);
                if (result)
                    return Ok(new { message = "Teacher approved and role assigned successfully" });
                return NotFound(new { message = "Teacher not found or already approved" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("head-stats/{schoolId}")]
        public async Task<ActionResult> GetHeadStats(string schoolId)
        {
            try
            {
                var stats = await _teacherService.GetHeadStatsAsync(schoolId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }
        [HttpGet("all/{schoolId}")]
        public async Task<ActionResult<List<PendingTeacherInfo>>> GetAllTeachers(string schoolId)
        {
            try
            {
                var teachers = await _teacherService.GetAllTeachersBySchoolAsync(schoolId?.Trim() ?? "");
                return Ok(teachers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        [HttpPut("update-info/{userId}")]
        public async Task<ActionResult> UpdateTeacher(string userId, [FromBody] UpdateTeacherRequest request)
        {
            try
            {
                var result = await _teacherService.UpdateTeacherAsync(userId, request);
                if (result)
                {
                    return Ok(new { message = "Teacher information updated successfully" });
                }
                return NotFound(new { message = "Teacher not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }
    }

    public class UpdateFullNameRequest
    {
        public string FullName { get; set; } = string.Empty;
    }
}
