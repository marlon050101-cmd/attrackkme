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
        private readonly IAcademicPeriodService _periodService;

        public SubjectAttendanceService(Dbconnection dbConnection, ILogger<SubjectAttendanceService> logger, SmsQueueService smsQueueService, IAcademicPeriodService periodService)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _smsQueueService = smsQueueService;
            _periodService = periodService;
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
                    // 1. Check for PeriodId in subject_attendance (already added in sql but for robustness)
                    var checkPeriodSa = @"
                        SELECT COUNT(*) FROM information_schema.columns 
                        WHERE table_schema = DATABASE() AND table_name = 'subject_attendance' AND column_name = 'PeriodId'";
                    using (var cmdP = new MySqlCommand(checkPeriodSa, connection))
                    {
                        if (Convert.ToInt32(await cmdP.ExecuteScalarAsync()) == 0)
                        {
                            using var addCmd = new MySqlCommand("ALTER TABLE subject_attendance ADD COLUMN PeriodId VARCHAR(100)", connection);
                            await addCmd.ExecuteNonQueryAsync();
                        }
                    }

                    // 2. Check for PeriodId in student_daily_summary and update PK
                    var checkPeriodDs = @"
                        SELECT COUNT(*) FROM information_schema.columns 
                        WHERE table_schema = DATABASE() AND table_name = 'student_daily_summary' AND column_name = 'PeriodId'";
                    using (var cmdP = new MySqlCommand(checkPeriodDs, connection))
                    {
                        if (Convert.ToInt32(await cmdP.ExecuteScalarAsync()) == 0)
                        {
                            try {
                                using var addCmd = new MySqlCommand("ALTER TABLE student_daily_summary ADD COLUMN PeriodId VARCHAR(100)", connection);
                                await addCmd.ExecuteNonQueryAsync();
                                using var dropPk = new MySqlCommand("ALTER TABLE student_daily_summary DROP PRIMARY KEY", connection);
                                await dropPk.ExecuteNonQueryAsync();
                                using var addPk = new MySqlCommand("ALTER TABLE student_daily_summary ADD PRIMARY KEY (StudentId, Date, PeriodId)", connection);
                                await addPk.ExecuteNonQueryAsync();
                            } catch (Exception pkEx) { _logger.LogWarning("Daily Summary PK update: {Msg}", pkEx.Message); }
                        }
                    }

                    // 3. Update Indexes (Old Logic)
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
                    _logger.LogWarning("Migration info: {Msg}", ex.Message);
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
                        SELECT co.TeacherId, co.PeriodId 
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
                    
                    string? teacherSubjectId = null;
                    string? periodId = null;
                    bool isEnrolled = false;

                    using (var reader = await vcmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            isEnrolled = true;
                            teacherSubjectId = reader.IsDBNull(0) ? null : reader.GetString(0);
                            periodId = reader.IsDBNull(1) ? null : reader.GetString(1);
                        }
                    }

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
                            SubjectAttendanceId, ClassOfferingId, TeacherSubjectId, StudentId, Date, Status, Remarks, TimeIn, TimeOut, PeriodId
                        )
                        VALUES (@Id, @COId, @TeacherSubjectId, @StudentId, @Date, @Status, @Remarks, @TimeIn, @TimeOut, @PeriodId)
                        ON DUPLICATE KEY UPDATE 
                            Status = VALUES(Status),
                            Remarks = VALUES(Remarks),
                            TimeIn = COALESCE(TimeIn, VALUES(TimeIn)),
                            TimeOut = COALESCE(TimeOut, VALUES(TimeOut)),
                            ClassOfferingId = VALUES(ClassOfferingId),
                            TeacherSubjectId = VALUES(TeacherSubjectId),
                            PeriodId = VALUES(PeriodId),
                            UpdatedAt = NOW()";

                    using var cmd = new MySqlCommand(upsertSql, connection);
                    cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                    cmd.Parameters.AddWithValue("@TeacherSubjectId", (object?)teacherSubjectId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                    cmd.Parameters.AddWithValue("@Date", date);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@PeriodId", (object?)periodId ?? DBNull.Value);
                    
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

        public async Task<List<SubjectAttendanceRecord>> GetByClassOfferingAndDateAsync(string classOfferingId, DateTime date, string? adviserId = null)
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
                    WHERE sa.ClassOfferingId = @ClassOfferingId 
                      AND sa.Date = @Date
                      AND (@AdviserId IS NULL OR (
                          TRIM(st.AdviserId) = @AdviserId
                          OR st.Section IN (SELECT DISTINCT Section FROM class_offering WHERE TRIM(AdviserId) = @AdviserId)
                      ))
                    ORDER BY sa.UpdatedAt DESC, st.FullName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ClassOfferingId", classOfferingId);
                cmd.Parameters.AddWithValue("@Date", date.Date);
                cmd.Parameters.AddWithValue("@AdviserId", string.IsNullOrEmpty(adviserId) ? (object)DBNull.Value : adviserId.Trim());
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
                // Get student's school and active period
                // Get student's grade level
                var gradeQuery = "SELECT GradeLevel FROM student WHERE StudentId = @Sid";
                int gradeLevel = 0;
                using (var gcmd = new MySqlCommand(gradeQuery, connection))
                {
                    gcmd.Parameters.AddWithValue("@Sid", studentId);
                    gradeLevel = Convert.ToInt32(await gcmd.ExecuteScalarAsync());
                }
                
                var periodId = schoolId != null ? (await _periodService.GetActivePeriodAsync(schoolId, gradeLevel))?.PeriodId : null;

                var fromDate = DateTime.Today.AddDays(-days);
                var query = @"
                    SELECT sa.SubjectAttendanceId, sa.StudentId, st.FullName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt, sa.TimeIn, sa.TimeOut, sa.ClassOfferingId
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    WHERE sa.StudentId = @StudentId AND sa.Date >= @FromDate
                      AND (@PeriodId IS NULL OR sa.PeriodId = @PeriodId)
                    ORDER BY sa.Date DESC, sa.UpdatedAt DESC";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                cmd.Parameters.AddWithValue("@FromDate", fromDate);
                cmd.Parameters.AddWithValue("@PeriodId", (object?)periodId ?? DBNull.Value);
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
                // Get teacher's school and active period
                var activePeriods = !string.IsNullOrEmpty(schoolId) ? await _periodService.GetAllPeriodsAsync(schoolId) : new List<AcademicPeriod>();
                var activeJhsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Junior High" || p.AcademicLevel == "General"))?.PeriodId;
                var activeShsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Senior High" || p.AcademicLevel == "General"))?.PeriodId;

                var fromDate = DateTime.Today.AddDays(-daysCount);
                
                var query = @"
                    SELECT sa.SubjectAttendanceId, sa.StudentId, st.FullName as StudentName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt, sa.TimeIn, sa.TimeOut, 
                           sa.ClassOfferingId, sub.SubjectName
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    INNER JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                    INNER JOIN subject sub ON co.SubjectId = sub.SubjectId
                    WHERE co.TeacherId = @TeacherId AND sa.Date >= @FromDate
                      AND (
                        (@JhsId IS NULL AND @ShsId IS NULL) OR
                        (st.GradeLevel <= 10 AND sa.PeriodId = @JhsId) OR
                        (st.GradeLevel >= 11 AND sa.PeriodId = @ShsId)
                      )
                    ORDER BY sa.Date DESC, sa.UpdatedAt DESC";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TeacherId", teacherId);
                cmd.Parameters.AddWithValue("@FromDate", fromDate);
                cmd.Parameters.AddWithValue("@JhsId", (object?)activeJhsId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ShsId", (object?)activeShsId ?? DBNull.Value);
                
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
                // Get adviser's school and active period
                var activePeriods = !string.IsNullOrEmpty(schoolId) ? await _periodService.GetAllPeriodsAsync(schoolId) : new List<AcademicPeriod>();
                var activeJhsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Junior High" || p.AcademicLevel == "General"))?.PeriodId;
                var activeShsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Senior High" || p.AcademicLevel == "General"))?.PeriodId;

                var query = @"
                    SELECT co.ClassOfferingId, co.SubjectId, COALESCE(s.SubjectName, 'Unknown Subject') as SubjectName, 
                           co.AdviserId, co.TeacherId, 
                           co.GradeLevel, co.Section, co.Strand, co.ScheduleStart, co.ScheduleEnd, co.DayOfWeek,
                           t.FullName as AdviserName, t2.FullName as TeacherName
                    FROM class_offering co
                    LEFT JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher t ON co.AdviserId = t.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE (TRIM(co.AdviserId) = @AdviserId OR TRIM(co.TeacherId) = @AdviserId)
                      AND (
                        (@JhsId IS NULL AND @ShsId IS NULL) OR
                        (co.GradeLevel <= 10 AND co.PeriodId = @JhsId) OR
                        (co.GradeLevel >= 11 AND co.PeriodId = @ShsId)
                      )
                    ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId.Trim());
                cmd.Parameters.AddWithValue("@JhsId", (object?)activeJhsId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ShsId", (object?)activeShsId ?? DBNull.Value);
                
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

                // 1. Get student's school and active period
                // 1. Get student's school and grade level to find correct active period
                var studentInfoQuery = "SELECT SchoolId, GradeLevel FROM student WHERE StudentId = @Sid";
                string? schoolId = null;
                int gradeLevel = 0;
                using (var scmd = new MySqlCommand(studentInfoQuery, connection))
                {
                    scmd.Parameters.AddWithValue("@Sid", studentId);
                    using var sreader = await scmd.ExecuteReaderAsync();
                    if (await sreader.ReadAsync())
                    {
                        schoolId = sreader.GetString(0);
                        gradeLevel = sreader.GetInt32(1);
                    }
                }
                if (string.IsNullOrEmpty(schoolId)) return;

                var period = await _periodService.GetActivePeriodAsync(schoolId, gradeLevel);
                var periodId = period?.PeriodId;
                if (string.IsNullOrEmpty(periodId)) return;

                // 2. Count Total Scheduled Subjects for this student TODAY (filtered by PeriodId)
                var dayFull = date.DayOfWeek.ToString();
                var dayShort = dayFull.Substring(0, 3);

                var totalSql = @"
                    SELECT COUNT(*) 
                    FROM class_offering co
                    INNER JOIN student s ON s.StudentId = @StudentId
                    WHERE co.GradeLevel = s.GradeLevel 
                      AND co.Section = s.Section
                      AND co.PeriodId = @PeriodId
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
                    tcmd.Parameters.AddWithValue("@PeriodId", periodId);
                    tcmd.Parameters.AddWithValue("@DayFull", dayFull);
                    tcmd.Parameters.AddWithValue("@DayShort", dayShort);
                    total = Convert.ToInt32(await tcmd.ExecuteScalarAsync());
                }

                // 3. Count Attended Subjects and get Time In/Out (filtered by PeriodId)
                var attendanceSql = @"
                    SELECT 
                        COUNT(DISTINCT ClassOfferingId) as Attended,
                        MIN(TimeIn) as TimeIn,
                        MAX(TimeOut) as TimeOut,
                        MAX(UpdatedAt) as LastSeen
                    FROM subject_attendance
                    WHERE StudentId = @StudentId AND Date = @Date AND PeriodId = @PeriodId";
                
                int attended = 0;
                DateTime? timeIn = null;
                DateTime? timeOut = null;

                using (var acmd = new MySqlCommand(attendanceSql, connection))
                {
                    acmd.Parameters.AddWithValue("@StudentId", studentId);
                    acmd.Parameters.AddWithValue("@Date", date.Date);
                    acmd.Parameters.AddWithValue("@PeriodId", periodId);
                    using (var reader = await acmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            attended = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                            timeIn = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                            timeOut = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
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

                // 5. Upsert into student_daily_summary
                var upsertSql = @"
                    INSERT INTO student_daily_summary (StudentId, Date, Status, TotalSubjects, AttendedSubjects, TimeIn, TimeOut, PeriodId, UpdatedAt)
                    VALUES (@StudentId, @Date, @Status, @Total, @Attended, @In, @Out, @PeriodId, NOW())
                    ON DUPLICATE KEY UPDATE 
                        Status = VALUES(Status),
                        TotalSubjects = VALUES(TotalSubjects),
                        AttendedSubjects = VALUES(AttendedSubjects),
                        TimeIn = VALUES(TimeIn),
                        TimeOut = VALUES(TimeOut),
                        PeriodId = VALUES(PeriodId),
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
                    ucmd.Parameters.AddWithValue("@PeriodId", periodId);
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

                // Get adviser's school and active period
                var activePeriods = !string.IsNullOrEmpty(schoolId) ? await _periodService.GetAllPeriodsAsync(schoolId) : new List<AcademicPeriod>();
                var activeJhsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Junior High" || p.AcademicLevel == "General"))?.PeriodId;
                var activeShsId = activePeriods.FirstOrDefault(p => p.IsActive && (p.AcademicLevel == "Senior High" || p.AcademicLevel == "General"))?.PeriodId;

                var dayFull = date.DayOfWeek.ToString();
                var dayShort = dayFull.Substring(0, 3);

                var query = @"
                    SELECT 
                        s.StudentId, 
                        s.FullName,
                        COALESCE(ds.TotalSubjects, (
                            SELECT COUNT(*) 
                            FROM class_offering co
                            WHERE co.GradeLevel = s.GradeLevel 
                              AND co.Section = s.Section
                              AND (
                                (s.GradeLevel <= 10 AND co.PeriodId = @JhsId) OR
                                (s.GradeLevel >= 11 AND co.PeriodId = @ShsId)
                              )
                              AND (co.Strand IS NULL OR s.Strand IS NULL OR co.Strand = s.Strand)
                              AND (
                                   co.DayOfWeek IS NULL 
                                   OR co.DayOfWeek = '' 
                                   OR FIND_IN_SET(@DayFull, co.DayOfWeek) 
                                   OR FIND_IN_SET(@DayShort, co.DayOfWeek)
                                   OR co.DayOfWeek LIKE CONCAT('%', @DayFull, '%')
                                   OR co.DayOfWeek LIKE CONCAT('%', @DayShort, '%')
                              )
                        )),
                        COALESCE(ds.AttendedSubjects, 0),
                        COALESCE(ds.Status, 'Not yet timed in') as Status,
                        ds.TimeIn,
                        ds.TimeOut,
                        ds.Remarks,
                        ds.UpdatedAt as LastSeen
                    FROM student s
                    LEFT JOIN student_daily_summary ds ON s.StudentId = ds.StudentId AND ds.Date = @Date AND (
                        (s.GradeLevel <= 10 AND ds.PeriodId = @JhsId) OR
                        (s.GradeLevel >= 11 AND ds.PeriodId = @ShsId)
                    )
                    WHERE s.IsActive = 1
                      AND (
                        TRIM(s.AdviserId) = @AdviserId
                        OR s.Section IN (SELECT DISTINCT Section FROM class_offering WHERE TRIM(AdviserId) = @AdviserId AND (
                            (GradeLevel <= 10 AND PeriodId = @JhsId) OR
                            (GradeLevel >= 11 AND PeriodId = @ShsId)
                        ))
                      )
                    ORDER BY s.FullName";

                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId.Trim());
                cmd.Parameters.AddWithValue("@Date", date.Date);
                cmd.Parameters.AddWithValue("@JhsId", (object?)activeJhsId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ShsId", (object?)activeShsId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DayFull", dayFull);
                cmd.Parameters.AddWithValue("@DayShort", dayShort);

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

        public async Task<bool> UpdateDailySummaryStatusAsync(string studentId, DateTime date, string status)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO student_daily_summary (StudentId, Date, Status, TotalSubjects, AttendedSubjects, UpdatedAt)
                    VALUES (@StudentId, @Date, @Status, 0, 0, NOW())
                    ON DUPLICATE KEY UPDATE 
                        Status = @Status,
                        UpdatedAt = NOW()";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                cmd.Parameters.AddWithValue("@Date", date.Date);
                cmd.Parameters.AddWithValue("@Status", status);

                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating daily summary status for student {Id}", studentId);
                return false;
            }
        }
    }
}
