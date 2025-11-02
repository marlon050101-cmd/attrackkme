using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using MySql.Data.MySqlClient;
using System.Data;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<StudentController> _logger;

        public StudentController(IConfiguration configuration, ILogger<StudentController> logger)
        {
            _configuration = configuration;
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
                    SELECT StudentId, FullName, Email, GradeLevel, Section, Strand, SchoolId, ParentsNumber, QRImage, CreatedAt, UpdatedAt, IsActive
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
                    SELECT StudentId, FullName, Email, GradeLevel, Section, Strand, SchoolId, ParentsNumber, QRImage, CreatedAt, UpdatedAt, IsActive
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
    }
}

