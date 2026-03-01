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

        public async Task<List<ClassOffering>> GetByAdvisorAsync(string advisorId)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT co.ClassOfferingId, co.AdvisorId, t.FullName as AdvisorName, co.SubjectId, s.SubjectName,
                           co.GradeLevel, co.Section, co.Strand, TIME_FORMAT(co.ScheduleStart,'%H:%i:%s'), TIME_FORMAT(co.ScheduleEnd,'%H:%i:%s'),
                           co.TeacherId, t2.FullName as TeacherName, co.CreatedAt
                    FROM class_offering co
                    INNER JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher t ON co.AdvisorId = t.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE co.AdvisorId = @AdvisorId
                    ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@AdvisorId", advisorId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(ReadClassOffering(reader));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class offerings for advisor {AdvisorId}", advisorId);
            }
            return list;
        }

        /// <summary>Offerings not yet assigned to a teacher (for subject teacher to pick).</summary>
        public async Task<List<ClassOffering>> GetAvailableForTeacherAsync(string? schoolId, int? gradeLevel, string? strand)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT co.ClassOfferingId, co.AdvisorId, adv.FullName as AdvisorName, co.SubjectId, s.SubjectName,
                           co.GradeLevel, co.Section, co.Strand, TIME_FORMAT(co.ScheduleStart,'%H:%i:%s'), TIME_FORMAT(co.ScheduleEnd,'%H:%i:%s'),
                           co.TeacherId, t2.FullName as TeacherName, co.CreatedAt
                    FROM class_offering co
                    INNER JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher adv ON co.AdvisorId = adv.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE co.TeacherId IS NULL";
                if (!string.IsNullOrEmpty(schoolId))
                {
                    query += " AND adv.SchoolId = @SchoolId";
                }
                if (gradeLevel.HasValue)
                {
                    query += " AND co.GradeLevel = @GradeLevel";
                }
                if (!string.IsNullOrEmpty(strand))
                {
                    query += " AND (co.Strand = @Strand OR (co.Strand IS NULL AND @Strand IS NULL))";
                }
                query += " ORDER BY co.GradeLevel, co.Section, co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@SchoolId", schoolId ?? "");
                if (gradeLevel.HasValue) cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel.Value);
                cmd.Parameters.AddWithValue("@Strand", strand ?? (object)DBNull.Value);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(ReadClassOffering(reader));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available class offerings");
            }
            return list;
        }

        /// <summary>Classes assigned to this subject teacher.</summary>
        public async Task<List<ClassOffering>> GetByTeacherAsync(string teacherId)
        {
            var list = new List<ClassOffering>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var query = @"
                    SELECT co.ClassOfferingId, co.AdvisorId, t.FullName as AdvisorName, co.SubjectId, s.SubjectName,
                           co.GradeLevel, co.Section, co.Strand, TIME_FORMAT(co.ScheduleStart,'%H:%i:%s'), TIME_FORMAT(co.ScheduleEnd,'%H:%i:%s'),
                           co.TeacherId, t2.FullName as TeacherName, co.CreatedAt
                    FROM class_offering co
                    INNER JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher t ON co.AdvisorId = t.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE co.TeacherId = @TeacherId
                    ORDER BY co.ScheduleStart";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@TeacherId", teacherId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(ReadClassOffering(reader));
                }
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
                var query = @"
                    SELECT co.ClassOfferingId, co.AdvisorId, t.FullName as AdvisorName, co.SubjectId, s.SubjectName,
                           co.GradeLevel, co.Section, co.Strand, TIME_FORMAT(co.ScheduleStart,'%H:%i:%s'), TIME_FORMAT(co.ScheduleEnd,'%H:%i:%s'),
                           co.TeacherId, t2.FullName as TeacherName, co.CreatedAt
                    FROM class_offering co
                    INNER JOIN subject s ON co.SubjectId = s.SubjectId
                    LEFT JOIN teacher t ON co.AdvisorId = t.TeacherId
                    LEFT JOIN teacher t2 ON co.TeacherId = t2.TeacherId
                    WHERE co.ClassOfferingId = @Id";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                    return ReadClassOffering(reader);
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
                var id = Guid.NewGuid().ToString();
                var sql = @"
                    INSERT INTO class_offering (ClassOfferingId, AdvisorId, SubjectId, GradeLevel, Section, Strand, ScheduleStart, ScheduleEnd)
                    VALUES (@Id, @AdvisorId, @SubjectId, @GradeLevel, @Section, @Strand, @Start, @End)";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@AdvisorId", request.AdvisorId);
                cmd.Parameters.AddWithValue("@SubjectId", request.SubjectId);
                cmd.Parameters.AddWithValue("@GradeLevel", request.GradeLevel);
                cmd.Parameters.AddWithValue("@Section", request.Section);
                cmd.Parameters.AddWithValue("@Strand", request.Strand ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Start", request.ScheduleStart);
                cmd.Parameters.AddWithValue("@End", request.ScheduleEnd);
                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Created class offering {Id} by advisor {AdvisorId}", id, request.AdvisorId);
                var created = await GetByIdAsync(id);
                return new ClassOfferingResponse { Success = true, Message = "Class added.", ClassOffering = created };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating class offering");
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
                if (rows == 0)
                    return new ClassOfferingResponse { Success = false, Message = "Class not found or already assigned." };
                var updated = await GetByIdAsync(classOfferingId);
                return new ClassOfferingResponse { Success = true, Message = "You are now assigned to this class.", ClassOffering = updated };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning teacher to class offering");
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<ClassOfferingResponse> UnassignTeacherAsync(string classOfferingId, string advisorId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var sql = "UPDATE class_offering SET TeacherId = NULL WHERE ClassOfferingId = @Id AND AdvisorId = @AdvisorId";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                cmd.Parameters.AddWithValue("@AdvisorId", advisorId);
                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                    return new ClassOfferingResponse { Success = false, Message = "Not found or not your class." };
                return new ClassOfferingResponse { Success = true, Message = "Teacher unassigned." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unassigning teacher");
                return new ClassOfferingResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<ClassOfferingResponse> DeleteAsync(string classOfferingId, string advisorId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var sql = "DELETE FROM class_offering WHERE ClassOfferingId = @Id AND AdvisorId = @AdvisorId";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", classOfferingId);
                cmd.Parameters.AddWithValue("@AdvisorId", advisorId);
                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                    return new ClassOfferingResponse { Success = false, Message = "Not found or not your class." };
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
                var query = @"
                    SELECT s.SubjectId, s.SubjectName, s.GradeLevel, s.Strand
                    FROM subject s
                    WHERE s.GradeLevel = @GradeLevel
                      AND (s.Strand = @Strand OR (s.Strand IS NULL AND @Strand IS NULL))
                    ORDER BY s.SubjectName";
                using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@GradeLevel", gradeLevel);
                cmd.Parameters.AddWithValue("@Strand", strand ?? (object)DBNull.Value);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new TeacherSubjectAssignment
                    {
                        SubjectId = reader.GetString(0),
                        SubjectName = reader.GetString(1),
                        GradeLevel = reader.GetInt32(2),
                        Strand = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ScheduleStart = TimeSpan.Zero,
                        ScheduleEnd = TimeSpan.Zero
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subjects for grade/strand");
            }
            return list;
        }

        private static ClassOffering ReadClassOffering(DbDataReader reader)
        {
            return new ClassOffering
            {
                ClassOfferingId = reader.GetString(0),
                AdvisorId = reader.GetString(1),
                AdvisorName = reader.IsDBNull(2) ? null : reader.GetString(2),
                SubjectId = reader.GetString(3),
                SubjectName = reader.GetString(4),
                GradeLevel = reader.GetInt32(5),
                Section = reader.GetString(6),
                Strand = reader.IsDBNull(7) ? null : reader.GetString(7),
                ScheduleStart = TimeSpan.Parse(reader.GetString(8)),
                ScheduleEnd = TimeSpan.Parse(reader.GetString(9)),
                TeacherId = reader.IsDBNull(10) ? null : reader.GetString(10),
                TeacherName = reader.IsDBNull(11) ? null : reader.GetString(11),
                CreatedAt = reader.GetDateTime(12)
            };
        }
    }
}
