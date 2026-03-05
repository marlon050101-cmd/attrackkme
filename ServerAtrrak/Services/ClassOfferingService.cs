using System.Data.Common;
using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Services
{
    public class ClassOfferingService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<ClassOfferingService> _logger;

        public ClassOfferingService(Dbconnection dbConnection, ILogger<ClassOfferingService> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        private const string SelectColumns = @"
            co.ClassOfferingId, co.AdviserId, t.FullName as AdviserName, co.SubjectId, s.SubjectName, s.SubjectCode,
            co.GradeLevel, co.Section, co.Strand,
            TIME_FORMAT(co.ScheduleStart,'%H:%i:%s'), TIME_FORMAT(co.ScheduleEnd,'%H:%i:%s'),
            COALESCE(co.DayOfWeek,'Monday,Tuesday,Wednesday,Thursday,Friday') as DayOfWeek,
            co.TeacherId, t2.FullName as TeacherName, co.CreatedAt";

        private const string FromJoins = @"
            FROM class_offering co
            INNER JOIN subject s ON co.SubjectId = s.SubjectId
            LEFT JOIN teacher t ON co.AdviserId = t.TeacherId
            LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId";

        public async Task<List<ClassOffering>> GetAllAsync(string? schoolId)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = $@"SELECT {SelectColumns} {FromJoins}
                    WHERE (@SchoolId IS NULL OR t.SchoolId = @SchoolId)
                    ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", string.IsNullOrEmpty(schoolId) ? (object)DBNull.Value : schoolId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) list.Add(ReadClassOffering(reader));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all class offerings for school {SchoolId}", schoolId);
            }
            return list;
        }

        public async Task<List<ClassOffering>> GetByAdviserAsync(string adviserId)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = $@"SELECT {SelectColumns} {FromJoins}
                    WHERE co.AdviserId = @AdviserId
                    ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) list.Add(ReadClassOffering(reader));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class offerings for adviser {AdviserId}", adviserId);
            }
            return list;
        }

        public async Task<List<ClassOffering>> GetBySectionAsync(string adviserId, string section, int gradeLevel, string? dayOfWeek = null)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var dayFilter = string.IsNullOrEmpty(dayOfWeek)
                    ? ""
                    : "AND (co.DayOfWeek IS NULL OR co.DayOfWeek = '' OR FIND_IN_SET(@DayOfWeek, co.DayOfWeek) > 0)";

                var query = $@"SELECT {SelectColumns} {FromJoins}
                    WHERE co.AdviserId = @AdviserId AND co.Section = @Section AND co.GradeLevel = @GradeLevel
                    {dayFilter}
                    ORDER BY co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId);
                cmd.Parameters.AddWithValue("@Section", section);
                cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel);
                if (!string.IsNullOrEmpty(dayOfWeek))
                    cmd.Parameters.AddWithValue("@DayOfWeek", dayOfWeek);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) list.Add(ReadClassOffering(reader));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class offerings for adviser {AdviserId}, section {Section}, grade {Grade}", adviserId, section, gradeLevel);
            }
            return list;
        }

        public async Task<List<ClassOffering>> GetAvailableForTeacherAsync(string? schoolId, int? gradeLevel, string? strand)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                var query = $@"SELECT {SelectColumns}
                    FROM class_offering co
                    INNER JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher t ON co.AdviserId = t.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE co.TeacherId IS NULL";

                var cmd = new MySqlCommand("", connection);

                if (!string.IsNullOrEmpty(schoolId))
                {
                    // Filter by school, but also allow records where schoolId might be missing/null if that's the only way to see them
                    // Or keep it strict if required, but here we prioritize finding the data.
                    query += " AND (t.SchoolId = @SchoolId OR t.SchoolId IS NULL OR t.SchoolId = '')";
                    cmd.Parameters.AddWithValue("@SchoolId", schoolId.Trim());
                }
                
                if (gradeLevel.HasValue && gradeLevel.Value > 0)
                {
                    query += " AND co.GradeLevel = @GradeLevel";
                    cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel.Value);
                }
                
                if (!string.IsNullOrEmpty(strand))
                {
                    query += " AND (co.Strand = @Strand OR (co.Strand IS NULL OR co.Strand = ''))";
                    cmd.Parameters.AddWithValue("@Strand", strand.Trim());
                }
                
                query += " ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";
                cmd.CommandText = query;

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) list.Add(ReadClassOffering(reader));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available class offerings for School: {SchoolId}, Grade: {Grade}", schoolId, gradeLevel);
            }
            return list;
        }

        public async Task<List<ClassOffering>> GetByTeacherAsync(string teacherId)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = $@"SELECT {SelectColumns} {FromJoins}
                    WHERE co.TeacherId = @TeacherId ORDER BY co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TeacherId", teacherId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) list.Add(ReadClassOffering(reader));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class offerings for teacher {TeacherId}", teacherId);
            }
            return list;
        }

        public async Task<ClassOffering?> GetByIdAsync(string classOfferingId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = $@"SELECT {SelectColumns} {FromJoins} WHERE co.ClassOfferingId = @Id";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync()) return ReadClassOffering(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class offering {Id}", classOfferingId);
            }
            return null;
        }

        public async Task<ClassOfferingResponse> CreateAsync(CreateClassOfferingRequest request)
        {
            try
            {
                if (request.ScheduleEnd <= request.ScheduleStart)
                    return new ClassOfferingResponse { Success = false, Message = "End time must be after start time." };

                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                string finalSubjectId = request.SubjectId;
                if (string.IsNullOrEmpty(finalSubjectId) && !string.IsNullOrEmpty(request.SubjectName))
                    finalSubjectId = await EnsureSubjectExistsAsync(request.SubjectName, request.GradeLevel, request.Strand, connection);

                if (string.IsNullOrEmpty(finalSubjectId))
                    return new ClassOfferingResponse { Success = false, Message = "Subject is required." };

                var dayOfWeek = string.IsNullOrWhiteSpace(request.DayOfWeek)
                    ? "Monday,Tuesday,Wednesday,Thursday,Friday"
                    : request.DayOfWeek;

                var id = Guid.NewGuid().ToString();
                var sql = @"
                    INSERT INTO class_offering (ClassOfferingId, AdviserId, SubjectId, GradeLevel, Section, Strand, ScheduleStart, ScheduleEnd, DayOfWeek)
                    VALUES (@Id, @AdviserId, @SubjectId, @GradeLevel, @Section, @Strand, @Start, @End, @DayOfWeek)";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@AdviserId", request.AdviserId);
                cmd.Parameters.AddWithValue("@SubjectId", finalSubjectId);
                cmd.Parameters.AddWithValue("@GradeLevel", request.GradeLevel);
                cmd.Parameters.AddWithValue("@Section", request.Section);
                cmd.Parameters.AddWithValue("@Strand", request.Strand ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Start", request.ScheduleStart);
                cmd.Parameters.AddWithValue("@End", request.ScheduleEnd);
                cmd.Parameters.AddWithValue("@DayOfWeek", dayOfWeek);
                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Created class offering {Id} (Subject: {SubId}) by adviser {AdviserId}", id, finalSubjectId, request.AdviserId);

                // Auto-approve Adviser account if not already approved
                try
                {
                    // We'll try to find any user associated with this TeacherId that is not approved
                    // AND we also try to update if AdviserId itself was accidentally a UserId
                    var approveSql = @"
                        UPDATE user 
                        SET IsApproved = 1, IsActive = 1 
                        WHERE (TeacherId = @Id OR UserId = @Id)";
                    
                    using var approveCmd = new MySqlCommand(approveSql, connection);
                    approveCmd.Parameters.AddWithValue("@Id", request.AdviserId);
                    int rows = await approveCmd.ExecuteNonQueryAsync();
                    
                    if (rows > 0)
                    {
                        _logger.LogInformation("Auto-approved Adviser user(s) for ID: {Id}. Rows affected: {Rows}", request.AdviserId, rows);
                    }
                    else
                    {
                        _logger.LogWarning("Auto-approval ran but no user rows were updated for ID: {Id}. This teacher might already be approved or record not found in user table.", request.AdviserId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FAILED to auto-approve Adviser {Id} during class creation", request.AdviserId);
                }

                var created = await GetByIdAsync(id);
                return new ClassOfferingResponse { Success = true, Message = "Class added.", ClassOffering = created };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating class offering");
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<ClassOfferingResponse> UpdateAsync(string classOfferingId, string adviserId, UpdateClassOfferingRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Validate ownership
                var existing = await GetByIdAsync(classOfferingId);
                if (existing == null || existing.AdviserId != adviserId)
                    return new ClassOfferingResponse { Success = false, Message = "Class not found or not your class." };

                if (request.ScheduleStart.HasValue && request.ScheduleEnd.HasValue && request.ScheduleEnd <= request.ScheduleStart)
                    return new ClassOfferingResponse { Success = false, Message = "End time must be after start time." };

                // Resolve updated subject ID if SubjectName changed
                string? finalSubjectId = request.SubjectId;
                if (string.IsNullOrEmpty(finalSubjectId) && !string.IsNullOrEmpty(request.SubjectName))
                    finalSubjectId = await EnsureSubjectExistsAsync(request.SubjectName, existing.GradeLevel, existing.Strand, connection);

                var setClauses = new List<string>();
                if (request.ScheduleStart.HasValue) setClauses.Add("ScheduleStart = @Start");
                if (request.ScheduleEnd.HasValue) setClauses.Add("ScheduleEnd = @End");
                if (request.DayOfWeek != null) setClauses.Add("DayOfWeek = @DayOfWeek");
                if (!string.IsNullOrEmpty(finalSubjectId)) setClauses.Add("SubjectId = @SubjectId");

                if (!setClauses.Any())
                    return new ClassOfferingResponse { Success = false, Message = "Nothing to update." };

                var sql = $"UPDATE class_offering SET {string.Join(", ", setClauses)} WHERE ClassOfferingId = @Id AND AdviserId = @AdviserId";
                using var cmd = new MySqlCommand(sql, connection);
                if (request.ScheduleStart.HasValue) cmd.Parameters.AddWithValue("@Start", request.ScheduleStart.Value);
                if (request.ScheduleEnd.HasValue) cmd.Parameters.AddWithValue("@End", request.ScheduleEnd.Value);
                if (request.DayOfWeek != null) cmd.Parameters.AddWithValue("@DayOfWeek", request.DayOfWeek);
                if (!string.IsNullOrEmpty(finalSubjectId)) cmd.Parameters.AddWithValue("@SubjectId", finalSubjectId);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId);
                await cmd.ExecuteNonQueryAsync();

                var updated = await GetByIdAsync(classOfferingId);
                return new ClassOfferingResponse { Success = true, Message = "Class updated.", ClassOffering = updated };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating class offering {Id}", classOfferingId);
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<ClassOfferingResponse> AssignTeacherAsync(string classOfferingId, string teacherId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var sql = "UPDATE class_offering SET TeacherId = @TeacherId WHERE ClassOfferingId = @Id AND TeacherId IS NULL";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@TeacherId", teacherId);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                var rows = await cmd.ExecuteNonQueryAsync();
                
                if (rows == 0) return new ClassOfferingResponse { Success = false, Message = "Class not found or already assigned." };

                // Auto-approve Teacher account if not already approved
                try
                {
                    var approveSql = @"
                        UPDATE user 
                        SET IsApproved = 1, IsActive = 1 
                        WHERE (TeacherId = @Id OR UserId = @Id)";
                    
                    using var approveCmd = new MySqlCommand(approveSql, connection);
                    approveCmd.Parameters.AddWithValue("@Id", teacherId);
                    int approveRows = await approveCmd.ExecuteNonQueryAsync();
                    
                    if (approveRows > 0)
                    {
                        _logger.LogInformation("Auto-approved Teacher user(s) for ID: {Id} upon assignment. Rows affected: {Rows}", teacherId, approveRows);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FAILED to auto-approve Teacher {Id} during assignment", teacherId);
                }

                var updated = await GetByIdAsync(classOfferingId);
                return new ClassOfferingResponse { Success = true, Message = "You are now assigned to this class.", ClassOffering = updated };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning teacher to class offering");
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<ClassOfferingResponse> UnassignTeacherAsync(string classOfferingId, string adviserId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var sql = "UPDATE class_offering SET TeacherId = NULL WHERE ClassOfferingId = @Id AND AdviserId = @AdviserId";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId);
                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0) return new ClassOfferingResponse { Success = false, Message = "Not found or not your class." };
                return new ClassOfferingResponse { Success = true, Message = "Teacher unassigned." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unassigning teacher");
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<ClassOfferingResponse> DeleteAsync(string classOfferingId, string adviserId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var sql = "DELETE FROM class_offering WHERE ClassOfferingId = @Id AND AdviserId = @AdviserId";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                cmd.Parameters.AddWithValue("@AdviserId", adviserId);
                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0) return new ClassOfferingResponse { Success = false, Message = "Not found or not your class." };
                return new ClassOfferingResponse { Success = true, Message = "Class removed." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting class offering");
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<List<TeacherSubjectAssignment>> GetSubjectsForGradeStrandAsync(int gradeLevel, string? strand)
        {
            var list = new List<TeacherSubjectAssignment>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                string? normalizedStrand = string.IsNullOrWhiteSpace(strand) ? null : strand.Trim();
                string query; MySqlCommand cmd;
                if (normalizedStrand != null)
                {
                    query = @"SELECT s.SubjectId, s.SubjectName, s.GradeLevel, s.Strand, s.SubjectCode FROM subject s
                        WHERE s.GradeLevel = @GradeLevel AND (s.Strand = @Strand OR s.Strand IS NULL OR s.Strand = '')
                        ORDER BY s.SubjectName";
                    cmd = new MySqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel);
                    cmd.Parameters.AddWithValue("@Strand", normalizedStrand);
                }
                else
                {
                    query = @"SELECT s.SubjectId, s.SubjectName, s.GradeLevel, s.Strand, s.SubjectCode FROM subject s
                        WHERE s.GradeLevel = @GradeLevel ORDER BY s.SubjectName";
                    cmd = new MySqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel);
                }
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    list.Add(new TeacherSubjectAssignment { 
                        SubjectId = reader.GetString(0), 
                        SubjectName = reader.GetString(1), 
                        GradeLevel = reader.GetInt32(2), 
                        Strand = reader.IsDBNull(3) ? null : reader.GetString(3), 
                        SubjectCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ScheduleStart = TimeSpan.Zero, 
                        ScheduleEnd = TimeSpan.Zero 
                    });
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting subjects for grade/strand"); }
            return list;
        }

        public async Task<List<TeacherSubjectAssignment>> SearchSubjectsAsync(int gradeLevel, string? strand, string? keyword)
        {
            var list = new List<TeacherSubjectAssignment>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                string? normalizedStrand = string.IsNullOrWhiteSpace(strand) ? null : strand.Trim();
                string? normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword.Trim()}%";
                var conditions = new List<string> { "s.GradeLevel = @GradeLevel" };
                if (normalizedStrand != null) conditions.Add("(s.Strand = @Strand OR s.Strand IS NULL OR s.Strand = '')");
                if (normalizedKeyword != null) conditions.Add("s.SubjectName LIKE @Keyword");
                var query = $@"SELECT s.SubjectId, s.SubjectName, s.GradeLevel, s.Strand, s.SubjectCode FROM subject s
                    WHERE {string.Join(" AND ", conditions)} ORDER BY s.SubjectName LIMIT 20";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel);
                if (normalizedStrand != null) cmd.Parameters.AddWithValue("@Strand", normalizedStrand);
                if (normalizedKeyword != null) cmd.Parameters.AddWithValue("@Keyword", normalizedKeyword);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    list.Add(new TeacherSubjectAssignment { 
                        SubjectId = reader.GetString(0), 
                        SubjectName = reader.GetString(1), 
                        GradeLevel = reader.GetInt32(2), 
                        Strand = reader.IsDBNull(3) ? null : reader.GetString(3), 
                        SubjectCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ScheduleStart = TimeSpan.Zero, 
                        ScheduleEnd = TimeSpan.Zero 
                    });
            }
            catch (Exception ex) { _logger.LogError(ex, "Error searching subjects"); }
            return list;
        }

        private async Task<string> EnsureSubjectExistsAsync(string name, int grade, string? strand, MySqlConnection conn)
        {
            var checkSql = "SELECT SubjectId FROM subject WHERE SubjectName = @Name AND GradeLevel = @Grade AND (Strand = @Strand OR (@Strand IS NULL AND (Strand IS NULL OR Strand = '')))";
            using (var checkCmd = new MySqlCommand(checkSql, conn))
            {
                checkCmd.Parameters.AddWithValue("@Name", name);
                checkCmd.Parameters.AddWithValue("@Grade", grade);
                checkCmd.Parameters.AddWithValue("@Strand", strand ?? (object)DBNull.Value);
                var existingId = await checkCmd.ExecuteScalarAsync();
                if (existingId != null) return existingId.ToString()!;
            }
            var id = Guid.NewGuid().ToString();
            var insertSql = "INSERT INTO subject (SubjectId, SubjectName, GradeLevel, Strand) VALUES (@Id, @Name, @Grade, @Strand)";
            using (var insertCmd = new MySqlCommand(insertSql, conn))
            {
                insertCmd.Parameters.AddWithValue("@Id", id);
                insertCmd.Parameters.AddWithValue("@Name", name);
                insertCmd.Parameters.AddWithValue("@Grade", grade);
                insertCmd.Parameters.AddWithValue("@Strand", strand ?? (object)DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Created new subject '{Name}' (Grade {Grade}) with ID {Id}", name, grade, id);
            }
            return id;
        }

        private static ClassOffering ReadClassOffering(DbDataReader reader)
        {
            return new ClassOffering
            {
                ClassOfferingId = reader.GetString(0),
                AdviserId = reader.GetString(1),
                AdviserName = reader.IsDBNull(2) ? null : reader.GetString(2),
                SubjectId = reader.GetString(3),
                SubjectName = reader.GetString(4),
                SubjectCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                GradeLevel = reader.GetInt32(6),
                Section = reader.GetString(7),
                Strand = reader.IsDBNull(8) ? null : reader.GetString(8),
                ScheduleStart = TimeSpan.Parse(reader.GetString(9)),
                ScheduleEnd = TimeSpan.Parse(reader.GetString(10)),
                DayOfWeek = reader.IsDBNull(11) ? "Monday,Tuesday,Wednesday,Thursday,Friday" : reader.GetString(11),
                TeacherId = reader.IsDBNull(12) ? null : reader.GetString(12),
                TeacherName = reader.IsDBNull(13) ? null : reader.GetString(13),
                CreatedAt = reader.GetDateTime(14)
            };
        }
    }
}
