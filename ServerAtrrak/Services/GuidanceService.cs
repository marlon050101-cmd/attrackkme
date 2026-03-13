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
        private readonly IAcademicPeriodService _periodService;

        public GuidanceService(Dbconnection dbConnection, ILogger<GuidanceService> logger, SmsQueueService smsQueueService, IAcademicPeriodService periodService)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _smsQueueService = smsQueueService;
            _periodService = periodService;
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
                var summaryMap = new Dictionary<string, AttendanceSummary>();

                var activePeriods = await _periodService.GetAllPeriodsAsync(schoolId);
                var activeJhsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Junior High" || p.AcademicLevel == "General"))?.PeriodId;
                var activeShsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Senior High" || p.AcademicLevel == "General"))?.PeriodId;

                // Query 1: Overall stats from student_daily_summary (Source of truth for Absences)
                var dailyQuery = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                           COUNT(ds.Date) as TotalDays,
                           SUM(CASE WHEN ds.Status IN ('Whole Day', 'Half Day') THEN 1 ELSE 0 END) as PresentDays,
                           SUM(CASE WHEN ds.Status = 'Absent' THEN 1 ELSE 0 END) as AbsentDays,
                           MAX(CASE WHEN ds.Status = 'Absent' THEN ds.Date END) as LastAbsentDate,
                           COALESCE((SELECT Status FROM guidance_cases WHERE StudentId = s.StudentId ORDER BY UpdatedAt DESC LIMIT 1), 'Normal') as GuidanceStatus
                    FROM student s
                    INNER JOIN student_daily_summary ds ON s.StudentId = ds.StudentId
                    WHERE s.SchoolId = @SchoolId
                      AND s.IsActive = true
                      AND ds.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                      AND (
                        (@JhsId IS NULL AND @ShsId IS NULL) OR
                        (s.GradeLevel <= 10 AND ds.PeriodId = @JhsId) OR
                        (s.GradeLevel >= 11 AND ds.PeriodId = @ShsId)
                      )
                    GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender";

                using (var command = new MySqlCommand(dailyQuery, connection))
                {
                    command.Parameters.AddWithValue("@SchoolId", schoolId);
                    command.Parameters.AddWithValue("@Days", days);
                    command.Parameters.AddWithValue("@JhsId", (object)activeJhsId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ShsId", (object)activeShsId ?? DBNull.Value);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var studentId = reader.GetString("StudentId");
                        var totalDays = Convert.ToInt32(reader["TotalDays"]);
                        var presentDays = Convert.ToInt32(reader["PresentDays"]);
                        
                        summaryMap[studentId] = new AttendanceSummary
                        {
                            StudentId = studentId,
                            FullName = reader.GetString("FullName"),
                            GradeLevel = reader.GetInt32("GradeLevel"),
                            Section = reader.GetString("Section"),
                            Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                            Gender = reader.IsDBNull("Gender") ? "" : reader.GetString("Gender"),
                            TotalDays = totalDays,
                            PresentDays = presentDays,
                            AbsentDays = Convert.ToInt32(reader["AbsentDays"]),
                            AttendanceRate = totalDays > 0 ? (double)presentDays / totalDays * 100 : 0,
                            GuidanceStatus = reader.GetString("GuidanceStatus"),
                            SubjectName = "Overall",
                            LastAbsentDate = reader.IsDBNull("LastAbsentDate") ? null : reader.GetDateTime("LastAbsentDate")
                        };
                    }
                }

                // Query 2: Cutting Class Risk (NoTimeOut) from subject_attendance
                var subjectQuery = @"
                    SELECT s.StudentId,
                           COUNT(sa.SubjectAttendanceId) as IncompleteSessions,
                           MAX(sa.Date) as LastIncidentDate,
                           COALESCE(sub_co.SubjectName, sub_ts.SubjectName, 'Unknown Subject') as SubjectName
                    FROM student s
                    INNER JOIN subject_attendance sa ON sa.StudentId = s.StudentId 
                    LEFT JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                    LEFT JOIN subject sub_co ON co.SubjectId = sub_co.SubjectId
                    LEFT JOIN teachersubject ts ON sa.TeacherSubjectId = ts.TeacherSubjectId 
                    LEFT JOIN subject sub_ts ON ts.SubjectId = sub_ts.SubjectId
                    WHERE s.SchoolId = @SchoolId 
                      AND sa.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                      AND sa.TimeIn IS NOT NULL AND sa.TimeOut IS NULL
                      AND (
                        (@JhsId IS NULL AND @ShsId IS NULL) OR
                        (s.GradeLevel <= 10 AND sa.PeriodId = @JhsId) OR
                        (s.GradeLevel >= 11 AND sa.PeriodId = @ShsId)
                      )
                    GROUP BY s.StudentId, SubjectName";

                using (var command = new MySqlCommand(subjectQuery, connection))
                {
                    command.Parameters.AddWithValue("@SchoolId", schoolId);
                    command.Parameters.AddWithValue("@Days", days);
                    command.Parameters.AddWithValue("@JhsId", (object)activeJhsId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ShsId", (object)activeShsId ?? DBNull.Value);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var studentId = reader.GetString("StudentId");
                        var incompleteCount = Convert.ToInt32(reader["IncompleteSessions"]);
                        var subName = reader.GetString("SubjectName");

                        if (summaryMap.TryGetValue(studentId, out var existing))
                        {
                            existing.IncompleteSessions += incompleteCount;
                            if (existing.SubjectName == "Overall") existing.SubjectName = subName;
                        }
                    }
                }

                // Final Pass: Guidance Status and Consecutive Absences
                foreach (var s in summaryMap.Values)
                {
                    var studentPeriodId = s.GradeLevel <= 10 ? activeJhsId : activeShsId;
                    s.ConsecutiveAbsences = await GetConsecutiveAbsencesAsync(s.StudentId, null, connection, studentPeriodId);
                }

                // Threshold filtering
                int riskThreshold = days <= 3 ? 1 : 3;
                return summaryMap.Values
                    .Where(s => s.AbsentDays >= riskThreshold || s.ConsecutiveAbsences >= 3 || s.IncompleteSessions > 0)
                    .OrderByDescending(s => s.ConsecutiveAbsences)
                    .ThenByDescending(s => s.AbsentDays)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance summary for school {SchoolId}", schoolId);
                throw;
            }
        }

        private async Task<int> GetConsecutiveAbsencesAsync(string studentId, string? subjectName, MySqlConnection connection, string? periodId)
        {
            try
            {
                // If subjectName is null or Overall, use Daily Summary
                if (string.IsNullOrEmpty(subjectName) || subjectName == "Overall")
                {
                    var query = @"
                        SELECT Status FROM student_daily_summary
                        WHERE StudentId = @StudentId AND Status != 'Not yet timed in'
                        AND (@PeriodId IS NULL OR PeriodId = @PeriodId)
                        ORDER BY Date DESC
                        LIMIT 10";

                    using var cmd = new MySqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    cmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);

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
                        AND (@PeriodId IS NULL OR sa.PeriodId = @PeriodId)
                        ORDER BY sa.Date DESC
                        LIMIT 10";

                    using var cmd = new MySqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    cmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);

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

                var activePeriods = await _periodService.GetAllPeriodsAsync(schoolId);
                var activeJhsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Junior High" || p.AcademicLevel == "General"))?.PeriodId;
                var activeShsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Senior High" || p.AcademicLevel == "General"))?.PeriodId;

                // For the dashboard display, use the SHS period if available, otherwise JHS, otherwise General
                var displayPeriod = activePeriods.FirstOrDefault(p => p.IsActive && p.AcademicLevel == "Senior High") 
                                 ?? activePeriods.FirstOrDefault(p => p.IsActive && p.AcademicLevel == "Junior High")
                                 ?? activePeriods.FirstOrDefault(p => p.IsActive && p.AcademicLevel == "General");

                return new GuidanceDashboardData
                {
                    TotalStudents = students.Count,
                    FlaggedStudents = uniqueFlaggedIds,
                    NoTimeOutCount = noTimeOutCount,
                    GradeLevelsAffected = gradeLevels,
                    SectionsMonitored = sections,
                    StudentsAtRisk = flaggedStudents,
                    AllStudents = students,
                    WeeklyAttendanceRate = await GetMultiLevelAttendanceRateAsync(schoolId, students, activeJhsId, activeShsId),
                    DailyPresenceRate = await GetMultiLevelDailyPresenceRateAsync(schoolId, students, activeJhsId, activeShsId),
                    CaseResolutionRate = await GetMultiLevelCaseResolutionRateAsync(schoolId, activeJhsId, activeShsId),
                    OnTimeArrivalRate = await GetMultiLevelOnTimeArrivalRateAsync(schoolId, students, activeJhsId, activeShsId),
                    DailyTrends = await GetDailyAttendanceTrendsAsync(schoolId, days, students.Count, activeJhsId), // Trends use JHS as default or similar
                    ActivePeriod = displayPeriod
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data for school {SchoolId}", schoolId);
                throw;
            }
        }

        private async Task<List<DailyTrendData>> GetDailyAttendanceTrendsAsync(string schoolId, int days, int totalStudents, string? periodId)
        {
            var trends = new List<DailyTrendData>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        ds.Date,
                        SUM(CASE WHEN ds.Status IN ('Whole Day', 'Half Day') THEN 1 ELSE 0 END) as Present,
                        SUM(CASE WHEN ds.Status = 'Absent' THEN 1 ELSE 0 END) as Absent
                    FROM student_daily_summary ds
                    INNER JOIN student s ON ds.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND ds.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                      AND (@PeriodId IS NULL OR ds.PeriodId = @PeriodId)
                    GROUP BY ds.Date
                    ORDER BY ds.Date ASC";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                cmd.Parameters.AddWithValue("@Days", days);
                cmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var date = reader.GetDateTime("Date");
                    var present = Convert.ToInt32(reader["Present"]);
                    var absent = Convert.ToInt32(reader["Absent"]);

                    trends.Add(new DailyTrendData
                    {
                        Date = date,
                        DayName = date.ToString("ddd"),
                        // Use total population if larger than the number of scan records found
                        PresenceRate = totalStudents > 0 ? (double)present / totalStudents * 100 : 0,
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

                // 1. Check if there's an existing non-resolved case for this student in the current period
                var activePeriod = await _periodService.GetActivePeriodAsync(studentId); // This might be wrong, need to get schoolId from student
                // Let's get schoolId first
                var getSchoolQuery = "SELECT SchoolId FROM student WHERE StudentId = @Sid";
                string? schoolId = null;
                using (var cmd = new MySqlCommand(getSchoolQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Sid", studentId);
                    schoolId = (await cmd.ExecuteScalarAsync())?.ToString();
                }
                
                var activePeriodId = !string.IsNullOrEmpty(schoolId) ? (await _periodService.GetActivePeriodAsync(schoolId))?.PeriodId : null;

                var checkSql = "SELECT CaseId FROM guidance_cases WHERE StudentId = @Sid AND Status != 'Resolved' AND (@PeriodId IS NULL OR PeriodId = @PeriodId) LIMIT 1";
                string? existingCaseId = null;
                using (var checkCmd = new MySqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@Sid", studentId);
                    checkCmd.Parameters.AddWithValue("@PeriodId", (object)activePeriodId ?? DBNull.Value);
                    existingCaseId = (await checkCmd.ExecuteScalarAsync())?.ToString();
                }

                string sql;
                if (!string.IsNullOrEmpty(existingCaseId))
                {
                    // Update existing case
                    sql = @"
                        UPDATE guidance_cases 
                        SET Status = @Status, 
                            Notes = COALESCE(@Notes, Notes),
                            UpdatedAt = NOW()
                        WHERE CaseId = @CaseId";
                    
                    using var cmd = new MySqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@CaseId", existingCaseId);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@Notes", notes ?? (object)DBNull.Value);
                    return await cmd.ExecuteNonQueryAsync() > 0;
                }
                else
                {
                    // Create new case
                    sql = @"
                        INSERT INTO guidance_cases (CaseId, StudentId, Status, LastFlaggedDate, Notes, PeriodId)
                        VALUES (@CaseId, @StudentId, @Status, NOW(), @Notes, @PeriodId)";
                    
                    using var cmd = new MySqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@CaseId", Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@Notes", notes ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@PeriodId", (object)activePeriodId ?? DBNull.Value);
                    return await cmd.ExecuteNonQueryAsync() > 0;
                }
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

        private async Task<double> GetSchoolWeeklyAttendanceRateAsync(string schoolId, int totalStudents, string? periodId)
        {
            if (totalStudents <= 0) return 0;
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Get count of unique dates in the last 7 days for this school for this period
                var dateCountQuery = "SELECT COUNT(DISTINCT Date) FROM student_daily_summary ds INNER JOIN student s ON ds.StudentId = s.StudentId WHERE s.SchoolId = @SchoolId AND ds.Date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY) AND (@PeriodId IS NULL OR ds.PeriodId = @PeriodId)";
                int schoolDays = 0;
                using (var countCmd = new MySqlCommand(dateCountQuery, connection))
                {
                    countCmd.Parameters.AddWithValue("@SchoolId", schoolId);
                    countCmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);
                    schoolDays = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                }
                
                if (schoolDays <= 0) return 0;

                var query = @"
                    SELECT 
                        SUM(CASE WHEN ds.Status IN ('Whole Day', 'Half Day') THEN 1 ELSE 0 END) as Present
                    FROM student_daily_summary ds
                    INNER JOIN student s ON ds.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND ds.Date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)
                      AND (@PeriodId IS NULL OR ds.PeriodId = @PeriodId)";

                using var attendanceCmd = new MySqlCommand(query, connection);
                attendanceCmd.Parameters.AddWithValue("@SchoolId", schoolId);
                attendanceCmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);

                using var reader = await attendanceCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    long present = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]);
                    long totalExpected = (long)totalStudents * schoolDays;
                    return totalExpected > 0 ? (double)present / totalExpected * 100 : 0;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating school weekly attendance for {SchoolId}", schoolId);
                return 0;
            }
        }

        private async Task<double> GetDailyPresenceRateAsync(string schoolId, int totalStudents, string? periodId)
        {
            if (totalStudents <= 0) return 0;
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                // Try today first
                var query = @"
                    SELECT 
                        SUM(CASE WHEN ds.Status IN ('Whole Day', 'Half Day') THEN 1 ELSE 0 END) as Present
                    FROM student_daily_summary ds
                    INNER JOIN student s ON ds.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId AND ds.Date = CURDATE()
                      AND (@PeriodId IS NULL OR ds.PeriodId = @PeriodId)";
                
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                    cmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var hasData = !reader.IsDBNull(0);
                        if (hasData)
                        {
                            long present = Convert.ToInt64(reader["Present"]);
                            return (double)present / totalStudents * 100;
                        }
                    }
                }

                // Fallback to the most recent day if today is empty
                var fallbackQuery = @"
                    SELECT 
                        SUM(CASE WHEN ds.Status IN ('Whole Day', 'Half Day') THEN 1 ELSE 0 END) as Present
                    FROM student_daily_summary ds
                    INNER JOIN student s ON ds.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND ds.Date = (SELECT MAX(Date) FROM student_daily_summary ds2 
                                    INNER JOIN student s2 ON ds2.StudentId = s2.StudentId 
                                    WHERE s2.SchoolId = @SchoolId)";
                
                using (var cmd = new MySqlCommand(fallbackQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                    // Update fallback query to use period
                    var modifiedFallbackSql = fallbackQuery.Replace("WHERE s2.SchoolId = @SchoolId", "WHERE s2.SchoolId = @SchoolId AND (@PeriodId IS NULL OR ds2.PeriodId = @PeriodId)");
                    cmd.CommandText = modifiedFallbackSql;
                    cmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            long present = Convert.ToInt64(reader["Present"]);
                            return (double)present / totalStudents * 100;
                        }
                    }
                }
                return 0;
            }
            catch { return 0; }
        }

        private async Task<double> GetCaseResolutionRateAsync(string schoolId, string? periodId)
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
                    WHERE s.SchoolId = @SchoolId
                      AND (@PeriodId IS NULL OR gc.PeriodId = @PeriodId)";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                cmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);
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

        private async Task<double> GetOnTimeArrivalRateAsync(string schoolId, int totalStudents, string? periodId)
        {
            if (totalStudents <= 0) return 0;
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Get count of unique dates in the last 7 days for this school for this period
                var dateCountQuery = "SELECT COUNT(DISTINCT Date) FROM student_daily_summary ds INNER JOIN student s ON ds.StudentId = s.StudentId WHERE s.SchoolId = @SchoolId AND ds.Date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY) AND (@PeriodId IS NULL OR ds.PeriodId = @PeriodId)";
                int schoolDays = 0;
                using (var countCmd = new MySqlCommand(dateCountQuery, connection))
                {
                    countCmd.Parameters.AddWithValue("@SchoolId", schoolId);
                    countCmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);
                    schoolDays = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                }

                if (schoolDays <= 0) return 0;

                var query = @"
                    SELECT 
                        SUM(CASE WHEN ds.Status = 'Whole Day' AND ds.TimeIn IS NOT NULL THEN 1 ELSE 0 END) as OnTime
                    FROM student_daily_summary ds
                    INNER JOIN student s ON ds.StudentId = s.StudentId
                    WHERE s.SchoolId = @SchoolId 
                      AND ds.Date >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)
                      AND ds.Status IN ('Whole Day', 'Half Day')
                      AND (@PeriodId IS NULL OR ds.PeriodId = @PeriodId)";

                using var onTimeCmd = new MySqlCommand(query, connection);
                onTimeCmd.Parameters.AddWithValue("@SchoolId", schoolId);
                onTimeCmd.Parameters.AddWithValue("@PeriodId", (object)periodId ?? DBNull.Value);
                using var reader = await onTimeCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    long onTime = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]);
                    long totalExpected = (long)totalStudents * schoolDays;
                    return totalExpected > 0 ? (double)onTime / totalExpected * 100 : 0;
                }
                return 0;
            }
            catch { return 0; }
        }

        private async Task<double> GetMultiLevelAttendanceRateAsync(string schoolId, List<StudentInfo> students, string? jhsId, string? shsId)
        {
            var jhsCount = students.Count(s => s.GradeLevel <= 10);
            var shsCount = students.Count(s => s.GradeLevel >= 11);
            if (jhsCount + shsCount == 0) return 0;

            double jhsRate = jhsCount > 0 ? await GetSchoolWeeklyAttendanceRateAsync(schoolId, jhsCount, jhsId) : 0;
            double shsRate = shsCount > 0 ? await GetSchoolWeeklyAttendanceRateAsync(schoolId, shsCount, shsId) : 0;

            return (jhsRate * jhsCount + shsRate * shsCount) / (jhsCount + shsCount);
        }

        private async Task<double> GetMultiLevelDailyPresenceRateAsync(string schoolId, List<StudentInfo> students, string? jhsId, string? shsId)
        {
            var jhsCount = students.Count(s => s.GradeLevel <= 10);
            var shsCount = students.Count(s => s.GradeLevel >= 11);
            if (jhsCount + shsCount == 0) return 0;

            double jhsRate = jhsCount > 0 ? await GetDailyPresenceRateAsync(schoolId, jhsCount, jhsId) : 0;
            double shsRate = shsCount > 0 ? await GetDailyPresenceRateAsync(schoolId, shsCount, shsId) : 0;

            return (jhsRate * jhsCount + shsRate * shsCount) / (jhsCount + shsCount);
        }

        private async Task<double> GetMultiLevelCaseResolutionRateAsync(string schoolId, string? jhsId, string? shsId)
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
                    WHERE s.SchoolId = @SchoolId
                      AND (
                        (@JhsId IS NULL AND @ShsId IS NULL) OR
                        (s.GradeLevel <= 10 AND gc.PeriodId = @JhsId) OR
                        (s.GradeLevel >= 11 AND gc.PeriodId = @ShsId)
                      )";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                cmd.Parameters.AddWithValue("@JhsId", (object)jhsId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ShsId", (object)shsId ?? DBNull.Value);
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

        private async Task<double> GetMultiLevelOnTimeArrivalRateAsync(string schoolId, List<StudentInfo> students, string? jhsId, string? shsId)
        {
            var jhsCount = students.Count(s => s.GradeLevel <= 10);
            var shsCount = students.Count(s => s.GradeLevel >= 11);
            if (jhsCount + shsCount == 0) return 0;

            double jhsRate = jhsCount > 0 ? await GetOnTimeArrivalRateAsync(schoolId, jhsCount, jhsId) : 0;
            double shsRate = shsCount > 0 ? await GetOnTimeArrivalRateAsync(schoolId, shsCount, shsId) : 0;

            return (jhsRate * jhsCount + shsRate * shsCount) / (jhsCount + shsCount);
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
