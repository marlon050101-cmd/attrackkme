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

        [HttpGet("pending/{schoolId}")]
        public async Task<ActionResult<List<PendingTeacherInfo>>> GetPendingTeachers(string schoolId)
        {
            try
            {
                var pendingTeachers = await _teacherService.GetPendingTeachersAsync(schoolId);
                return Ok(pendingTeachers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // Resolves SchoolId from TeacherId, then returns pending teachers.
        // Use this when the Head's session SchoolId is null.
        [HttpGet("pending-by-teacher/{teacherId}")]
        public async Task<ActionResult<List<PendingTeacherInfo>>> GetPendingTeachersByTeacher(string teacherId)
        {
            try
            {
                using var connection = await _teacherService.GetDebugConnectionAsync();
                var schoolIdQuery = "SELECT SchoolId FROM teacher WHERE TeacherId = @teacherId LIMIT 1";
                string? schoolId = null;
                using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(schoolIdQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@teacherId", teacherId);
                    var result = await cmd.ExecuteScalarAsync();
                    schoolId = result?.ToString();
                }
                Console.WriteLine($"DEBUG pending-by-teacher: teacherId={teacherId} -> schoolId={schoolId}");

                if (string.IsNullOrEmpty(schoolId))
                    return NotFound(new { message = "School not found for this teacher" });

                var pendingTeachers = await _teacherService.GetPendingTeachersAsync(schoolId);
                return Ok(pendingTeachers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("debug-pending/{schoolId}")]
        public async Task<ActionResult> DebugPendingTeachers(string schoolId)
        {
            try
            {
                // Returns ALL pending teachers in the DB (no school filter) + head school info
                // Use this to diagnose SchoolId mismatches
                using var connection = await _teacherService.GetDebugConnectionAsync();
                
                // Get head's school info
                var headSchoolQuery = "SELECT SchoolId, SchoolName FROM school WHERE SchoolId = @schoolId LIMIT 1";
                object? headSchoolName = null;
                using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(headSchoolQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@schoolId", schoolId);
                    headSchoolName = await cmd.ExecuteScalarAsync();
                }

                // Get ALL pending teachers with school info
                var allPendingQuery = @"
                    SELECT u.UserId, u.Username, u.Email, u.UserType, u.IsApproved, u.IsActive,
                           t.TeacherId, t.SchoolId as TeacherSchoolId, s.SchoolName as TeacherSchoolName
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    LEFT JOIN school s ON t.SchoolId = s.SchoolId
                    WHERE u.IsApproved = 0 AND (u.UserType = 'Teacher' OR u.UserType = 'SubjectTeacher')
                    LIMIT 50";

                var allPending = new System.Collections.Generic.List<object>();
                using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(allPendingQuery, connection))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var schoolIdOrdinal = reader.GetOrdinal("TeacherSchoolId");
                        var schoolNameOrdinal = reader.GetOrdinal("TeacherSchoolName");
                        var teacherSchoolId = reader.IsDBNull(schoolIdOrdinal) ? null : reader.GetString(schoolIdOrdinal);
                        var teacherSchoolName = reader.IsDBNull(schoolNameOrdinal) ? null : reader.GetString(schoolNameOrdinal);
                        allPending.Add(new
                        {
                            UserId = reader.GetString(reader.GetOrdinal("UserId")),
                            Username = reader.GetString(reader.GetOrdinal("Username")),
                            UserType = reader.GetString(reader.GetOrdinal("UserType")),
                            TeacherSchoolId = teacherSchoolId,
                            TeacherSchoolName = teacherSchoolName,
                            MatchesHeadSchool = teacherSchoolId == schoolId
                        });
                    }
                }

                return Ok(new
                {
                    HeadSchoolId = schoolId,
                    HeadSchoolName = headSchoolName,
                    AllPendingTeachersInDB = allPending,
                    TotalPendingInDB = allPending.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Debug error", error = ex.Message, stack = ex.StackTrace });
            }
        }

        [HttpPut("approve/{userId}")]
        public async Task<ActionResult> ApproveTeacher(string userId)
        {
            try
            {
                var result = await _teacherService.ApproveTeacherAsync(userId);
                if (result)
                    return Ok(new { message = "Teacher approved successfully" });
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
    }

    public class UpdateFullNameRequest
    {
        public string FullName { get; set; } = string.Empty;
    }
}
