using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;
using System.Data;

namespace ServerAtrrak.Services
{
    public class GuidanceService : IGuidanceService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<GuidanceService> _logger;

        public GuidanceService(Dbconnection dbConnection, ILogger<GuidanceService> logger)
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
                        SchoolId = schoolId, // Set from the method parameter
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
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Join with guidance_cases to get Status
                var query = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(sa.SubjectAttendanceId) as TotalDays,
                           SUM(CASE WHEN sa.Status = 'Present' THEN 1 ELSE 0 END) as PresentDays,
                           SUM(CASE WHEN sa.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
                           SUM(CASE WHEN sa.Status = 'Late' THEN 1 ELSE 0 END) as LateDays,
                           SUM(CASE WHEN sa.TimeIn IS NOT NULL AND sa.TimeOut IS NULL THEN 1 ELSE 0 END) as IncompleteSessions,
                           sub.SubjectName,
                           COALESCE(gc.Status, 'Normal') as GuidanceStatus
                    FROM student s
                    INNER JOIN class_offering co ON s.GradeLevel = co.GradeLevel AND s.Section = co.Section
                    INNER JOIN subject sub ON co.SubjectId = sub.SubjectId
                    LEFT JOIN subject_attendance sa ON s.StudentId = sa.StudentId 
                        AND sa.ClassOfferingId = co.ClassOfferingId
                        AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                    LEFT JOIN guidance_cases gc ON s.StudentId = gc.StudentId
                    WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender, sub.SubjectName, gc.Status
                    HAVING AbsentDays > 0 OR IncompleteSessions > 0
                    ORDER BY AbsentDays DESC, IncompleteSessions DESC, s.GradeLevel, s.Section, s.FullName";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                command.Parameters.AddWithValue("@Days", days);

                var summaries = new List<AttendanceSummary>();
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var studentId = reader.GetString("StudentId");
                    var subjectName = reader.IsDBNull("SubjectName") ? null : reader.GetString("SubjectName");
                    var totalDays = reader.IsDBNull("TotalDays") ? 0 : reader.GetInt32("TotalDays");
                    var presentDays = reader.IsDBNull("PresentDays") ? 0 : reader.GetInt32("PresentDays");
                    
                    summaries.Add(new AttendanceSummary
                    {
                        StudentId = studentId,
                        FullName = reader.IsDBNull("FullName") ? string.Empty : reader.GetString("FullName"),
                        GradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel"),
                        Section = reader.IsDBNull("Section") ? string.Empty : reader.GetString("Section"),
                        Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                        Gender = reader.IsDBNull("Gender") ? string.Empty : reader.GetString("Gender"),
                        TotalDays = totalDays,
                        PresentDays = presentDays,
                        AbsentDays = reader.IsDBNull("AbsentDays") ? 0 : reader.GetInt32("AbsentDays"),
                        LateDays = reader.IsDBNull("LateDays") ? 0 : reader.GetInt32("LateDays"),
                        IncompleteSessions = reader.IsDBNull("IncompleteSessions") ? 0 : reader.GetInt32("IncompleteSessions"),
                        AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                        GuidanceStatus = reader.GetString("GuidanceStatus"),
                        SubjectName = subjectName
                    });
                }
                reader.Close();

                // Calculate consecutive absences for flagged students
                foreach (var summary in summaries)
                {
                    summary.ConsecutiveAbsences = await GetConsecutiveAbsencesAsync(summary.StudentId, summary.SubjectName, connection);
                }

                return summaries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance summary for school {SchoolId}", schoolId);
                throw;
            }
        }

        private async Task<int> GetConsecutiveAbsencesAsync(string studentId, string? subjectName, MySqlConnection connection)
        {
            var query = @"
                SELECT Status FROM subject_attendance sa
                INNER JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                INNER JOIN subject sub ON co.SubjectId = sub.SubjectId
                WHERE sa.StudentId = @StudentId AND sub.SubjectName = @SubjectName
                ORDER BY sa.Date DESC
                LIMIT 10";

            using var cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@StudentId", studentId);
            cmd.Parameters.AddWithValue("@SubjectName", subjectName ?? (object)DBNull.Value);

            int consecutive = 0;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(0) == "Absent")
                    consecutive++;
                else
                    break;
            }
            return consecutive;
        }

        public async Task<GuidanceDashboardData> GetDashboardDataAsync(string schoolId, int days = 30)
        {
            try
            {
                var students = await GetStudentsBySchoolAsync(schoolId);
                var summaries = await GetAttendanceSummaryAsync(schoolId, days);

                // For small timeframes (Daily/Weekly), be more sensitive
                // days=1 (Daily): Flag anyone absent today
                // days=7 (Weekly): Flag anyone with at least 1-2 absences? Or stick to 3?
                // Let's use a dynamic threshold: Math.Max(1, Math.Min(3, days / 2))? No, simple is better.
                int threshold = days <= 3 ? 1 : 3;

                var flaggedStudents = summaries.Where(s => s.AbsentDays >= threshold || s.ConsecutiveAbsences >= 3 || s.IncompleteSessions > 0).ToList();
                
                var uniqueFlaggedIds = flaggedStudents.Select(s => s.StudentId).Distinct().Count();
                var gradeLevels = flaggedStudents.Select(s => s.GradeLevel).Distinct().Count();
                var sections = flaggedStudents.Select(s => s.Section).Distinct().Count();

                return new GuidanceDashboardData
                {
                    TotalStudents = students.Count,
                    FlaggedStudents = uniqueFlaggedIds,
                    GradeLevelsAffected = gradeLevels,
                    SectionsMonitored = sections,
                    StudentsAtRisk = flaggedStudents,
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
    }

    public interface IGuidanceService
    {
        Task<List<StudentInfo>> GetStudentsBySchoolAsync(string schoolId);
        Task<List<AttendanceSummary>> GetAttendanceSummaryAsync(string schoolId, int days = 30);
        Task<GuidanceDashboardData> GetDashboardDataAsync(string schoolId, int days = 30);
        Task<bool> UpdateCaseStatusAsync(string studentId, string status, string? notes = null);
    }

}
