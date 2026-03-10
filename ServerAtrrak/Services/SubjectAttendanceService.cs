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

                    // Prevent double insert by checking for existing record first
                    var checkExistSql = @"
                        SELECT SubjectAttendanceId FROM subject_attendance 
                        WHERE ClassOfferingId = @COId 
                          AND StudentId = @StudentId AND Date = @Date 
                        LIMIT 1";
                    using var checkCmd = new MySqlCommand(checkExistSql, connection);
                    checkCmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                    checkCmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                    checkCmd.Parameters.AddWithValue("@Date", date);
                    var existingId = await checkCmd.ExecuteScalarAsync();

                    if (existingId != null && existingId != DBNull.Value)
                    {
                        var updateSql = @"
                            UPDATE subject_attendance 
                            SET Status = @Status, 
                                Remarks = @Remarks, 
                                TimeIn = IF(@Type = 'TimeIn', @Time, TimeIn),
                                TimeOut = IF(@Type = 'TimeOut', @Time, TimeOut),
                                ClassOfferingId = COALESCE(@COId, ClassOfferingId),
                                TeacherSubjectId = COALESCE(@TeacherSubjectId, TeacherSubjectId),
                                UpdatedAt = NOW()
                            WHERE SubjectAttendanceId = @Id";
                        using var cmd = new MySqlCommand(updateSql, connection);
                        cmd.Parameters.AddWithValue("@Id", existingId);
                        cmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                        cmd.Parameters.AddWithValue("@TeacherSubjectId", (object?)teacherSubjectId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Type", item.AttendanceType ?? "TimeIn");
                        cmd.Parameters.AddWithValue("@Time", item.ScanTimestamp);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var insertSql = @"
                            INSERT INTO subject_attendance (SubjectAttendanceId, ClassOfferingId, TeacherSubjectId, StudentId, Date, Status, Remarks, TimeIn, TimeOut)
                            VALUES (@Id, @COId, @TeacherSubjectId, @StudentId, @Date, @Status, @Remarks, @TimeIn, @TimeOut)";
                        using var cmd = new MySqlCommand(insertSql, connection);
                        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
                        cmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                        cmd.Parameters.AddWithValue("@TeacherSubjectId", (object?)teacherSubjectId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                        cmd.Parameters.AddWithValue("@Date", date);
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@TimeIn", item.AttendanceType == "TimeIn" ? item.ScanTimestamp : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@TimeOut", item.AttendanceType == "TimeOut" ? item.ScanTimestamp : (object)DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }

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
    }
}
