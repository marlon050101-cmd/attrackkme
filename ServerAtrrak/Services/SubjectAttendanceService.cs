using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Services
{
    public class SubjectAttendanceService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<SubjectAttendanceService> _logger;

        public SubjectAttendanceService(Dbconnection dbConnection, ILogger<SubjectAttendanceService> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<List<SubjectAttendanceRecord>> GetByClassAndDateAsync(string teacherSubjectId, DateTime date)
        {
            var list = new List<SubjectAttendanceRecord>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT sa.SubjectAttendanceId, sa.TeacherSubjectId, sa.StudentId, st.FullName as StudentName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    WHERE sa.TeacherSubjectId = @TeacherSubjectId AND sa.Date = @Date
                    ORDER BY st.FullName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TeacherSubjectId", teacherSubjectId);
                cmd.Parameters.AddWithValue("@Date", date.Date);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new SubjectAttendanceRecord
                    {
                        SubjectAttendanceId = reader.GetString(0),
                        TeacherSubjectId = reader.GetString(1),
                        StudentId = reader.GetString(2),
                        StudentName = reader.GetString(3),
                        Date = reader.GetDateTime(4),
                        Status = reader.GetString(5),
                        Remarks = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CreatedAt = reader.GetDateTime(7),
                        UpdatedAt = reader.GetDateTime(8)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subject attendance for class {TeacherSubjectId} date {Date}", teacherSubjectId, date);
            }
            return list;
        }

        public async Task<SubjectAttendanceResponse> SaveBatchAsync(SubjectAttendanceBatchRequest request)
        {
            try
            {
                if (request?.Items == null || request.Items.Count == 0)
                    return new SubjectAttendanceResponse { Success = false, Message = "No items to save." };
                var useOffering = !string.IsNullOrEmpty(request.ClassOfferingId);
                if (!useOffering && string.IsNullOrEmpty(request.TeacherSubjectId))
                    return new SubjectAttendanceResponse { Success = false, Message = "ClassOfferingId or TeacherSubjectId required." };
                var date = request.Date.Date;
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                foreach (var item in request.Items)
                {
                    if (string.IsNullOrEmpty(item.StudentId)) continue;
                    var status = string.IsNullOrEmpty(item.Status) ? "Present" : item.Status;
                    if (status != "Present" && status != "Absent" && status != "Late") status = "Present";
                    if (useOffering)
                    {
                        var sql = @"
                            INSERT INTO subject_attendance (SubjectAttendanceId, ClassOfferingId, TeacherSubjectId, StudentId, Date, Status, Remarks)
                            VALUES (@Id, @COId, NULL, @StudentId, @Date, @Status, @Remarks)
                            ON DUPLICATE KEY UPDATE Status = @Status, Remarks = @Remarks, UpdatedAt = NOW()";
                        using var cmd = new MySqlCommand(sql, connection);
                        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
                        cmd.Parameters.AddWithValue("@COId", request.ClassOfferingId);
                        cmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                        cmd.Parameters.AddWithValue("@Date", date);
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? (object)DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        var sql = @"
                            INSERT INTO subject_attendance (SubjectAttendanceId, TeacherSubjectId, StudentId, Date, Status, Remarks)
                            VALUES (@Id, @TSId, @StudentId, @Date, @Status, @Remarks)
                            ON DUPLICATE KEY UPDATE Status = @Status, Remarks = @Remarks, UpdatedAt = NOW()";
                        using var cmd = new MySqlCommand(sql, connection);
                        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
                        cmd.Parameters.AddWithValue("@TSId", request.TeacherSubjectId);
                        cmd.Parameters.AddWithValue("@StudentId", item.StudentId);
                        cmd.Parameters.AddWithValue("@Date", date);
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? (object)DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                _logger.LogInformation("Saved {Count} subject attendance records date {Date}", request.Items.Count, date);
                return new SubjectAttendanceResponse { Success = true, Message = "Saved." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving subject attendance batch");
                return new SubjectAttendanceResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>Students enrolled in this class: same school as teacher, same GradeLevel, Section, Strand as the subject.</summary>
        public async Task<List<StudentDisplayInfo>> GetClassRosterAsync(string teacherSubjectId)
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
                    INNER JOIN teachersubject ts ON ts.TeacherSubjectId = @TeacherSubjectId
                    INNER JOIN teacher t ON ts.TeacherId = t.TeacherId AND t.SchoolId = st.SchoolId
                    INNER JOIN subject sub ON ts.SubjectId = sub.SubjectId
                    WHERE st.IsActive = 1
                      AND st.GradeLevel = sub.GradeLevel
                      AND st.Section = ts.Section
                      AND (sub.Strand IS NULL OR st.Strand = sub.Strand)
                    ORDER BY st.FullName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TeacherSubjectId", teacherSubjectId);
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
                _logger.LogError(ex, "Error getting class roster for TeacherSubjectId {TeacherSubjectId}", teacherSubjectId);
            }
            return list;
        }

        /// <summary>Roster for a class offering: students under this advisor with same GradeLevel, Section, Strand.</summary>
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
                      AND st.AdvisorId = co.AdvisorId
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
                    SELECT sa.SubjectAttendanceId, sa.ClassOfferingId, sa.TeacherSubjectId, sa.StudentId, st.FullName as StudentName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt
                    FROM subject_attendance sa
                    INNER JOIN student st ON sa.StudentId = st.StudentId
                    WHERE sa.ClassOfferingId = @ClassOfferingId AND sa.Date = @Date
                    ORDER BY st.FullName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ClassOfferingId", classOfferingId);
                cmd.Parameters.AddWithValue("@Date", date.Date);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new SubjectAttendanceRecord
                    {
                        SubjectAttendanceId = reader.GetString(0),
                        ClassOfferingId = reader.IsDBNull(1) ? null : reader.GetString(1),
                        TeacherSubjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        StudentId = reader.GetString(3),
                        StudentName = reader.GetString(4),
                        Date = reader.GetDateTime(5),
                        Status = reader.GetString(6),
                        Remarks = reader.IsDBNull(7) ? null : reader.GetString(7),
                        CreatedAt = reader.GetDateTime(8),
                        UpdatedAt = reader.GetDateTime(9)
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
                    SELECT sa.SubjectAttendanceId, sa.TeacherSubjectId, sa.StudentId, st.FullName,
                           sa.Date, sa.Status, sa.Remarks, sa.CreatedAt, sa.UpdatedAt
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
                        TeacherSubjectId = reader.GetString(1),
                        StudentId = reader.GetString(2),
                        StudentName = reader.GetString(3),
                        Date = reader.GetDateTime(4),
                        Status = reader.GetString(5),
                        Remarks = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CreatedAt = reader.GetDateTime(7),
                        UpdatedAt = reader.GetDateTime(8)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subject attendance history for student {StudentId}", studentId);
            }
            return list;
        }
    }
}
