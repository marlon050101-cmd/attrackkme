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

                // Query from student_daily_summary to correctly track adviser manual overrides
                var query = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(sds.Date) as TotalDays,
                           SUM(CASE WHEN sds.Status IN ('Present', 'Late', 'Partial') THEN 1 ELSE 0 END) as PresentDays,
                           SUM(CASE WHEN sds.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
                           SUM(CASE WHEN sds.Status = 'Late' THEN 1 ELSE 0 END) as LateDays,
                           SUM(CASE WHEN sds.IncompleteSessions > 0 THEN 1 ELSE 0 END) as DaysWithIncompleteSessions,
                           MIN(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as FirstAbsentDate,
                           MAX(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as LastAbsentDate,
                           GROUP_CONCAT(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END ORDER BY sds.Date SEPARATOR ', ') as AbsentDates
                    FROM student s
                    LEFT JOIN (
                        SELECT StudentId, Date, Status,
                               -- Check if any subjects for this day are incomplete (TimeIn without TimeOut)
                               (SELECT COUNT(*) FROM subject_attendance sa 
                                WHERE sa.StudentId = sds_inner.StudentId AND sa.Date = sds_inner.Date 
                                  AND sa.TimeIn IS NOT NULL AND sa.TimeOut IS NULL) as IncompleteSessions
                        FROM student_daily_summary sds_inner
                    ) sds ON s.StudentId = sds.StudentId 
                         AND sds.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                    WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
                    HAVING AbsentDays > 0 OR DaysWithIncompleteSessions > 0
                    ORDER BY AbsentDays DESC, s.GradeLevel, s.Section, s.FullName";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                command.Parameters.AddWithValue("@Days", days);

                var summaries = new List<AttendanceSummary>();
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var totalDays = reader.IsDBNull("TotalDays") ? 0 : reader.GetInt32("TotalDays");
                    var presentDays = reader.IsDBNull("PresentDays") ? 0 : reader.GetInt32("PresentDays");
                    var incompleteSessions = reader.IsDBNull("DaysWithIncompleteSessions") ? 0 : reader.GetInt32("DaysWithIncompleteSessions");
                    
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
                        IncompleteSessions = incompleteSessions,
                        AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                        FirstAbsentDate = reader.IsDBNull("FirstAbsentDate") ? null : reader.GetDateTime("FirstAbsentDate"),
                        LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate"),
                        AbsentDates = reader.IsDBNull("AbsentDates") ? string.Empty : reader.GetString("AbsentDates")
                    });
                }
                
                reader.Close(); // Close before firing off another reader loop

                // Calculate consecutive absences
                foreach (var summary in summaries)
                {
                    summary.ConsecutiveAbsences = await GetConsecutiveAbsencesAsync(summary.StudentId, connection);
                }

                _logger.LogInformation("Retrieved {Count} flagged attendance summaries for school {SchoolId}", summaries.Count, schoolId);
                return summaries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance summary for school {SchoolId}", schoolId);
                throw;
            }
        }

        private async Task<int> GetConsecutiveAbsencesAsync(string studentId, MySqlConnection connection)
        {
            var query = @"
                SELECT Status FROM student_daily_summary
                WHERE StudentId = @StudentId AND Status != 'Not yet timed in'
                ORDER BY Date DESC
                LIMIT 10";

            using var cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@StudentId", studentId);

            int consecutive = 0;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var status = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (status == "Absent" || status == "Half Day") 
                    consecutive++;
                else
                    break; // Break on a present or valid day
            }
            return consecutive;
        }

        public async Task<GuidanceDashboardData> GetDashboardDataAsync(string schoolId, int days = 30)
        {
            try
            {
                var students = await GetStudentsBySchoolAsync(schoolId);
                var attendanceSummary = await GetAttendanceSummaryAsync(schoolId, days);

                // Adjust thresholds based on timeframe
                int criticalThreshold = days <= 3 ? 1 : 3;
                int warningThreshold = days <= 3 ? 1 : 2;

                // Critical Alert: 3+ consecutive absences (red alert) or 1+ for Daily
                var criticalStudents = attendanceSummary.Where(s => s.ConsecutiveAbsences >= criticalThreshold || s.AbsentDays >= criticalThreshold).ToList();
                
                // Warning Alert: 2 consecutive absences (orange alert) or 1+ for Daily
                var warningStudents = attendanceSummary.Where(s => 
                    (s.ConsecutiveAbsences >= warningThreshold && s.ConsecutiveAbsences < (days <= 3 ? 3 : 3)) ||
                    (s.AbsentDays >= warningThreshold && s.AbsentDays < (days <= 3 ? 3 : 3))
                ).ToList();
                
                // Combine both for display (ensure no duplicates if logic overlaps)
                var allFlaggedStudents = criticalStudents.Concat(warningStudents).GroupBy(s => s.StudentId).Select(g => g.First()).ToList();
                
                var gradeLevels = allFlaggedStudents.Select(s => s.GradeLevel).Distinct().Count();
                var sections = allFlaggedStudents.Select(s => s.Section).Distinct().Count();
                var noTimeOutCount = attendanceSummary.Count(s => s.IncompleteSessions > 0);

                return new GuidanceDashboardData
                {
                    TotalStudents = students.Count,
                    FlaggedStudents = allFlaggedStudents.Count, // Total of critical + warning
                    NoTimeOutCount = noTimeOutCount,
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
        public async Task<bool> UpdateCaseStatusAsync(string studentId, string status, string? notes = null)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO guidance_cases (CaseId, StudentId, Status, LastFlaggedDate, Notes)
                    VALUES (@CaseId, @StudentId, @Status, NOW(), @Notes)
                    ON DUPLICATE KEY UPDATE 
                        Status = @Status, 
                        Notes = COALESCE(@Notes, Notes),
                        UpdatedAt = NOW()";
                
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@CaseId", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@Notes", notes ?? (object)DBNull.Value);

                return await cmd.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating case status for student {StudentId}", studentId);
                return false;
            }
        }
        public async Task<bool> NotifyStudentAsync(string studentId, string type)
        {
            _logger.LogInformation("Awareness notification (NoDateFilter) for {Id} from {Type}", studentId, type);
            return true; // Simplified stub
        }
    }
}
