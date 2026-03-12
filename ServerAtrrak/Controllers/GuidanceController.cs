using Microsoft.AspNetCore.Mvc;
using ServerAtrrak.Services;
using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GuidanceController : ControllerBase
    {
        private readonly IGuidanceService _guidanceService;
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<GuidanceController> _logger;

        public GuidanceController(IGuidanceService guidanceService, Dbconnection dbConnection, ILogger<GuidanceController> logger)
        {
            _guidanceService = guidanceService;
            _dbConnection = dbConnection;
            _logger = logger;
        }

        [HttpGet("dashboard/{userId}")]
        public async Task<ActionResult<GuidanceDashboardData>> GetDashboardData(string userId, [FromQuery] int days = 30)
        {
            try
            {
                _logger.LogInformation("Getting dashboard data for guidance counselor {UserId}", userId);
                
                // Get the guidance counselor's school ID
                var schoolId = await GetGuidanceCounselorSchoolIdAsync(userId);
                if (string.IsNullOrEmpty(schoolId))
                {
                    _logger.LogWarning("No school found for guidance counselor {UserId}", userId);
                    return Ok(new GuidanceDashboardData()); // Return empty (not 404) so dashboard still loads
                }

                _logger.LogInformation("Found school {SchoolId} for guidance counselor {UserId}", schoolId, userId);
                
                var dashboardData = await _guidanceService.GetDashboardDataAsync(schoolId, days);
                _logger.LogInformation("Retrieved dashboard data with {StudentCount} students and {FlaggedCount} flagged students", 
                    dashboardData.TotalStudents, dashboardData.FlaggedStudents);
                
                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data for guidance counselor {UserId}: {Message}", userId, ex.Message);
                return StatusCode(500, new { message = $"An error occurred while retrieving dashboard data: {ex.Message}" });
            }
        }

        [HttpGet("students/{userId}")]
        public async Task<ActionResult<List<StudentInfo>>> GetStudents(string userId)
        {
            try
            {
                var schoolId = await GetGuidanceCounselorSchoolIdAsync(userId);
                if (string.IsNullOrEmpty(schoolId))
                {
                    return NotFound(new { message = "Guidance counselor not found or no school assigned" });
                }

                var students = await _guidanceService.GetStudentsBySchoolAsync(schoolId);
                return Ok(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting students for guidance counselor {UserId}", userId);
                return StatusCode(500, new { message = "An error occurred while retrieving students" });
            }
        }

        [HttpGet("attendance-summary/{userId}")]
        public async Task<ActionResult<List<AttendanceSummary>>> GetAttendanceSummary(string userId, [FromQuery] int days = 30)
        {
            try
            {
                var schoolId = await GetGuidanceCounselorSchoolIdAsync(userId);
                if (string.IsNullOrEmpty(schoolId))
                {
                    return NotFound(new { message = "Guidance counselor not found or no school assigned" });
                }

                var summary = await _guidanceService.GetAttendanceSummaryAsync(schoolId, days);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance summary for guidance counselor {UserId}", userId);
                return StatusCode(500, new { message = "An error occurred while retrieving attendance summary" });
            }
        }

        [HttpPost("update-case-status")]
        public async Task<ActionResult> UpdateCaseStatus([FromBody] UpdateCaseStatusRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.StudentId))
                    return BadRequest(new { message = "Invalid request" });

                var success = await _guidanceService.UpdateCaseStatusAsync(request.StudentId, request.Status, request.Notes);
                if (success)
                    return Ok(new { success = true, message = "Status updated successfully" });
                
                return BadRequest(new { success = false, message = "Failed to update status" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for student {StudentId}", request?.StudentId);
                return StatusCode(500, new { message = "An error occurred while updating status" });
            }
        }

        [HttpGet("advisers-by-school/{schoolId}")]
        public async Task<ActionResult<List<AdviserInfo>>> GetAdvisersBySchool(string schoolId)
        {
            try
            {
                var advisers = new List<AdviserInfo>();
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT t.TeacherId, t.FullName, t.Email
                    FROM teacher t
                    INNER JOIN user u ON u.TeacherId = t.TeacherId
                    WHERE t.SchoolId = @SchoolId AND u.UserType IN ('GuidanceCounselor', 'Adviser') AND u.IsActive = 1
                    ORDER BY t.FullName";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    advisers.Add(new AdviserInfo
                    {
                        TeacherId = reader.GetString(0),
                        FullName = reader.GetString(1),
                        Email = reader.IsDBNull(2) ? null : reader.GetString(2)
                    });
                }
                return Ok(advisers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting advisers for school {SchoolId}", schoolId);
                return StatusCode(500, new List<AdviserInfo>());
            }
        }

        [HttpGet("test/{userId}")]
        public async Task<ActionResult> TestGuidanceCounselor(string userId)
        {
            try
            {
                _logger.LogInformation("Testing guidance counselor {UserId}", userId);
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Test 1: Check if user exists
                var userExists = await CheckUserExistsAsync(userId);
                if (!userExists)
                {
                    return Ok(new { 
                        status = "User not found",
                        userId = userId,
                        message = "The guidance counselor user does not exist in the database"
                    });
                }

                // Test 2: Check if user has teacher record
                var queryTeacher = "SELECT TeacherId FROM user WHERE UserId = @UserId";
                string? teacherId = null;
                using (var cmd = new MySqlCommand(queryTeacher, connection))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    teacherId = (await cmd.ExecuteScalarAsync())?.ToString();
                }

                if (string.IsNullOrEmpty(teacherId))
                {
                    return Ok(new { 
                        status = "TeacherId missing",
                        userId = userId,
                        message = "The user exists but their TeacherId is null. They must be linked to a record in the 'teacher' table."
                    });
                }

                // Test 3: Get school ID
                var schoolId = await GetGuidanceCounselorSchoolIdAsync(userId);
                
                // Extra info for diagnostics: are there ANY schools?
                var schoolCount = 0;
                using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM school", connection))
                {
                    schoolCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (string.IsNullOrEmpty(schoolId))
                {
                    return Ok(new { 
                        status = "School not found",
                        userId = userId,
                        schoolCount = schoolCount,
                        message = schoolCount == 0 ? "Zero schools exist in the 'school' table. Setup your school first." : "The guidance counselor has no school assigned. Tap 'Repair' to link your account."
                    });
                }

                // Test 4: Check students in school
                var studentCount = await GetStudentCountAsync(schoolId);
                
                return Ok(new { 
                    status = "Success",
                    userId = userId,
                    schoolId = schoolId,
                    studentCount = studentCount,
                    schoolCount = schoolCount,
                    message = "Guidance counselor setup is correct"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing guidance counselor {UserId}: {Message}", userId, ex.Message);
                return StatusCode(500, new { message = $"Test failed: {ex.Message}" });
            }
        }

        [HttpPost("repair-setup/{userId}")]
        public async Task<ActionResult> FixGuidanceSetup(string userId)
        {
            try
            {
                _logger.LogInformation("Attempting to auto-repair guidance setup for {UserId}", userId);
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // 1. Get the first school ID from the DB
                string? schoolId = null;
                using (var cmd = new MySqlCommand("SELECT SchoolId FROM school LIMIT 1", connection))
                {
                    schoolId = (await cmd.ExecuteScalarAsync())?.ToString();
                }

                if (string.IsNullOrEmpty(schoolId))
                {
                    return BadRequest(new { message = "No schools found in database to link to." });
                }

                // 2. Get the TeacherId tied to this user
                string? teacherId = null;
                using (var cmd = new MySqlCommand("SELECT TeacherId FROM user WHERE UserId = @UserId", connection))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    teacherId = (await cmd.ExecuteScalarAsync())?.ToString();
                }

                if (string.IsNullOrEmpty(teacherId))
                {
                    // Create a dummy teacher record if missing? 
                    // No, let's just fail and tell them to register properly or manually link.
                    // But for "Auto-Fix", let's try to be helpful.
                    return BadRequest(new { message = "User has no TeacherId. Account might be corrupt or incomplete." });
                }

                // 3. Update both tables
                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // Update user table
                    var updateU = "UPDATE user SET SchoolId = @Sid WHERE UserId = @Uid";
                    using (var cmd = new MySqlCommand(updateU, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@Sid", schoolId);
                        cmd.Parameters.AddWithValue("@Uid", userId);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Update teacher table
                    var updateT = "UPDATE teacher SET SchoolId = @Sid WHERE TeacherId = @Tid";
                    using (var cmd = new MySqlCommand(updateT, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@Sid", schoolId);
                        cmd.Parameters.AddWithValue("@Tid", teacherId);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Ok(new { success = true, schoolId = schoolId, message = "Successfully linked to first available school." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error repairing guidance setup for {UserId}", userId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        private async Task<string?> GetGuidanceCounselorSchoolIdAsync(string userId)
        {
            try
            {
                _logger.LogInformation("Getting school ID for guidance counselor {UserId}", userId);
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Check teacher join first, but also select SchoolId directly from user table as fallback
                var query = @"
                    SELECT COALESCE(t.SchoolId, u.SchoolId) as SchoolId
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.UserId = @UserId AND u.IsActive = true";
                    
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);

                var result = await command.ExecuteScalarAsync();
                var schoolId = result?.ToString();

                // Second Fallback: Just get any schoolId from user table for this user
                if (string.IsNullOrEmpty(schoolId))
                {
                    var fallbackQuery = "SELECT SchoolId FROM user WHERE UserId = @UserId2 LIMIT 1";
                    using var fallbackCmd = new MySqlCommand(fallbackQuery, connection);
                    fallbackCmd.Parameters.AddWithValue("@UserId2", userId);
                    var fallbackResult = await fallbackCmd.ExecuteScalarAsync();
                    schoolId = fallbackResult?.ToString();
                }
                
                // Third Fallback: If still nothing, try to find ANY school this user might be related to via teacher table even if inactive
                if (string.IsNullOrEmpty(schoolId))
                {
                    var query3 = "SELECT t.SchoolId FROM teacher t INNER JOIN user u ON u.TeacherId = t.TeacherId WHERE u.UserId = @UserId3 LIMIT 1";
                    using var cmd3 = new MySqlCommand(query3, connection);
                    cmd3.Parameters.AddWithValue("@UserId3", userId);
                    schoolId = (await cmd3.ExecuteScalarAsync())?.ToString();
                }
                
                _logger.LogInformation("School ID for guidance counselor {UserId}: {SchoolId}", userId, schoolId ?? "null");
                return schoolId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting school ID for guidance counselor {UserId}: {Message}", userId, ex.Message);
                return null;
            }
        }

        private async Task<bool> CheckUserExistsAsync(string userId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT COUNT(*) FROM user WHERE UserId = @UserId";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);

                var count = await command.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if user exists {UserId}: {Message}", userId, ex.Message);
                return false;
            }
        }

        private async Task<bool> CheckTeacherExistsAsync(string userId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT COUNT(*) 
                    FROM user u
                    INNER JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.UserId = @UserId";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);

                var count = await command.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if teacher exists for user {UserId}: {Message}", userId, ex.Message);
                return false;
            }
        }

        private async Task<int> GetStudentCountAsync(string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT COUNT(*) FROM student WHERE SchoolId = @SchoolId AND IsActive = true";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);

                var count = await command.ExecuteScalarAsync();
                return Convert.ToInt32(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student count for school {SchoolId}: {Message}", schoolId, ex.Message);
                return 0;
            }
        }
        [HttpPost("notify-student")]
        public async Task<ActionResult> NotifyStudent([FromBody] NotifyStudentRequest request)
        {
            try
            {
                var success = await _guidanceService.NotifyStudentAsync(request.StudentId, request.Type);
                if (success) return Ok();
                return BadRequest("Failed to notify student");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying student");
                return StatusCode(500, "Internal server error");
            }
        }
    }

    public class NotifyStudentRequest
    {
        public string StudentId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
