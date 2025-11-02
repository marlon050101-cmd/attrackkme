using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using ServerAtrrak.Services;
using MySql.Data.MySqlClient;
using ServerAtrrak.Data;
using QRCoder;
using System.Drawing.Imaging;
using System.Data;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegisterController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly SchoolService _schoolService;
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<RegisterController> _logger;

        public RegisterController(IAuthService authService, SchoolService schoolService, Dbconnection dbConnection, ILogger<RegisterController> logger)
        {
            _authService = authService;
            _schoolService = schoolService;
            _dbConnection = dbConnection;
            _logger = logger;
        }

        [HttpPost("student")]
        public async Task<ActionResult<StudentRegisterResponse>> RegisterStudent([FromBody] StudentRegisterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new StudentRegisterResponse
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                // Check if username already exists
                var existingUser = await _authService.GetUserByUsernameAsync(request.Username);
                if (existingUser != null)
                {
                    return BadRequest(new StudentRegisterResponse
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Check if email already exists
                if (await EmailExistsAsync(request.Email))
                {
                    return BadRequest(new StudentRegisterResponse
                    {
                        Success = false,
                        Message = "Email already exists"
                    });
                }

                // Find school by name
                var schoolId = await FindSchoolByNameAsync(request.SchoolName);
                if (string.IsNullOrEmpty(schoolId))
                {
                    return BadRequest(new StudentRegisterResponse
                    {
                        Success = false,
                        Message = "School not found. Please select a valid school from the list."
                    });
                }

                // Create student record
                var studentId = await CreateStudentAsync(request, schoolId);

                // Create user record
                var userId = await CreateUserForStudentAsync(request, studentId);

                // Generate QR code
                var qrCodeData = await GenerateQRCodeAsync(studentId);

                _logger.LogInformation("Student registered successfully: {Username}", request.Username);

                return Ok(new StudentRegisterResponse
                {
                    Success = true,
                    Message = "Student registered successfully",
                    UserId = userId,
                    StudentId = studentId,
                    QRCodeData = qrCodeData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during student registration for user: {Username}. Error: {ErrorMessage}", request.Username, ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                return StatusCode(500, new StudentRegisterResponse
                {
                    Success = false,
                    Message = $"An error occurred during registration: {ex.Message}"
                });
            }
        }

        [HttpPost("teacher")]
        public async Task<ActionResult<RegisterResponse>> RegisterTeacher([FromBody] RegisterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new RegisterResponse
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                // Check if username already exists
                var existingUser = await _authService.GetUserByUsernameAsync(request.Username);
                if (existingUser != null)
                {
                    return BadRequest(new RegisterResponse
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Check if email already exists
                if (await EmailExistsAsync(request.Email))
                {
                    return BadRequest(new RegisterResponse
                    {
                        Success = false,
                        Message = "Email already exists"
                    });
                }

                // Check if school exists, if not create it
                var schoolId = await _schoolService.GetOrCreateSchoolAsync(request);

                // Create teacher record
                var teacherId = await CreateTeacherAsync(request, schoolId);

                // Create user record
                var userId = await CreateUserAsync(request, teacherId);

                _logger.LogInformation("Teacher registered successfully: {Username}", request.Username);

                return Ok(new RegisterResponse
                {
                    Success = true,
                    Message = "Teacher registered successfully",
                    UserId = userId,
                    TeacherId = teacherId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during teacher registration for user: {Username}", request.Username);
                return StatusCode(500, new RegisterResponse
                {
                    Success = false,
                    Message = "An error occurred during registration"
                });
            }
        }

        [HttpPost("guidance")]
        public async Task<ActionResult<RegisterResponse>> RegisterGuidanceCounselor([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Guidance registration attempt for user: {Username}, UserType: {UserType}", request.Username, request.UserType);
                
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model validation failed for guidance registration: {Username}", request.Username);
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return BadRequest(new RegisterResponse
                    {
                        Success = false,
                        Message = $"Invalid request data: {string.Join(", ", errors)}"
                    });
                }

                // Check if username already exists
                var existingUser = await _authService.GetUserByUsernameAsync(request.Username);
                if (existingUser != null)
                {
                    return BadRequest(new RegisterResponse
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Check if email already exists
                if (await EmailExistsAsync(request.Email))
                {
                    return BadRequest(new RegisterResponse
                    {
                        Success = false,
                        Message = "Email already exists"
                    });
                }

                // Check if school exists, if not create it
                var schoolId = await _schoolService.GetOrCreateSchoolAsync(request);

                // Set default values for guidance counselors if needed
                if (request.UserType == UserType.GuidanceCounselor)
                {
                    request.GradeLevel = null; // Guidance counselors don't have a specific grade level
                    request.Section = null; // No section needed
                    request.Strand = null; // No strand needed
                }

                // Create guidance counselor record (similar to teacher but with different table)
                var guidanceId = await CreateGuidanceCounselorAsync(request, schoolId);

                // Create user record
                var userId = await CreateUserForGuidanceAsync(request, guidanceId);

                _logger.LogInformation("Guidance Counselor registered successfully: {Username}", request.Username);

                return Ok(new RegisterResponse
                {
                    Success = true,
                    Message = "Guidance Counselor registered successfully",
                    UserId = userId,
                    TeacherId = guidanceId // Reusing TeacherId field for guidance counselor ID
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during guidance counselor registration for user: {Username}", request.Username);
                return StatusCode(500, new RegisterResponse
                {
                    Success = false,
                    Message = "An error occurred during registration"
                });
            }
        }

        [HttpGet("regions")]
        public async Task<ActionResult<List<string>>> GetRegions()
        {
            try
            {
                var regions = await _schoolService.GetRegionsAsync();
                return Ok(regions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting regions: {ErrorMessage}", ex.Message);
                return StatusCode(500, new List<string>());
            }
        }

        [HttpGet("divisions/{region}")]
        public async Task<ActionResult<List<string>>> GetDivisions(string region)
        {
            try
            {
                _logger.LogInformation("API: Getting divisions for region: {Region}", region);
                var divisions = await _schoolService.GetDivisionsByRegionAsync(region);
                _logger.LogInformation("API: Returning {Count} divisions: {Divisions}", divisions.Count, string.Join(", ", divisions));
                return Ok(divisions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting divisions for region: {Region}", region);
                return StatusCode(500, new List<string>());
            }
        }

        [HttpGet("districts/{division}")]
        public async Task<ActionResult<List<string>>> GetDistricts(string division)
        {
            try
            {
                var districts = await _schoolService.GetDistrictsByDivisionAsync(division);
                return Ok(districts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting districts for division: {Division}", division);
                return StatusCode(500, new List<string>());
            }
        }

        [HttpGet("schools/all")]
        public async Task<ActionResult<List<SchoolInfo>>> GetAllSchools()
        {
            try
            {
                var schools = await _schoolService.GetAllSchoolsAsync();
                return Ok(schools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all schools: {ErrorMessage}", ex.Message);
                return StatusCode(500, new List<SchoolInfo>());
            }
        }

        [HttpGet("schools/search")]
        public async Task<ActionResult<List<SchoolInfo>>> SearchSchools([FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || name.Length < 2)
                {
                    return Ok(new List<SchoolInfo>());
                }

                var schools = await _schoolService.SearchSchoolsAsync(name);
                return Ok(schools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching schools: {ErrorMessage}", ex.Message);
                return StatusCode(500, new List<SchoolInfo>());
            }
        }

        [HttpGet("student/{studentId}/qr")]
        public async Task<ActionResult<string>> GetStudentQRCode(string studentId)
        {
            try
            {
                _logger.LogInformation("Getting QR code for student: {StudentId}", studentId);
                var qrCodeData = await GetStudentQRImageAsync(studentId);
                
                if (string.IsNullOrEmpty(qrCodeData))
                {
                    // If QR code doesn't exist, generate it
                    _logger.LogInformation("QR code not found for student {StudentId}, generating new one", studentId);
                    qrCodeData = await GenerateQRCodeAsync(studentId);
                    if (string.IsNullOrEmpty(qrCodeData))
                    {
                        _logger.LogError("Failed to generate QR code for student: {StudentId}", studentId);
                        return NotFound("Failed to generate QR code for this student");
                    }
                    _logger.LogInformation("Successfully generated QR code for student: {StudentId}, Length: {Length}", studentId, qrCodeData.Length);
                }
                else
                {
                    _logger.LogInformation("Retrieved existing QR code for student: {StudentId}, Length: {Length}", studentId, qrCodeData.Length);
                }
                
                return Ok(qrCodeData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting QR code for student: {StudentId}", studentId);
                return StatusCode(500, "Error retrieving QR code");
            }
        }

        [HttpGet("student/{studentId}")]
        public async Task<ActionResult<StudentInfo>> GetStudentInfo(string studentId)
        {
            try
            {
                var studentInfo = await GetStudentByIdAsync(studentId);
                if (studentInfo == null)
                {
                    return NotFound("Student not found");
                }
                return Ok(studentInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student info: {StudentId}", studentId);
                return StatusCode(500, "Error retrieving student information");
            }
        }

        [HttpGet("school/{schoolId}")]
        public async Task<ActionResult<SchoolInfo>> GetSchoolInfo(string schoolId)
        {
            try
            {
                var schoolInfo = await GetSchoolByIdAsync(schoolId);
                if (schoolInfo == null)
                {
                    return NotFound("School not found");
                }
                return Ok(schoolInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting school info: {SchoolId}", schoolId);
                return StatusCode(500, "Error retrieving school information");
            }
        }

        [HttpGet("students")]
        public async Task<ActionResult<List<Student>>> GetStudents([FromQuery] string? teacherId)
        {
            try
            {
                Console.WriteLine($"DEBUG: GetStudents API called with teacherId: {teacherId}");
                var students = await GetStudentsAsync(teacherId);
                Console.WriteLine($"DEBUG: GetStudents API returning {students.Count} students");
                return Ok(students);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Error in GetStudents API: {ex.Message}");
                _logger.LogError(ex, "Error getting students for teacher {TeacherId}: {ErrorMessage}", teacherId, ex.Message);
                return StatusCode(500, new List<Student>());
            }
        }

        [HttpGet("test")]
        public ActionResult<string> Test()
        {
            Console.WriteLine("DEBUG: Test API endpoint called");
            return Ok("Test API is working!");
        }

        [HttpGet("test-teacher/{teacherId}")]
        public async Task<ActionResult<object>> TestTeacher(string teacherId)
        {
            try
            {
                Console.WriteLine($"DEBUG: TestTeacher endpoint called with teacherId: {teacherId}");
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                // Test teacher lookup
                var teacherQuery = "SELECT TeacherId, SchoolId, Section FROM teacher WHERE TeacherId = @TeacherId";
                using var command = new MySqlCommand(teacherQuery, connection);
                command.Parameters.AddWithValue("@TeacherId", teacherId);
                
                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var teacherInfo = new
                    {
                        teacherId = reader.GetString("TeacherId"),
                        schoolId = reader.GetString("SchoolId"),
                        section = reader.GetString("Section")
                    };
                    
                    Console.WriteLine($"DEBUG: Found teacher: {teacherInfo}");
                    return Ok(teacherInfo);
                }
                else
                {
                    Console.WriteLine($"DEBUG: No teacher found for TeacherId: {teacherId}");
                    return Ok(new { error = "Teacher not found" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: TestTeacher error: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("debug-queries/{teacherId}")]
        public ActionResult<object> GetDebugQueries(string teacherId)
        {
            try
            {
                var queries = new
                {
                    TeacherQuery = @"
                        SELECT u.TeacherId, t.SchoolId, t.Gradelvl as GradeLevel, t.Section 
                        FROM user u
                        LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                        WHERE u.UserId = @UserId AND u.UserType = 'Teacher'",
                    
                    SchoolCheckQuery = "SELECT StudentId, FullName, GradeLevel, Section, SchoolId FROM student WHERE SchoolId = @SchoolId AND IsActive = 1",
                    
                    SectionCheckQuery = @"
                        SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.SchoolId
                        FROM student s
                        WHERE s.SchoolId = @SchoolId AND s.Section = @Section AND s.IsActive = 1",
                    
                    MainQuery = @"
                        SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.SchoolId, s.ParentsNumber, s.Gender, s.QRImage, s.CreatedAt, s.UpdatedAt, s.IsActive
                        FROM student s
                        WHERE s.SchoolId = @SchoolId AND s.Section = @Section AND s.IsActive = 1
                        ORDER BY s.FullName",
                    
                    FallbackQuery = @"
                        SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.SchoolId, s.ParentsNumber, s.Gender, s.QRImage, s.CreatedAt, s.UpdatedAt, s.IsActive
                        FROM student s
                        WHERE s.SchoolId = @SchoolId AND s.IsActive = 1
                        ORDER BY s.FullName",
                    
                    Parameters = new
                    {
                        TeacherId = teacherId,
                        UserId = teacherId,
                        SchoolId = "1d543c73-eebc-4c2c-99ff-4beb2dbfc12f", // From your database
                        Section = "MEOW"
                    }
                };
                
                return Ok(queries);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("test-database")]
        public async Task<ActionResult<object>> TestDatabase()
        {
            try
            {
                Console.WriteLine("DEBUG: TestDatabase endpoint called");
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                Console.WriteLine("DEBUG: Database connection opened successfully");
                
                // Test 1: Count all students
                var countQuery = "SELECT COUNT(*) as StudentCount FROM student WHERE IsActive = 1";
                using var countCommand = new MySqlCommand(countQuery, connection);
                var totalStudents = await countCommand.ExecuteScalarAsync();
                Console.WriteLine($"DEBUG: Total active students: {totalStudents}");
                
                // Test 2: Get students with specific SchoolId and Section
                var testQuery = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.SchoolId
                    FROM student s
                    WHERE s.SchoolId = @SchoolId AND s.Section = @Section AND s.IsActive = 1";
                
                using var testCommand = new MySqlCommand(testQuery, connection);
                testCommand.Parameters.AddWithValue("@SchoolId", "1d543c73-eebc-4c2c-99ff-4beb2dbfc12f");
                testCommand.Parameters.AddWithValue("@Section", "MEOW");
                
                var testResults = new List<object>();
                using var testReader = await testCommand.ExecuteReaderAsync();
                while (await testReader.ReadAsync())
                {
                    testResults.Add(new
                    {
                        StudentId = testReader.GetString("StudentId"),
                        FullName = testReader.GetString("FullName"),
                        GradeLevel = testReader.GetInt32("GradeLevel"),
                        Section = testReader.GetString("Section"),
                        SchoolId = testReader.GetString("SchoolId")
                    });
                }
                
                Console.WriteLine($"DEBUG: Test query returned {testResults.Count} students");
                
                return Ok(new
                {
                    TotalActiveStudents = totalStudents,
                    TestQueryResults = testResults,
                    TestQuery = testQuery,
                    Parameters = new { SchoolId = "1d543c73-eebc-4c2c-99ff-4beb2dbfc12f", Section = "MEOW" }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: TestDatabase error: {ex.Message}");
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("teacher-by-user/{userId}")]
        public async Task<ActionResult<TeacherInfo>> GetTeacherByUserId(string userId)
        {
            try
            {
                var teacherInfo = await GetTeacherByUserIdAsync(userId);
                if (teacherInfo == null)
                {
                    return NotFound("Teacher not found");
                }
                return Ok(teacherInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher by user ID {UserId}: {ErrorMessage}", userId, ex.Message);
                return StatusCode(500, "Error retrieving teacher information");
            }
        }

        [HttpGet("student-info/{studentId}")]
        public async Task<ActionResult<StudentDisplayInfo>> GetStudentInfoForScanner(string studentId)
        {
            try
            {
                var studentInfo = await GetStudentDisplayInfoAsync(studentId);
                if (studentInfo == null)
                {
                    return NotFound("Student not found");
                }
                return Ok(studentInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student info for scanner: {StudentId}: {ErrorMessage}", studentId, ex.Message);
                return StatusCode(500, "Error retrieving student information");
            }
        }

        private async Task<bool> EmailExistsAsync(string email)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT COUNT(*) FROM user WHERE Email = @Email";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }


        private async Task<string> CreateTeacherAsync(RegisterRequest request, string schoolId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var teacherId = Guid.NewGuid().ToString();
            var query = @"
                INSERT INTO teacher (TeacherId, FullName, Email, SchoolId, Gradelvl, Section, Strand)
                VALUES (@TeacherId, @FullName, @Email, @SchoolId, @Gradelvl, @Section, @Strand)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@TeacherId", teacherId);
            command.Parameters.AddWithValue("@FullName", request.FullName);
            command.Parameters.AddWithValue("@Email", request.Email);
            command.Parameters.AddWithValue("@SchoolId", schoolId);
            command.Parameters.AddWithValue("@Gradelvl", request.GradeLevel);
            command.Parameters.AddWithValue("@Section", request.Section);
            command.Parameters.AddWithValue("@Strand", request.Strand ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
            return teacherId;
        }

        private async Task<string> CreateUserAsync(RegisterRequest request, string teacherId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var userId = Guid.NewGuid().ToString();
            var query = @"
                INSERT INTO user (UserId, Username, Email, Password, UserType, IsActive, CreatedAt, UpdatedAt, TeacherId)
                VALUES (@UserId, @Username, @Email, @Password, @UserType, @IsActive, @CreatedAt, @UpdatedAt, @TeacherId)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Username", request.Username);
            command.Parameters.AddWithValue("@Email", request.Email);
            command.Parameters.AddWithValue("@Password", request.Password);
            command.Parameters.AddWithValue("@UserType", "Teacher");
            command.Parameters.AddWithValue("@IsActive", true);
            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@TeacherId", teacherId);

            await command.ExecuteNonQueryAsync();
            return userId;
        }

        private async Task<string?> FindSchoolByNameAsync(string schoolName)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT SchoolId FROM school WHERE SchoolName = @SchoolName LIMIT 1";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@SchoolName", schoolName);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }

        private async Task<string> CreateStudentAsync(StudentRegisterRequest request, string schoolId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var studentId = Guid.NewGuid().ToString();
            var query = @"
                INSERT INTO student (StudentId, FullName, Email, GradeLevel, Section, Strand, SchoolId, ParentsNumber, Gender, QRImage)
                VALUES (@StudentId, @FullName, @Email, @GradeLevel, @Section, @Strand, @SchoolId, @ParentsNumber, @Gender, @QRImage)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@StudentId", studentId);
            command.Parameters.AddWithValue("@FullName", request.FullName);
            command.Parameters.AddWithValue("@Email", request.Email);
            command.Parameters.AddWithValue("@GradeLevel", request.GradeLevel);
            command.Parameters.AddWithValue("@Section", request.Section);
            command.Parameters.AddWithValue("@Strand", request.Strand ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@SchoolId", schoolId);
            command.Parameters.AddWithValue("@ParentsNumber", request.ParentsNumber);
            command.Parameters.AddWithValue("@Gender", request.Gender);
            command.Parameters.AddWithValue("@QRImage", ""); // Will be updated after QR generation

            await command.ExecuteNonQueryAsync();
            return studentId;
        }

        private async Task<string> CreateUserForStudentAsync(StudentRegisterRequest request, string studentId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var userId = Guid.NewGuid().ToString();
            var query = @"
                INSERT INTO user (UserId, Username, Email, Password, UserType, IsActive, CreatedAt, UpdatedAt, StudentId)
                VALUES (@UserId, @Username, @Email, @Password, @UserType, @IsActive, @CreatedAt, @UpdatedAt, @StudentId)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Username", request.Username);
            command.Parameters.AddWithValue("@Email", request.Email);
            command.Parameters.AddWithValue("@Password", request.Password);
            command.Parameters.AddWithValue("@UserType", UserType.Student.ToString());
            command.Parameters.AddWithValue("@IsActive", true);
            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@StudentId", studentId);

            await command.ExecuteNonQueryAsync();
            return userId;
        }

        private async Task<string> GenerateQRCodeAsync(string studentId)
        {
            try
            {
                // Generate QR code with student ID as content
                using var qrGenerator = new QRCodeGenerator();
                var qrCodeData = qrGenerator.CreateQrCode(studentId, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new Base64QRCode(qrCodeData);
                
                // Generate QR code as base64 string
                var qrCodeBase64 = qrCode.GetGraphic(20);

                // Update the QR image in the database
                await UpdateStudentQRImageAsync(studentId, qrCodeBase64);

                return qrCodeBase64;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code for student: {StudentId}", studentId);
                throw;
            }
        }

        private async Task UpdateStudentQRImageAsync(string studentId, string qrCodeBase64)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "UPDATE student SET QRImage = @QRImage WHERE StudentId = @StudentId";
            using var command = new MySqlCommand(query, connection);
            
            // Convert Base64 string to bytes for LONGBLOB storage (store as UTF8 bytes)
            var qrCodeBytes = System.Text.Encoding.UTF8.GetBytes(qrCodeBase64);
            _logger.LogInformation("Storing QR code for student {StudentId}: Base64 length {Base64Length}, Byte array length {ByteLength}", 
                studentId, qrCodeBase64.Length, qrCodeBytes.Length);
            
            command.Parameters.AddWithValue("@QRImage", qrCodeBytes);
            command.Parameters.AddWithValue("@StudentId", studentId);

            await command.ExecuteNonQueryAsync();
        }

        private async Task<string?> GetStudentQRImageAsync(string studentId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT QRImage FROM student WHERE StudentId = @StudentId";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@StudentId", studentId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                if (reader.IsDBNull("QRImage"))
                {
                    _logger.LogInformation("No QR code found in database for student: {StudentId}", studentId);
                    return null;
                }

                // Get the data as byte array (LONGBLOB)
                var byteArray = (byte[])reader["QRImage"];
                _logger.LogInformation("QR code byte array length for student {StudentId}: {Length}", studentId, byteArray.Length);
                
                // Convert bytes to string (since it's stored as Base64 string in LONGBLOB)
                var base64String = System.Text.Encoding.UTF8.GetString(byteArray);
                _logger.LogInformation("Converted to string, length: {Length}, starts with: {Start}", base64String.Length, base64String.Substring(0, Math.Min(20, base64String.Length)));
                
                return base64String;
            }

            _logger.LogInformation("No student found with ID: {StudentId}", studentId);
            return null;
        }

        private async Task<StudentInfo?> GetStudentByIdAsync(string studentId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT StudentId, FullName, GradeLevel, Section, Strand, SchoolId, ParentsNumber, Gender FROM student WHERE StudentId = @StudentId";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@StudentId", studentId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new StudentInfo
                {
                    StudentId = reader.GetString("StudentId"),
                    FullName = reader.GetString("FullName"),
                    GradeLevel = reader.GetInt32("GradeLevel"),
                    Section = reader.GetString("Section"),
                    Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                    SchoolId = reader.GetString("SchoolId"),
                    ParentsNumber = reader.GetString("ParentsNumber"),
                    Gender = reader.GetString("Gender")
                };
            }

            return null;
        }

        private async Task<SchoolInfo?> GetSchoolByIdAsync(string schoolId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT SchoolId, SchoolName, Region, Division, District, SchoolAddress FROM school WHERE SchoolId = @SchoolId";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@SchoolId", schoolId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new SchoolInfo
                {
                    SchoolId = reader.GetString("SchoolId"),
                    SchoolName = reader.GetString("SchoolName"),
                    Region = reader.GetString("Region"),
                    Division = reader.GetString("Division"),
                    District = reader.IsDBNull("District") ? null : reader.GetString("District"),
                    SchoolAddress = reader.IsDBNull("SchoolAddress") ? null : reader.GetString("SchoolAddress")
                };
            }

            return null;
        }

        private async Task<List<Student>> GetStudentsAsync(string? teacherId)
        {
            Console.WriteLine($"DEBUG: GetStudentsAsync called with teacherId: {teacherId}");
            var students = new List<Student>();
            
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                Console.WriteLine("DEBUG: Database connection opened successfully");

                string query;
                MySqlCommand command;

                string? schoolId = null;
                string? section = null;
            
            if (!string.IsNullOrEmpty(teacherId))
            {
                Console.WriteLine($"DEBUG: Getting students for teacherId: {teacherId}");
                
                // Get teacher info directly from teacher table using the teacherId
                var teacherQuery = @"
                    SELECT t.SchoolId, t.Section 
                    FROM teacher t
                    WHERE t.TeacherId = @TeacherId";
                
                using (var teacherCommand = new MySqlCommand(teacherQuery, connection))
                {
                    teacherCommand.Parameters.AddWithValue("@TeacherId", teacherId);
                    using var teacherReader = await teacherCommand.ExecuteReaderAsync();
                    if (await teacherReader.ReadAsync())
                    {
                        schoolId = teacherReader.IsDBNull("SchoolId") ? null : teacherReader.GetString("SchoolId");
                        section = teacherReader.IsDBNull("Section") ? null : teacherReader.GetString("Section");
                        
                        Console.WriteLine($"DEBUG: Found teacher - SchoolId: {schoolId}, Section: {section}");
                    }
                    else
                    {
                        Console.WriteLine($"DEBUG: No teacher found for TeacherId: {teacherId}");
                    }
                }
                
                if (string.IsNullOrEmpty(schoolId) || string.IsNullOrEmpty(section))
                {
                    Console.WriteLine($"DEBUG: Teacher info incomplete - SchoolId: {schoolId}, Section: {section}");
                    return students; // Return empty list if teacher not found
                }

                // Use the EXACT query that works in MySQL Workbench
                query = @"
                    SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.SchoolId, s.ParentsNumber, s.Gender, s.QRImage, s.CreatedAt, s.UpdatedAt, s.IsActive
                    FROM student s
                    WHERE s.SchoolId = @SchoolId AND s.Section = @Section AND s.IsActive = 1
                    ORDER BY s.FullName";

                Console.WriteLine($"DEBUG: Using EXACT query from MySQL Workbench");
                Console.WriteLine($"DEBUG: SchoolId: '{schoolId}', Section: '{section}'");

                command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                command.Parameters.AddWithValue("@Section", section);
            }
            else
            {
                // Get all students if no teacher ID provided
                query = "SELECT StudentId, FullName, GradeLevel, Section, Strand, SchoolId, ParentsNumber, Gender, QRImage, CreatedAt, UpdatedAt, IsActive FROM student WHERE IsActive = 1 ORDER BY FullName";
                command = new MySqlCommand(query, connection);
            }

            Console.WriteLine($"DEBUG: About to execute main query with SchoolId: '{schoolId}', Section: '{section}'");
            Console.WriteLine($"DEBUG: Query: {query}");
            
            using var reader = await command.ExecuteReaderAsync();
            int studentCount = 0;
            while (await reader.ReadAsync())
            {
                studentCount++;
                var student = new Student
                {
                    StudentId = reader.IsDBNull("StudentId") ? "" : reader.GetString("StudentId"),
                    FullName = reader.IsDBNull("FullName") ? "" : reader.GetString("FullName"),
                    GradeLevel = reader.IsDBNull("GradeLevel") ? 0 : reader.GetInt32("GradeLevel"),
                    Section = reader.IsDBNull("Section") ? "" : reader.GetString("Section"),
                    Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                    SchoolId = reader.IsDBNull("SchoolId") ? "" : reader.GetString("SchoolId"),
                    ParentsNumber = reader.IsDBNull("ParentsNumber") ? "" : reader.GetString("ParentsNumber"),
                    Gender = reader.IsDBNull("Gender") ? "" : reader.GetString("Gender"),
                    QRImage = reader.IsDBNull("QRImage") ? null : Convert.ToBase64String((byte[])reader["QRImage"]),
                    CreatedAt = reader.IsDBNull("CreatedAt") ? DateTime.Now : reader.GetDateTime("CreatedAt"),
                    UpdatedAt = reader.IsDBNull("UpdatedAt") ? DateTime.Now : reader.GetDateTime("UpdatedAt"),
                    IsActive = reader.IsDBNull("IsActive") ? true : reader.GetBoolean("IsActive")
                };
                
                Console.WriteLine($"DEBUG: Found student {studentCount}: {student.FullName} (Grade: {student.GradeLevel}, Section: {student.Section}, School: {student.SchoolId})");
                students.Add(student);
            }

            Console.WriteLine($"DEBUG: Main query returned {studentCount} students");
            
            return students;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Exception in GetStudentsAsync: {ex.Message}");
                Console.WriteLine($"DEBUG: Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task<TeacherInfo?> GetTeacherByIdAsync(string teacherId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = @"
                SELECT t.TeacherId, t.FullName, t.Email, s.SchoolName, s.SchoolId, t.Gradelvl, t.Section, t.Strand
                FROM teacher t
                INNER JOIN school s ON t.SchoolId = s.SchoolId
                WHERE t.TeacherId = @TeacherId";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@TeacherId", teacherId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TeacherInfo
                {
                    TeacherId = reader.GetString("TeacherId"),
                    FullName = reader.GetString("FullName"),
                    Email = reader.GetString("Email"),
                    SchoolName = reader.GetString("SchoolName"),
                    SchoolId = reader.GetString("SchoolId"),
                    GradeLevel = reader.GetInt32("Gradelvl"),
                    Section = reader.GetString("Section"),
                    Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand")
                };
            }

            return null;
        }

        private async Task<TeacherInfo?> GetTeacherByUserIdAsync(string userId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = @"
                SELECT t.TeacherId, t.FullName, t.Email, s.SchoolName, s.SchoolId, t.Gradelvl, t.Section, t.Strand
                FROM user u
                INNER JOIN teacher t ON u.TeacherId = t.TeacherId
                INNER JOIN school s ON t.SchoolId = s.SchoolId
                WHERE u.UserId = @UserId";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TeacherInfo
                {
                    TeacherId = reader.GetString("TeacherId"),
                    FullName = reader.GetString("FullName"),
                    Email = reader.GetString("Email"),
                    SchoolName = reader.GetString("SchoolName"),
                    SchoolId = reader.GetString("SchoolId"),
                    GradeLevel = reader.GetInt32("Gradelvl"),
                    Section = reader.GetString("Section"),
                    Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand")
                };
            }

            return null;
        }

        private async Task<StudentDisplayInfo?> GetStudentDisplayInfoAsync(string studentId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = @"
                SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.SchoolId, s.ParentsNumber, s.Gender, sch.SchoolName, s.QRImage
                FROM student s
                INNER JOIN school sch ON s.SchoolId = sch.SchoolId
                WHERE s.StudentId = @StudentId";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@StudentId", studentId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var qrCodeData = "";
                if (!reader.IsDBNull("QRImage"))
                {
                    var qrBytes = (byte[])reader["QRImage"];
                    qrCodeData = System.Text.Encoding.UTF8.GetString(qrBytes);
                }

                return new StudentDisplayInfo
                {
                    StudentId = reader.GetString("StudentId"),
                    FullName = reader.GetString("FullName"),
                    GradeLevel = reader.GetInt32("GradeLevel"),
                    Section = reader.GetString("Section"),
                    Strand = reader.IsDBNull("Strand") ? null : reader.GetString("Strand"),
                    ParentsNumber = reader.GetString("ParentsNumber"),
                    Gender = reader.GetString("Gender"),
                    SchoolName = reader.GetString("SchoolName"),
                    QRCodeData = qrCodeData,
                    IsValid = true,
                    Message = "Student information retrieved successfully"
                };
            }

            return null;
        }

        private async Task<string> CreateGuidanceCounselorAsync(RegisterRequest request, string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var guidanceId = Guid.NewGuid().ToString();
                var query = @"
                    INSERT INTO teacher (TeacherId, FullName, Email, SchoolId, Gradelvl, Section, Strand)
                    VALUES (@TeacherId, @FullName, @Email, @SchoolId, @Gradelvl, @Section, @Strand)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@TeacherId", guidanceId);
                command.Parameters.AddWithValue("@FullName", request.FullName);
                command.Parameters.AddWithValue("@Email", request.Email);
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                command.Parameters.AddWithValue("@Gradelvl", request.GradeLevel ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Section", request.Section ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Strand", request.Strand ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Guidance counselor record created successfully: {GuidanceId}", guidanceId);
                return guidanceId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating guidance counselor record for user: {Username}", request.Username);
                throw;
            }
        }

        private async Task<string> CreateUserForGuidanceAsync(RegisterRequest request, string guidanceId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var userId = Guid.NewGuid().ToString();
                var query = @"
                    INSERT INTO user (UserId, Username, Email, Password, UserType, IsActive, CreatedAt, UpdatedAt, TeacherId)
                    VALUES (@UserId, @Username, @Email, @Password, @UserType, @IsActive, @CreatedAt, @UpdatedAt, @TeacherId)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@Username", request.Username);
                command.Parameters.AddWithValue("@Email", request.Email);
                command.Parameters.AddWithValue("@Password", request.Password);
                command.Parameters.AddWithValue("@UserType", UserType.GuidanceCounselor.ToString());
                command.Parameters.AddWithValue("@IsActive", true);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@TeacherId", guidanceId);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("User record created successfully for guidance counselor: {UserId}", userId);
                return userId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user record for guidance counselor: {Username}", request.Username);
                throw;
            }
        }
    }

    public class StudentRegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int GradeLevel { get; set; }
        public string Section { get; set; } = string.Empty;
        public string? Strand { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string ParentsNumber { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }

    public class StudentRegisterResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? StudentId { get; set; }
        public string? UserId { get; set; }
        public string? QRCodeData { get; set; }
    }

}
