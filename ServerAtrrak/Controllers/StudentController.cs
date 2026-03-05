using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using MySql.Data.MySqlClient;
using System.Data;
using ServerAtrrak.Data;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<StudentController> _logger;

        public StudentController(IConfiguration configuration, Dbconnection dbConnection, ILogger<StudentController> logger)
        {
            _configuration = configuration;
            _dbConnection = dbConnection;
            _logger = logger;
        }

        [HttpGet("{studentId}")]
        public async Task<ActionResult<Student>> GetStudent(string studentId)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("dbconstring");
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT StudentId, FullName, Email, GradeLevel, Section, Strand, SchoolId, ParentsNumber, QRImage, AdviserId, CreatedAt, UpdatedAt, IsActive
                    FROM student 
                    WHERE StudentId = @StudentId AND IsActive = 1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@StudentId", studentId);

                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    var student = new Student
                    {
                        StudentId = reader.GetString("StudentId"),
                        FullName = reader.GetString("FullName"),
                        Email = reader.GetString("Email"),
                        GradeLevel = reader.GetInt32("GradeLevel"),
                        Section = reader.GetString("Section"),
                        Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                        SchoolId = reader.GetString("SchoolId"),
                        ParentsNumber = reader.GetString("ParentsNumber"),
                        QRImage = reader.IsDBNull("QRImage") ? null : reader.GetString("QRImage"),
                        AdviserId = reader.IsDBNull("AdviserId") ? null : reader.GetString("AdviserId"),
                        CreatedAt = reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.GetDateTime("UpdatedAt"),
                        IsActive = reader.GetBoolean("IsActive")
                    };

                    return Ok(student);
                }

                return NotFound($"Student with ID {studentId} not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student {StudentId}: {ErrorMessage}", studentId, ex.Message);
                return StatusCode(500, "Error retrieving student information");
            }
        }

        [HttpGet("search/{name}")]
        public async Task<ActionResult<List<Student>>> SearchStudents(string name)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("dbconstring");
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT StudentId, FullName, Email, GradeLevel, Section, Strand, SchoolId, ParentsNumber, QRImage, AdviserId, CreatedAt, UpdatedAt, IsActive
                    FROM student 
                    WHERE (FullName LIKE @SearchName OR StudentId LIKE @SearchName) AND IsActive = 1
                    ORDER BY FullName
                    LIMIT 50";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SearchName", $"%{name}%");

                var students = new List<Student>();

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    students.Add(new Student
                    {
                        StudentId = reader.GetString("StudentId"),
                        FullName = reader.GetString("FullName"),
                        Email = reader.GetString("Email"),
                        GradeLevel = reader.GetInt32("GradeLevel"),
                        Section = reader.GetString("Section"),
                        Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                        SchoolId = reader.GetString("SchoolId"),
                        ParentsNumber = reader.GetString("ParentsNumber"),
                        QRImage = reader.IsDBNull("QRImage") ? null : reader.GetString("QRImage"),
                        AdviserId = reader.IsDBNull("AdviserId") ? null : reader.GetString("AdviserId"),
                        CreatedAt = reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.GetDateTime("UpdatedAt"),
                        IsActive = reader.GetBoolean("IsActive")
                    });
                }

                return Ok(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching students with name {Name}: {ErrorMessage}", name, ex.Message);
                return StatusCode(500, "Error searching students");
            }
        }

        [HttpPut("{studentId}/adviser")]
        public async Task<IActionResult> SetStudentAdviser(string studentId, [FromBody] SetAdviserRequest request)
        {
            if (request == null || string.IsNullOrEmpty(studentId))
                return BadRequest();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                var sql = "UPDATE student SET AdviserId = @AdviserId, UpdatedAt = NOW() WHERE StudentId = @StudentId AND IsActive = 1";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@AdviserId", string.IsNullOrEmpty(request.AdviserId) ? (object)DBNull.Value : request.AdviserId);
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                    return NotFound("Student not found");
                return Ok(new { success = true, message = "Adviser updated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting adviser for student {StudentId}", studentId);
                return StatusCode(500, "Error updating adviser");
            }
        }
    }

    public class SetAdviserRequest
    {
        public string? AdviserId { get; set; }
    }
}

