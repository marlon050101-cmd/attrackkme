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
        private readonly SmsQueueService _smsQueueService;

        public GuidanceService(Dbconnection dbConnection, ILogger<GuidanceService> logger, SmsQueueService smsQueueService)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _smsQueueService = smsQueueService;
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
                var summaries = new List<AttendanceSummary>();

                // Query 1: Absences from subject_attendance (primary QR scan table - most reliable)
                var dailyQuery = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(DISTINCT sa.Date) as TotalDays,
                           COUNT(DISTINCT CASE WHEN sa.Status IN ('Present','Late') THEN sa.Date END) as PresentDays,
                           COUNT(DISTINCT CASE WHEN sa.Status = 'Absent' THEN sa.Date END) as AbsentDays,
                           COUNT(DISTINCT CASE WHEN sa.Status = 'Late' THEN sa.Date END) as LateDays,
                           MIN(CASE WHEN sa.Status = 'Absent' THEN sa.Date END) as FirstAbsentDate,
                           MAX(CASE WHEN sa.Status = 'Absent' THEN sa.Date END) as LastAbsentDate,
                           GROUP_CONCAT(DISTINCT CASE WHEN sa.Status = 'Absent' THEN DATE_FORMAT(sa.Date, '%Y-%m-%d') END ORDER BY sa.Date DESC SEPARATOR ',') as AbsentDates,
                           COALESCE((SELECT Status FROM guidance_cases WHERE StudentId = s.StudentId ORDER BY UpdatedAt DESC LIMIT 1), 'Normal') as GuidanceStatus
                    FROM student s
                    INNER JOIN subject_attendance sa ON sa.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId
                      AND s.IsActive = true
                      AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
                    HAVING AbsentDays > 0";

                using (var command = new MySqlCommand(dailyQuery, connection))
                {
                    command.Parameters.AddWithValue("@SchoolId", schoolId);
                    command.Parameters.AddWithValue("@Days", days);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var totalDays = reader.IsDBNull("TotalDays") ? 0 : Convert.ToInt32(reader["TotalDays"]);
                        var presentDays = reader.IsDBNull("PresentDays") ? 0 : Convert.ToInt32(reader["PresentDays"]);
                        summaries.Add(new AttendanceSummary
                        {
                            StudentId = reader.GetString("StudentId"),
                            FullName = reader.GetString("FullName"),
                            GradeLevel = reader.GetInt32("GradeLevel"),
                            Section = reader.GetString("Section"),
                            Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                            Gender = reader.IsDBNull("Gender") ? "" : reader.GetString("Gender"),
                            TotalDays = totalDays,
                            PresentDays = presentDays,
                            AbsentDays = reader.IsDBNull("AbsentDays") ? 0 : Convert.ToInt32(reader["AbsentDays"]),
                            LateDays = reader.IsDBNull("LateDays") ? 0 : Convert.ToInt32(reader["LateDays"]),
                            AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                            GuidanceStatus = reader.GetString("GuidanceStatus"),
                            SubjectName = "Overall",
                            FirstAbsentDate = reader.IsDBNull("FirstAbsentDate") ? null : reader.GetDateTime("FirstAbsentDate"),
                            LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate"),
                            AbsentDates = reader.IsDBNull("AbsentDates") ? "" : reader.GetString("AbsentDates")
                        });
                    }
                }

                // Query 2: Subject Summaries for Cutting Class Risk (NoTimeOut)
                var subjectQuery = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(sa.SubjectAttendanceId) as IncompleteSessions,
                           MAX(sa.Date) as LastAbsentDate,
                           COALESCE(sub_co.SubjectName, sub_ts.SubjectName, 'Unknown Subject') as SubjectName,
                           COALESCE((SELECT Status FROM guidance_cases WHERE StudentId = s.StudentId ORDER BY UpdatedAt DESC LIMIT 1), 'Normal') as GuidanceStatus
                    FROM student s
                    INNER JOIN subject_attendance sa ON sa.StudentId = s.StudentId 
                    LEFT JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                    LEFT JOIN subject sub_co ON co.SubjectId = sub_co.SubjectId
                    LEFT JOIN teachersubject ts ON sa.TeacherSubjectId = ts.TeacherSubjectId 
                    LEFT JOIN subject sub_ts ON ts.SubjectId = sub_ts.SubjectId
                    WHERE s.SchoolId = @SchoolId 
                      AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                      AND sa.TimeIn IS NOT NULL AND sa.TimeOut IS NULL
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender, SubjectName";

                using (var command = new MySqlCommand(subjectQuery, connection))
                {
                    command.Parameters.AddWithValue("@SchoolId", schoolId);
                    command.Parameters.AddWithValue("@Days", days);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        summaries.Add(new AttendanceSummary
                        {
                            StudentId = reader.GetString("StudentId"),
                            FullName = reader.GetString("FullName"),
                            GradeLevel = reader.GetInt32("GradeLevel"),
                            Section = reader.GetString("Section"),
                            IncompleteSessions = reader.IsDBNull("IncompleteSessions") ? 0 : Convert.ToInt32(reader["IncompleteSessions"]),
                            SubjectName = reader.GetString("SubjectName"),
                            GuidanceStatus = reader.GetString("GuidanceStatus"),
                            LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate")
                        });
                    }
                }

                // Calculate consecutive absences
                foreach (var s in summaries.Where(x => x.SubjectName == "Overall"))
                {
                    s.ConsecutiveAbsences = await GetConsecutiveAbsencesAsync(s.StudentId, null, connection);
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
            try
            {
                // If subjectName is null or Overall, use Daily Summary
                if (string.IsNullOrEmpty(subjectName) || subjectName == "Overall")
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
                            break;
                    }
                    return consecutive;
                }
                else
                {
                    // Use Subject Attendance for specific subject consecutive absences
                    var query = @"
                        SELECT Status FROM subject_attendance sa
                        WHERE sa.StudentId = @StudentId AND sa.Status != 'Not yet timed in'
                        ORDER BY sa.Date DESC
                        LIMIT 10";

                    using var cmd = new MySqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@StudentId", studentId);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting consecutive absences for {StudentId}", studentId);
                return 0;
            }
        }

        public async Task<GuidanceDashboardData> GetDashboardDataAsync(string schoolId, int days = 30)
        {
            try
            {
                var students = await GetStudentsBySchoolAsync(schoolId);
                var summaries = await GetAttendanceSummaryAsync(schoolId, days);

                int threshold = days <= 3 ? 1 : 3;
                var flaggedStudents = summaries.Where(s => s.AbsentDays >= threshold || s.ConsecutiveAbsences >= 3 || s.IncompleteSessions > 0).ToList();
                
                var uniqueFlaggedIds = flaggedStudents.Select(s => s.StudentId).Distinct().Count();
                var noTimeOutCount = summaries.Where(s => s.IncompleteSessions > 0).Select(s => s.StudentId).Distinct().Count();
                var gradeLevels = flaggedStudents.Select(s => s.GradeLevel).Distinct().Count();
                var sections = flaggedStudents.Select(s => s.Section).Distinct().Count();

                return new GuidanceDashboardData
                {
                    TotalStudents = students.Count,
                    FlaggedStudents = uniqueFlaggedIds,
                    NoTimeOutCount = noTimeOutCount,
                    GradeLevelsAffected = gradeLevels,
                    SectionsMonitored = sections,
                    StudentsAtRisk = flaggedStudents,
                    AllStudents = students,
                    WeeklyAttendanceRate = await GetSchoolWeeklyAttendanceRateAsync(schoolId),
                    DailyPresenceRate = await GetDailyPresenceRateAsync(schoolId),
                    CaseResolutionRate = await GetCaseResolutionRateAsync(schoolId),
                    OnTimeArrivalRate = await GetOnTimeArrivalRateAsync(schoolId),
                    DailyTrends = await GetDailyAttendanceTrendsAsync(schoolId, days)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data for school {SchoolId}", schoolId);
                throw;
            }
        }

        private async Task<List<DailyTrendData>> GetDailyAttendanceTrendsAsync(string schoolId, int days)
        {
            var trends = new List<DailyTrendData>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Get aggregate stats per day for the last N days
                var query = @"
                    SELECT 
                        sa.Date,
                        COUNT(*) as Total,
                        SUM(CASE WHEN sa.Status IN ('Present', 'Late') THEN 1 ELSE 0 END) as Present,
                        SUM(CASE WHEN sa.Status = 'Absent' THEN 1 ELSE 0 END) as Absent
                    FROM subject_attendance sa
                    INNER JOIN student s ON sa.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                    GROUP BY sa.Date
                    ORDER BY sa.Date ASC";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                cmd.Parameters.AddWithValue("@Days", days);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var date = reader.GetDateTime("Date");
                    var total = Convert.ToInt32(reader["Total"]);
                    var present = Convert.ToInt32(reader["Present"]);
                    var absent = Convert.ToInt32(reader["Absent"]);

                    trends.Add(new DailyTrendData
                    {
                        Date = date,
                        DayName = date.ToString("ddd"),
                        PresenceRate = total > 0 ? (double)present / total * 100 : 0,
                        PresentCount = present,
                        AbsentCount = absent
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance trends for {SchoolId}", schoolId);
            }
            return trends;
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
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT FullName, ParentsNumber FROM student WHERE StudentId = @Id";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@Id", studentId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var name = reader.GetString(0);
                    var phone = reader.IsDBNull(1) ? null : reader.GetString(1);

                    if (!string.IsNullOrEmpty(phone))
                    {
                        // Log the awareness action in guidance_cases
                        await UpdateCaseStatusAsync(studentId, "Summoned", $"Guidance notification sent: {type}");
                        
                        // Fire and forget SMS
                        _ = _smsQueueService.QueueSmsAsync(phone, name, type, DateTime.Now, studentId, "Attendance Awareness");
                        
                        _logger.LogInformation("Awareness notification sent for student {Id} ({Type})", studentId, type);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying student {Id}", studentId);
                return false;
            }
        }

        private async Task<double> GetSchoolWeeklyAttendanceRateAsync(string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        COUNT(sa.SubjectAttendanceId) as Total,
                        SUM(CASE WHEN sa.Status IN ('Present', 'Late') THEN 1 ELSE 0 END) as Present
                    FROM subject_attendance sa
                    INNER JOIN student s ON sa.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    long total = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]);
                    long present = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader[1]);
                    return total > 0 ? (double)present / total * 100 : 0;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating school weekly attendance for {SchoolId}", schoolId);
                return 0;
            }
        }

        private async Task<double> GetDailyPresenceRateAsync(string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT 
                        COUNT(sa.SubjectAttendanceId) as Total,
                        SUM(CASE WHEN sa.Status IN ('Present', 'Late') THEN 1 ELSE 0 END) as Present
                    FROM subject_attendance sa
                    INNER JOIN student s ON sa.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId AND sa.Date = CURDATE()";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    long total = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]);
                    long present = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader[1]);
                    return total > 0 ? (double)present / total * 100 : 0;
                }
                return 0;
            }
            catch { return 0; }
        }

        private async Task<double> GetCaseResolutionRateAsync(string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT 
                        COUNT(*) as Total,
                        SUM(CASE WHEN Status = 'Resolved' THEN 1 ELSE 0 END) as Resolved
                    FROM guidance_cases gc
                    INNER JOIN student s ON gc.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    long total = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]);
                    long resolved = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader[1]);
                    return total > 0 ? (double)resolved / total * 100 : 0;
                }
                return 0;
            }
            catch { return 0; }
        }

        private async Task<double> GetOnTimeArrivalRateAsync(string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT 
                        SUM(CASE WHEN sa.Status IN ('Present', 'Late') THEN 1 ELSE 0 END) as Attended,
                        SUM(CASE WHEN sa.Status = 'Present' THEN 1 ELSE 0 END) as OnTime
                    FROM subject_attendance sa
                    INNER JOIN student s ON sa.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    long attended = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]);
                    long onTime = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader[1]);
                    return attended > 0 ? (double)onTime / attended * 100 : 0;
                }
                return 0;
            }
            catch { return 0; }
        }
    }

    public interface IGuidanceService
    {
        Task<List<StudentInfo>> GetStudentsBySchoolAsync(string schoolId);
        Task<List<AttendanceSummary>> GetAttendanceSummaryAsync(string schoolId, int days = 30);
        Task<GuidanceDashboardData> GetDashboardDataAsync(string schoolId, int days = 30);
        Task<bool> UpdateCaseStatusAsync(string studentId, string status, string? notes = null);
        Task<bool> NotifyStudentAsync(string studentId, string type);
    }

}
