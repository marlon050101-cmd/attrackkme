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
        public async Task<ActionResult<GuidanceDashboardData>> GetDashboardData(string userId)
        {
            try
            {
                _logger.LogInformation("Getting dashboard data for guidance counselor {UserId}", userId);
                
                // Get the guidance counselor's school ID
                var schoolId = await GetGuidanceCounselorSchoolIdAsync(userId);
                if (string.IsNullOrEmpty(schoolId))
                {
                    _logger.LogWarning("No school found for guidance counselor {UserId}", userId);
                    return NotFound(new { message = "Guidance counselor not found or no school assigned" });
                }

                _logger.LogInformation("Found school {SchoolId} for guidance counselor {UserId}", schoolId, userId);
                
                var dashboardData = await _guidanceService.GetDashboardDataAsync(schoolId);
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

        [HttpGet("test/{userId}")]
        public async Task<ActionResult> TestGuidanceCounselor(string userId)
        {
            try
            {
                _logger.LogInformation("Testing guidance counselor {UserId}", userId);
                
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
                var teacherExists = await CheckTeacherExistsAsync(userId);
                if (!teacherExists)
                {
                    return Ok(new { 
                        status = "Teacher record not found",
                        userId = userId,
                        message = "The user exists but has no teacher record"
                    });
                }

                // Test 3: Get school ID
                var schoolId = await GetGuidanceCounselorSchoolIdAsync(userId);
                if (string.IsNullOrEmpty(schoolId))
                {
                    return Ok(new { 
                        status = "School not found",
                        userId = userId,
                        message = "The guidance counselor has no school assigned"
                    });
                }

                // Test 4: Check students in school
                var studentCount = await GetStudentCountAsync(schoolId);
                
                return Ok(new { 
                    status = "Success",
                    userId = userId,
                    schoolId = schoolId,
                    studentCount = studentCount,
                    message = "Guidance counselor setup is correct"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing guidance counselor {UserId}: {Message}", userId, ex.Message);
                return StatusCode(500, new { message = $"Test failed: {ex.Message}" });
            }
        }

        private async Task<string?> GetGuidanceCounselorSchoolIdAsync(string userId)
        {
            try
            {
                _logger.LogInformation("Getting school ID for guidance counselor {UserId}", userId);
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT t.SchoolId 
                    FROM user u
                    INNER JOIN teacher t ON u.TeacherId = t.TeacherId
                    WHERE u.UserId = @UserId AND u.UserType = 'GuidanceCounselor' AND u.IsActive = true";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);

                var result = await command.ExecuteScalarAsync();
                var schoolId = result?.ToString();
                
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
    }
}
