using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Services
{
    public class SubjectAttendanceService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<SubjectAttendanceService> _logger;
        private readonly SmsQueueService _smsQueueService;

        public SubjectAttendanceService(Dbconnection dbConnection, ILogger<SubjectAttendanceService> logger, SmsQueueService smsQueueService)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _smsQueueService = smsQueueService;
        }


        public async Task<SubjectAttendanceResponse> SaveBatchAsync(SubjectAttendanceBatchRequest request)
        {
            string? lastStudentName = null;
            string? lastStudentId = null;
            try
            {
                if (request?.Items == null || request.Items.Count == 0)
                    return new SubjectAttendanceResponse { Success = false, Message = "No items to save." };
                if (string.IsNullOrEmpty(request.ClassOfferingId))
                    return new SubjectAttendanceResponse { Success = false, Message = "ClassOfferingId required." };
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // --- ROBUST SCHEMA MIGRATION: Ensure Granular Index ---
                try
                {
                    // 1. Get all unique indexes for subject_attendance (except PRIMARY)
                    var getIndexesSql = @"
                        SELECT DISTINCT index_name 
                        FROM information_schema.statistics 
                        WHERE table_schema = DATABASE() 
                          AND table_name = 'subject_attendance' 
                          AND non_unique = 0 
                          AND index_name <> 'PRIMARY'";
                    
                    var existingIndexes = new List<string>();
                    using (var cmdIdx = new MySqlCommand(getIndexesSql, connection))
                    using (var reader = await cmdIdx.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) existingIndexes.Add(reader.GetString(0));
                    }

                    bool hasCorrectIndex = false;
                    foreach (var idxName in existingIndexes)
                    {
                        // Check columns of this index
                        var getColsSql = "SHOW COLUMNS FROM subject_attendance"; // We'll just check statistics for this index
                        var colsQuery = $@"
                            SELECT column_name 
                            FROM information_schema.statistics 
                            WHERE table_schema = DATABASE() 
                              AND table_name = 'subject_attendance' 
                              AND index_name = @IdxName 
                            ORDER BY seq_in_index";
                        
                        var cols = new List<string>();
                        using (var cmdCols = new MySqlCommand(colsQuery, connection))
                        {
                            cmdCols.Parameters.AddWithValue("@IdxName", idxName);
                            using (var rdr = await cmdCols.ExecuteReaderAsync())
                            {
                                while (await rdr.ReadAsync()) cols.Add(rdr.GetString(0).ToLower());
                            }
                        }

                        // If it's the old one (StudentId, Date) or missing ClassOfferingId, we drop it
                        if (!cols.Contains("classofferingid"))
                        {
                            _logger.LogInformation("Dropping broad unique index: {IdxName}", idxName);
                            using var dropCmd = new MySqlCommand($"ALTER TABLE subject_attendance DROP INDEX {idxName}", connection);
                            await dropCmd.ExecuteNonQueryAsync();
                        }
                        else if (cols.Count == 3 && cols.Contains("studentid") && cols.Contains("classofferingid") && cols.Contains("date"))
                        {
                            hasCorrectIndex = true;
                        }
                    }

                    if (!hasCorrectIndex)
                    {
                        _logger.LogInformation("Creating granular unique index: unique_subject_student_date_v2");
                        var createIdxSql = "CREATE UNIQUE INDEX unique_subject_student_date_v2 ON subject_attendance (StudentId, ClassOfferingId, Date)";
                        using var createCmd = new MySqlCommand(createIdxSql, connection);
                        await createCmd.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Index migration info: {Msg}", ex.Message);
                }


                var date = request.Date.Date;

                foreach (var item in request.Items)
                {
                    if (string.IsNullOrEmpty(item.StudentId)) continue;

                    // --- SIMPLE VALIDATION ---
                    // The teacher already selected the correct ClassOffering from their dashboard.
                    // We only verify: (1) the class offering exists, (2) the student is active.
                    // We skip strict GradeLevel/Section/Strand matching which caused false negatives.
                    var validateSql = @"
                        SELECT co.TeacherId 
                        FROM class_offering co
                        WHERE co.ClassOfferingId = @COId
                        AND EXISTS (
                            SELECT 1 FROM student st 
                            WHERE st.StudentId = @StudentId 
                            AND st.IsActive = 1
                        )
                        LIMIT 1";
                    using var vcmd = new MySqlCommand(validateSql, connection);
                    vcmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                    vcmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                    
                    var teacherIdRaw = await vcmd.ExecuteScalarAsync();
                    // Row found = class offering exists and student is active
                    bool isEnrolled = teacherIdRaw != null;
                    // Safely convert: DBNull.Value means TeacherId column is NULL (not yet assigned), not a string
                    string? teacherSubjectId = (teacherIdRaw == null || teacherIdRaw == DBNull.Value) 
                        ? null 
                        : teacherIdRaw.ToString();

                    _logger.LogInformation("Validation: Student={StudentId}, ClassOffering={COId}, Valid={Valid}, TeacherSubjectId={TID}",
                        item.StudentId, request.ClassOfferingId, isEnrolled, teacherSubjectId ?? "NULL");

                    if (!isEnrolled)
                    {
                        return new SubjectAttendanceResponse
                        {
                            Success = false,
                            Message = "Student not found or inactive, or class offering does not exist."
                        };
                    }


                    var status = string.IsNullOrEmpty(item.Status) ? "Present" : item.Status;
                    if (status != "Present" && status != "Absent" && status != "Late") status = "Present";

                    // --- UPSERT LOGIC (INSERT OR UPDATE) ---
                    // This handles the 'Duplicate entry' error by updating the existing record instead of failing.
                    var upsertSql = @"
                        INSERT INTO subject_attendance (
                            SubjectAttendanceId, ClassOfferingId, TeacherSubjectId, StudentId, Date, Status, Remarks, TimeIn, TimeOut
                        )
                        VALUES (@Id, @COId, @TeacherSubjectId, @StudentId, @Date, @Status, @Remarks, @TimeIn, @TimeOut)
                        ON DUPLICATE KEY UPDATE 
                            Status = VALUES(Status),
                            Remarks = VALUES(Remarks),
                            TimeIn = COALESCE(TimeIn, VALUES(TimeIn)),
                            TimeOut = COALESCE(TimeOut, VALUES(TimeOut)),
                            ClassOfferingId = VALUES(ClassOfferingId),
                            TeacherSubjectId = VALUES(TeacherSubjectId),
                            UpdatedAt = NOW()";

                    using var cmd = new MySqlCommand(upsertSql, connection);
                    cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                    cmd.Parameters.AddWithValue("@TeacherSubjectId", (object?)teacherSubjectId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                    cmd.Parameters.AddWithValue("@Date", date);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? (object)DBNull.Value);
                    
                    var timestamp = item.ScanTimestamp;
                    cmd.Parameters.AddWithValue("@TimeIn", item.AttendanceType == "TimeIn" ? timestamp : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@TimeOut", item.AttendanceType == "TimeOut" ? timestamp : (object)DBNull.Value);
                    
                    await cmd.ExecuteNonQueryAsync();


                    // --- SMS NOTIFICATION ---
                    try
                    {
                        var smsDataQuery = @"
                            SELECT st.FullName, st.ParentsNumber, sub.SubjectName
                            FROM student st
                            CROSS JOIN class_offering co
                            INNER JOIN subject sub ON co.SubjectId = sub.SubjectId
                            WHERE st.StudentId = @Sid AND co.ClassOfferingId = @COId
                            LIMIT 1";
                        using var sCmd = new MySqlCommand(smsDataQuery, connection);
                        sCmd.Parameters.AddWithValue("@Sid", item.StudentId);
                        sCmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                        using var sReader = await sCmd.ExecuteReaderAsync();
                        if (await sReader.ReadAsync())
                        {
                            var name = sReader.GetString(0);
                            var phone = sReader.IsDBNull(1) ? null : sReader.GetString(1);
                            var subName = sReader.IsDBNull(2) ? null : sReader.GetString(2);
                            
                            lastStudentName = name;
                            lastStudentId = item.StudentId;

                            if (!string.IsNullOrEmpty(phone))
                            {
                                // Fire and forget SMS queuing
                                _ = _smsQueueService.QueueSmsAsync(phone, name, item.AttendanceType ?? "TimeIn", item.ScanTimestamp, item.StudentId, subName);
                            }
                        }
                        sReader.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to triggers SMS for subject attendance StudentId: {Id}", item.StudentId);
                    }
                }
                _logger.LogInformation("Saved {Count} subject attendance records date {Date}", request.Items.Count, date);

                // Update Daily Summaries for relevant students
                foreach (var item in request.Items.Where(i => !string.IsNullOrEmpty(i.StudentId)))
                {
                    _ = UpdateDailySummaryAsync(item.StudentId, date);
                }

                return new SubjectAttendanceResponse 
                { 
                    Success = true, 
                    Message = "Saved.",
                    StudentName = lastStudentName,
                    StudentId = lastStudentId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving subject attendance batch");
                return new SubjectAttendanceResponse { Success = false, Message = ex.Message };
            }
        }


        /// <summary>Roster for a class offering: students under this adviser with same GradeLevel, Section, Strand.</summary>
        public async Task<List<StudentDisplayInfo>> GetClassRosterByOfferingAsync(string classOfferingId)
        {
            var list = new List<StudentDisplayInfo>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT st.StudentId, st.FullName, st.GradeLevel, st.Section, st.Strand, st.ParentsNumber, st.Gender, s.SchoolName
                    FROM student st
                    INNER JOIN school s ON st.SchoolId = s.SchoolId
                    INNER JOIN class_offering co ON co.ClassOfferingId = @ClassOfferingId
                    WHERE st.IsActive = 1
                      AND st.GradeLevel = co.GradeLevel
                      AND st.Section = co.Section
                      AND (co.Strand IS NULL OR st.Strand = co.Strand)
                    ORDER BY st.FullName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ClassOfferingId", classOfferingId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new StudentDisplayInfo
                    {
                        StudentId = reader.GetString(0),
                        FullName = reader.GetString(1),
                        GradeLevel = reader.GetInt32(2),
                        Section = reader.GetString(3),
                        Strand = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ParentsNumber = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Gender = reader.IsDBNull(6) ? "" : reader.GetString(6),
                        SchoolName = reader.IsDBNull(7) ? "" : reader.GetString(7)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class roster for ClassOfferingId {ClassOfferingId}", classOfferingId);
            }
            return list;
        }

        public async Task<List<SubjectAttendanceRecord>> GetByClassOfferingAndDateAsync(string classOfferingId, DateTime date)
        {
            var list = new List<SubjectAttendanceRecord>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT sa.SubjectAttendanceId, sa.StudentId, st.FullName as StudentName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt, sa.TimeIn, sa.TimeOut, sa.ClassOfferingId
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    WHERE sa.ClassOfferingId = @ClassOfferingId AND sa.Date = @Date
                    ORDER BY sa.UpdatedAt DESC, st.FullName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ClassOfferingId", classOfferingId);
                cmd.Parameters.AddWithValue("@Date", date.Date);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new SubjectAttendanceRecord
                    {
                        SubjectAttendanceId = reader.GetString(0),
                        StudentId = reader.GetString(1),
                        StudentName = reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        Status = reader.GetString(4),
                        Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6),
                        UpdatedAt = reader.GetDateTime(7),
                        TimeIn = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        TimeOut = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        ClassOfferingId = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subject attendance for class offering {Id} date {Date}", classOfferingId, date);
            }
            return list;
        }

        public async Task<List<SubjectAttendanceRecord>> GetStudentHistoryAsync(string studentId, int days = 30)
        {
            var list = new List<SubjectAttendanceRecord>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var fromDate = DateTime.Today.AddDays(-days);
                var query = @"
                    SELECT sa.SubjectAttendanceId, sa.StudentId, st.FullName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt, sa.TimeIn, sa.TimeOut, sa.ClassOfferingId
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    WHERE sa.StudentId = @StudentId AND sa.Date >= @FromDate
                    ORDER BY sa.Date DESC, sa.UpdatedAt DESC";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                cmd.Parameters.AddWithValue("@FromDate", fromDate);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new SubjectAttendanceRecord
                    {
                        SubjectAttendanceId = reader.GetString(0),
                        StudentId = reader.GetString(1),
                        StudentName = reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        Status = reader.GetString(4),
                        Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6),
                        UpdatedAt = reader.GetDateTime(7),
                        TimeIn = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        TimeOut = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        ClassOfferingId = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subject attendance history for student {StudentId}", studentId);
            }
            return list;
        }

        public async Task<List<SubjectAttendanceRecord>> GetTeacherHistoryAsync(string teacherId, int daysCount = 30)
        {
            var list = new List<SubjectAttendanceRecord>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var fromDate = DateTime.Today.AddDays(-daysCount);
                
                // We join with student to get StudentName 
                // and join with class_offering + subject to get SubjectName
                var query = @"
                    SELECT sa.SubjectAttendanceId, sa.StudentId, st.FullName as StudentName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt, sa.TimeIn, sa.TimeOut, 
                           sa.ClassOfferingId, sub.SubjectName
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    INNER JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                    INNER JOIN subject sub ON co.SubjectId = sub.SubjectId
                    WHERE co.TeacherId = @TeacherId AND sa.Date >= @FromDate
                    ORDER BY sa.Date DESC, sa.UpdatedAt DESC";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TeacherId", teacherId);
                cmd.Parameters.AddWithValue("@FromDate", fromDate);
                
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new SubjectAttendanceRecord
                    {
                        SubjectAttendanceId = reader.GetString(0),
                        StudentId = reader.GetString(1),
                        StudentName = reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        Status = reader.GetString(4),
                        Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6),
                        UpdatedAt = reader.GetDateTime(7),
                        TimeIn = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        TimeOut = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        ClassOfferingId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        SubjectName = reader.IsDBNull(11) ? "Subject" : reader.GetString(11)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher subject attendance history. TeacherId: {Id}", teacherId);
            }
            return list;
        }
        public async Task<List<ClassOffering>> GetAdviserSubjectsAsync(string adviserId)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                // Using LEFT JOIN for subject to ensure we still see the offering even if subject link is missing (though SubjectName will be "Unknown")
                // Added TRIM() to ID comparisons just in case there are leading/trailing spaces in the DB
                var query = @"
                    SELECT co.ClassOfferingId, co.SubjectId, COALESCE(s.SubjectName, 'Unknown Subject') as SubjectName, 
                           co.AdviserId, co.TeacherId, 
                           co.GradeLevel, co.Section, co.Strand, co.ScheduleStart, co.ScheduleEnd, co.DayOfWeek,
                           t.FullName as AdviserName, t2.FullName as TeacherName
                    FROM class_offering co
                    LEFT JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher t ON co.AdviserId = t.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE TRIM(co.AdviserId) = @AdviserId OR TRIM(co.TeacherId) = @AdviserId
                    ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId.Trim());
                
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    try
                    {
                        var item = new ClassOffering
                        {
                            ClassOfferingId = reader.GetString(0),
                            SubjectId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            SubjectName = reader.GetString(2),
                            AdviserId = reader.IsDBNull(3) ? null : reader.GetString(3),
                            TeacherId = reader.IsDBNull(4) ? null : reader.GetString(4),
                            GradeLevel = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                            Section = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            Strand = reader.IsDBNull(7) ? null : reader.GetString(7),
                            DayOfWeek = reader.IsDBNull(10) ? null : reader.GetString(10),
                            AdviserName = reader.IsDBNull(11) ? null : reader.GetString(11),
                            TeacherName = reader.IsDBNull(12) ? null : reader.GetString(12)
                        };

                        // Safer TimeSpan handling — cast to MySqlDataReader for GetTimeSpan support
                        var mysqlReader = (MySql.Data.MySqlClient.MySqlDataReader)reader;
                        if (!reader.IsDBNull(8))
                        {
                            item.ScheduleStart = mysqlReader.GetTimeSpan(8);
                        }
                        if (!reader.IsDBNull(9))
                        {
                            item.ScheduleEnd = mysqlReader.GetTimeSpan(9);
                        }

                        list.Add(item);
                    }
                    catch (Exception rowEx)
                    {
                        _logger.LogWarning(rowEx, "Error parsing a class offering row for adviser {AdviserId}", adviserId);
                        // Continue to next row instead of failing everything
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subjects for adviser {AdviserId}", adviserId);
                throw; // Re-throw to let controller handle or return meaningful error
            }
            return list;
        }
        private async Task UpdateDailySummaryAsync(string studentId, DateTime date)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                _logger.LogInformation("Updating Daily Summary for Student: {Id} on {Date}", studentId, date.ToString("yyyy-MM-dd"));

                // 1. Ensure table exists
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS student_daily_summary (
                        StudentId VARCHAR(100),
                        Date DATE,
                        Status VARCHAR(50),
                        TotalSubjects INT,
                        AttendedSubjects INT,
                        TimeIn DATETIME NULL,
                        TimeOut DATETIME NULL,
                        Remarks TEXT NULL,
                        UpdatedAt DATETIME,
                        PRIMARY KEY (StudentId, Date)
                    )";
                using (var ccmd = new MySqlCommand(createTableSql, connection)) await ccmd.ExecuteNonQueryAsync();

                // 2. Count Total Scheduled Subjects for this student TODAY
                // We check for both full day name and 3-letter abbreviation (e.g. 'Monday' and 'Mon')
                var dayFull = date.DayOfWeek.ToString(); // e.g. "Monday"
                var dayShort = dayFull.Substring(0, 3);   // e.g. "Mon"

                var totalSql = @"
                    SELECT COUNT(*) 
                    FROM class_offering co
                    INNER JOIN student s ON s.StudentId = @StudentId
                    WHERE co.GradeLevel = s.GradeLevel 
                      AND co.Section = s.Section
                      AND (co.Strand IS NULL OR s.Strand IS NULL OR co.Strand = s.Strand)
                      AND (
                           co.DayOfWeek IS NULL 
                           OR co.DayOfWeek = '' 
                           OR FIND_IN_SET(@DayFull, co.DayOfWeek) 
                           OR FIND_IN_SET(@DayShort, co.DayOfWeek)
                           OR co.DayOfWeek LIKE CONCAT('%', @DayFull, '%')
                           OR co.DayOfWeek LIKE CONCAT('%', @DayShort, '%')
                      )";
                
                int total = 0;
                using (var tcmd = new MySqlCommand(totalSql, connection))
                {
                    tcmd.Parameters.AddWithValue("@StudentId", studentId);
                    tcmd.Parameters.AddWithValue("@DayFull", dayFull);
                    tcmd.Parameters.AddWithValue("@DayShort", dayShort);
                    total = Convert.ToInt32(await tcmd.ExecuteScalarAsync());
                }

                // 3. Count Attended Subjects and get Time In/Out
                var attendanceSql = @"
                    SELECT 
                        COUNT(DISTINCT ClassOfferingId) as Attended,
                        MIN(TimeIn) as TimeIn,
                        MAX(TimeOut) as TimeOut,
                        MAX(UpdatedAt) as LastSeen
                    FROM subject_attendance
                    WHERE StudentId = @StudentId AND Date = @Date";
                
                int attended = 0;
                DateTime? timeIn = null;
                DateTime? timeOut = null;
                DateTime? lastUpdated = null;

                using (var acmd = new MySqlCommand(attendanceSql, connection))
                {
                    acmd.Parameters.AddWithValue("@StudentId", studentId);
                    acmd.Parameters.AddWithValue("@Date", date.Date);
                    using (var reader = await acmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            attended = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                            timeIn = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                            timeOut = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
                            lastUpdated = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
                        }
                    }
                }

                // 4. Calculate Status
                double percentage = total > 0 ? (double)attended / total * 100 : 0;
                string status = "Absent";
                if (attended > 0)
                {
                    if (percentage >= 75) status = "Whole Day";
                    else if (percentage >= 40) status = "Half Day";
                    else status = "Partially Present";
                }

                _logger.LogInformation("Student {Id}: Total={Total}, Attended={Attended}, Status={Status}", studentId, total, attended, status);

                // 5. Upsert into student_daily_summary
                var upsertSql = @"
                    INSERT INTO student_daily_summary (StudentId, Date, Status, TotalSubjects, AttendedSubjects, TimeIn, TimeOut, Remarks, UpdatedAt)
                    VALUES (@StudentId, @Date, @Status, @Total, @Attended, @In, @Out, @Remarks, NOW())
                    ON DUPLICATE KEY UPDATE 
                        Status = VALUES(Status),
                        TotalSubjects = VALUES(TotalSubjects),
                        AttendedSubjects = VALUES(AttendedSubjects),
                        TimeIn = VALUES(TimeIn),
                        TimeOut = VALUES(TimeOut),
                        Remarks = VALUES(Remarks),
                        UpdatedAt = NOW()";

                using (var ucmd = new MySqlCommand(upsertSql, connection))
                {
                    ucmd.Parameters.AddWithValue("@StudentId", studentId);
                    ucmd.Parameters.AddWithValue("@Date", date.Date);
                    ucmd.Parameters.AddWithValue("@Status", status);
                    ucmd.Parameters.AddWithValue("@Total", total);
                    ucmd.Parameters.AddWithValue("@Attended", attended);
                    ucmd.Parameters.AddWithValue("@In", (object?)timeIn ?? DBNull.Value);
                    ucmd.Parameters.AddWithValue("@Out", (object?)timeOut ?? DBNull.Value);
                    ucmd.Parameters.AddWithValue("@Remarks", DBNull.Value); // For now default to null, or aggregate if needed
                    await ucmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update daily summary for student {Id}", studentId);
            }
        }

        public async Task<List<DailySubjectSummary>> GetDailySubjectSummaryAsync(string adviserId, DateTime date)
        {
            var list = new List<DailySubjectSummary>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        s.StudentId, 
                        s.FullName,
                        COALESCE(ds.TotalSubjects, 0),
                        COALESCE(ds.AttendedSubjects, 0),
                        COALESCE(ds.Status, 'Absent') as Status,
                        ds.TimeIn,
                        ds.TimeOut,
                        ds.Remarks,
                        ds.UpdatedAt as LastSeen
                    FROM student s
                    INNER JOIN teacher t ON t.TeacherId = @AdviserId
                    LEFT JOIN student_daily_summary ds ON s.StudentId = ds.StudentId AND ds.Date = @Date
                    WHERE s.IsActive = 1
                      AND (
                        s.AdviserId = @AdviserId
                        OR (s.SchoolId = t.SchoolId AND s.Section = t.Section AND (t.Gradelvl IS NULL OR s.GradeLevel = t.Gradelvl))
                      )
                    ORDER BY s.FullName";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId.Trim());
                cmd.Parameters.AddWithValue("@Date", date.Date);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new DailySubjectSummary
                    {
                        StudentId = reader.GetString(0),
                        FullName = reader.GetString(1),
                        TotalSubjects = reader.GetInt32(2),
                        AttendedSubjects = reader.GetInt32(3),
                        Status = reader.GetString(4),
                        TimeIn = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5),
                        TimeOut = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6),
                        Remarks = reader.IsDBNull(7) ? (string?)null : reader.GetString(7),
                        LastSeen = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8),
                        Date = date
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily subject summary for adviser {AdviserId}", adviserId);
                throw;
            }
            return list;
        }

        public async Task<bool> UpdateDailySummaryRemarksAsync(string studentId, DateTime date, string remarks)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Check if record exists, if not, create one with default empty status
                var sql = @"
                    INSERT INTO student_daily_summary (StudentId, Date, Remarks, Status, TotalSubjects, AttendedSubjects, UpdatedAt)
                    VALUES (@StudentId, @Date, @Remarks, 'Absent', 0, 0, NOW())
                    ON DUPLICATE KEY UPDATE 
                        Remarks = @Remarks,
                        UpdatedAt = NOW()";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                cmd.Parameters.AddWithValue("@Date", date.Date);
                cmd.Parameters.AddWithValue("@Remarks", remarks);

                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating daily summary remarks for student {Id}", studentId);
                return false;
            }
        }
    }
}
