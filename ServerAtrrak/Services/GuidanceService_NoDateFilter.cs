using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;
using System.Data;

namespace ServerAtrrak.Services
{
    public class GuidanceServiceNoDateFilter : IGuidanceService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<GuidanceServiceNoDateFilter> _logger;

        public GuidanceServiceNoDateFilter(Dbconnection dbConnection, ILogger<GuidanceServiceNoDateFilter> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<List<StudentInfo>> GetStudentsBySchoolAsync(string schoolId)
        {
            try
            {
                if (string.IsNullOrEmpty(schoolId))
                {
                    _logger.LogWarning("SchoolId is null or empty");
                    return new List<StudentInfo>();
                }

                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT s.StudentId, s.FullName, s.Email, s.GradeLevel, s.Section, s.Strand, 
                           s.ParentsNumber, s.Gender, s.IsActive, s.CreatedAt, s.UpdatedAt
                    FROM student s
                    WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                    ORDER BY s.GradeLevel, s.Section, s.FullName";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);

                var students = new List<StudentInfo>();
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    students.Add(new StudentInfo
                    {
                        StudentId = reader.IsDBNull("StudentId") ? string.Empty : reader.GetString("StudentId"),
                        FullName = reader.IsDBNull("FullName") ? string.Empty : reader.GetString("FullName"),
                        Email = reader.IsDBNull("Email") ? string.Empty : reader.GetString("Email"),
                        GradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel"),
                        Section = reader.IsDBNull("Section") ? string.Empty : reader.GetString("Section"),
                        Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                        SchoolId = schoolId,
                        ParentsNumber = reader.IsDBNull("ParentsNumber") ? string.Empty : reader.GetString("ParentsNumber"),
                        Gender = reader.IsDBNull("Gender") ? string.Empty : reader.GetString("Gender"),
                        IsActive = reader.IsDBNull("IsActive") ? false : reader.GetBoolean("IsActive"),
                        CreatedAt = reader.IsDBNull("CreatedAt") ? DateTime.MinValue : reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.IsDBNull("UpdatedAt") ? DateTime.MinValue : reader.GetDateTime("UpdatedAt")
                    });
                }

                _logger.LogInformation("Retrieved {Count} students for school {SchoolId}", students.Count, schoolId);
                return students;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting students for school {SchoolId}", schoolId);
                throw;
            }
        }

        public async Task<List<AttendanceSummary>> GetAttendanceSummaryAsync(string schoolId, int days = 30)
        {
            try
            {
                using var connection = new MySql.Data.MySqlClient.MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Modified query - Check for consecutive absences within a week
                var query = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(da.AttendanceId) as TotalDays,
                           SUM(CASE WHEN da.Status = 'Present' THEN 1 ELSE 0 END) as PresentDays,
                           SUM(CASE WHEN da.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
                           SUM(CASE WHEN da.Status = 'Late' THEN 1 ELSE 0 END) as LateDays,
                           MIN(da.Date) as FirstAbsentDate,
                           MAX(da.Date) as LastAbsentDate,
                           GROUP_CONCAT(da.Date ORDER BY da.Date SEPARATOR ', ') as AbsentDates
                    FROM student s
                    LEFT JOIN daily_attendance da ON s.StudentId = da.StudentId AND da.Status = 'Absent'
                    WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
                    HAVING AbsentDays >= 2
                    ORDER BY AbsentDays DESC, s.GradeLevel, s.Section, s.FullName";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);

                var summaries = new List<AttendanceSummary>();
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var totalDays = reader.IsDBNull("TotalDays") ? 0 : reader.GetInt32("TotalDays");
                    var presentDays = reader.IsDBNull("PresentDays") ? 0 : reader.GetInt32("PresentDays");
                    
                    summaries.Add(new AttendanceSummary
                    {
                        StudentId = reader.IsDBNull("StudentId") ? string.Empty : reader.GetString("StudentId"),
                        FullName = reader.IsDBNull("FullName") ? string.Empty : reader.GetString("FullName"),
                        GradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel"),
                        Section = reader.IsDBNull("Section") ? string.Empty : reader.GetString("Section"),
                        Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                        Gender = reader.IsDBNull("Gender") ? string.Empty : reader.GetString("Gender"),
                        TotalDays = totalDays,
                        PresentDays = presentDays,
                        AbsentDays = reader.IsDBNull("AbsentDays") ? 0 : reader.GetInt32("AbsentDays"),
                        LateDays = reader.IsDBNull("LateDays") ? 0 : reader.GetInt32("LateDays"),
                        AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                        FirstAbsentDate = reader.IsDBNull("FirstAbsentDate") ? null : reader.GetDateTime("FirstAbsentDate"),
                        LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate"),
                        AbsentDates = reader.IsDBNull("AbsentDates") ? string.Empty : reader.GetString("AbsentDates")
                    });
                }

                _logger.LogInformation("Retrieved {Count} attendance summaries for school {SchoolId}", summaries.Count, schoolId);
                return summaries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance summary for school {SchoolId}", schoolId);
                throw;
            }
        }

        public async Task<GuidanceDashboardData> GetDashboardDataAsync(string schoolId)
        {
            try
            {
                var students = await GetStudentsBySchoolAsync(schoolId);
                var attendanceSummary = await GetAttendanceSummaryAsync(schoolId);

                // Critical Alert: 3+ absences (red alert)
                var criticalStudents = attendanceSummary.Where(s => s.AbsentDays >= 3).ToList();
                
                // Warning Alert: 2 absences (yellow warning)
                var warningStudents = attendanceSummary.Where(s => s.AbsentDays == 2).ToList();
                
                // Combine both for display
                var allFlaggedStudents = criticalStudents.Concat(warningStudents).ToList();
                
                var gradeLevels = allFlaggedStudents.Select(s => s.GradeLevel).Distinct().Count();
                var sections = allFlaggedStudents.Select(s => s.Section).Distinct().Count();

                return new GuidanceDashboardData
                {
                    TotalStudents = students.Count,
                    FlaggedStudents = allFlaggedStudents.Count, // Total of critical + warning
                    GradeLevelsAffected = gradeLevels,
                    SectionsMonitored = sections,
                    StudentsAtRisk = allFlaggedStudents, // Show both critical and warning
                    AllStudents = students
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data for school {SchoolId}", schoolId);
                throw;
            }
        }
    }
}
