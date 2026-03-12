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
                var summaries = new List<AttendanceSummary>();

                // Query 1: Daily Summaries from student_daily_summary
                var dailyQuery = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(DISTINCT sds.Date) as TotalDays,
                           COUNT(DISTINCT CASE WHEN sds.Status IN ('Present', 'Late', 'Partial') THEN sds.Date ELSE NULL END) as PresentDays,
                           COUNT(DISTINCT CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as AbsentDays,
                           COUNT(DISTINCT CASE WHEN sds.Status = 'Late' THEN sds.Date ELSE NULL END) as LateDays,
                           MIN(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as FirstAbsentDate,
                           MAX(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as LastAbsentDate,
                           GROUP_CONCAT(DISTINCT CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END ORDER BY sds.Date SEPARATOR ', ') as AbsentDates,
                           COALESCE((SELECT Status FROM guidance_cases WHERE StudentId = s.StudentId ORDER BY DateCreated DESC LIMIT 1), 'Normal') as GuidanceStatus
                    FROM student s
                    LEFT JOIN student_daily_summary sds ON s.StudentId = sds.StudentId 
                         AND sds.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                    WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
                    HAVING AbsentDays > 0";

                using (var command = new MySqlCommand(dailyQuery, connection))
                {
                    command.Parameters.AddWithValue("@SchoolId", schoolId);
                    command.Parameters.AddWithValue("@Days", days);
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
                            IncompleteSessions = 0, // Daily has no incomplete sessions concept, it's subject level
                            SubjectName = "All", // Represents all subjects for a full day
                            AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                            FirstAbsentDate = reader.IsDBNull("FirstAbsentDate") ? null : reader.GetDateTime("FirstAbsentDate"),
                            LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate"),
                            AbsentDates = reader.IsDBNull("AbsentDates") ? string.Empty : reader.GetString("AbsentDates"),
                            GuidanceStatus = reader.IsDBNull("GuidanceStatus") ? "Normal" : reader.GetString("GuidanceStatus")
                        });
                    }
                }

                // Query 2: Subject Summaries from subject_attendance (Cutting Class Risk)
                var subjectQuery = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(DISTINCT sa.Date) as TotalDays,
                           COUNT(DISTINCT CASE WHEN sa.Status IN ('Present', 'Late', 'Partial') THEN sa.Date ELSE NULL END) as PresentDays,
                           COUNT(DISTINCT CASE WHEN sa.Status = 'Absent' THEN sa.Date ELSE NULL END) as AbsentDays,
                           COUNT(DISTINCT CASE WHEN sa.Status = 'Late' THEN sa.Date ELSE NULL END) as LateDays,
                           COUNT(DISTINCT CASE WHEN sa.TimeIn IS NOT NULL AND sa.TimeOut IS NULL THEN sa.Date ELSE NULL END) as DaysWithIncompleteSessions,
                           COALESCE(sub_co.SubjectName, sub_ts.SubjectName, 'Unknown Subject') as SubjectName,
                           MIN(CASE WHEN sa.Status = 'Absent' THEN sa.Date ELSE NULL END) as FirstAbsentDate,
                           MAX(CASE WHEN sa.Status = 'Absent' THEN sa.Date ELSE NULL END) as LastAbsentDate,
                           GROUP_CONCAT(DISTINCT CASE WHEN sa.Status = 'Absent' THEN sa.Date ELSE NULL END ORDER BY sa.Date SEPARATOR ', ') as AbsentDates,
                           COALESCE((SELECT Status FROM guidance_cases WHERE StudentId = s.StudentId ORDER BY DateCreated DESC LIMIT 1), 'Normal') as GuidanceStatus
                    FROM student s
                    INNER JOIN subject_attendance sa ON sa.StudentId = s.StudentId 
                          AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                    LEFT JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                    LEFT JOIN subject sub_co ON co.SubjectId = sub_co.SubjectId
                    LEFT JOIN teachersubject ts ON sa.TeacherSubjectId = ts.TeacherSubjectId 
                    LEFT JOIN subject sub_ts ON ts.SubjectId = sub_ts.SubjectId
                    WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender, SubjectName
                    HAVING AbsentDays > 0 OR DaysWithIncompleteSessions > 0";

                using (var command = new MySqlCommand(subjectQuery, connection))
                {
                    command.Parameters.AddWithValue("@SchoolId", schoolId);
                    command.Parameters.AddWithValue("@Days", days);
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
                            IncompleteSessions = reader.IsDBNull("DaysWithIncompleteSessions") ? 0 : reader.GetInt32("DaysWithIncompleteSessions"),
                            SubjectName = reader.IsDBNull("SubjectName") ? "Unknown Subject" : reader.GetString("SubjectName"),
                            AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                            FirstAbsentDate = reader.IsDBNull("FirstAbsentDate") ? null : reader.GetDateTime("FirstAbsentDate"),
                            LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate"),
                            AbsentDates = reader.IsDBNull("AbsentDates") ? string.Empty : reader.GetString("AbsentDates"),
                            GuidanceStatus = reader.IsDBNull("GuidanceStatus") ? "Normal" : reader.GetString("GuidanceStatus")
                        });
                    }
                }

                // Calculate consecutive absences strictly for the Daily Summary
                foreach (var summary in summaries.Where(s => s.SubjectName == "All"))
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
                    (s.AbsentDays >= warningThreshold && s.AbsentDays < (days <= 3 ? 3 : 3)) ||
                    s.IncompleteSessions > 0 // Cutting class risk
                ).ToList();
                
                // Combine both for display (ensure no duplicates for the SAME subject/daily type)
                var allFlaggedStudents = criticalStudents.Concat(warningStudents)
                    .GroupBy(s => new { s.StudentId, s.SubjectName }) // Group by Student+Subject
                    .Select(g => g.First())
                    .ToList();
                
                // For grade levels and sections, distinct across Students (regardless of subject)
                var uniqueStudents = allFlaggedStudents.Select(s => s.StudentId).Distinct().ToList();
                var gradeLevels = allFlaggedStudents.Select(s => s.GradeLevel).Distinct().Count();
                var sections = allFlaggedStudents.Select(s => s.Section).Distinct().Count();
                var noTimeOutCount = uniqueStudents.Count(id => allFlaggedStudents.Any(s => s.StudentId == id && s.IncompleteSessions > 0));

                return new GuidanceDashboardData
                {
                    TotalStudents = students.Count,
                    FlaggedStudents = uniqueStudents.Count, // Total unique students at risk
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
