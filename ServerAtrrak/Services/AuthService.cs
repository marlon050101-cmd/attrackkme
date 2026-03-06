using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Services
{
    public class AuthService : IAuthService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<AuthService> _logger;

        public AuthService(Dbconnection dbConnection, ILogger<AuthService> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                _logger.LogInformation("Login attempt for user: {Username}", request.Username);
                
                var user = await GetUserByUsernameAsync(request.Username);
                _logger.LogInformation("User found: {UserFound}", user != null);
                
                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", request.Username);
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Invalid username or password"
                    };
                }

                _logger.LogInformation("User active status: {IsActive}", user.IsActive);
                if (!user.IsActive)
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Account is deactivated"
                    };
                }

                if (!user.IsApproved)
                {
                    if (user.UserType == UserType.SubjectTeacher)
                    {
                        return new LoginResponse
                        {
                            Success = false,
                            Message = "Please consult to your head for approval"
                        };
                    }
                    if (user.UserType == UserType.Student)
                    {
                        return new LoginResponse
                        {
                            Success = false,
                            Message = "Please consult to your adviser for approval"
                        };
                    }
                }

                _logger.LogInformation("Validating password for user: {Username}", request.Username);
                var passwordValid = await ValidatePasswordAsync(request.Password, user.Password);
                _logger.LogInformation("Password valid: {PasswordValid}", passwordValid);
                
                if (!passwordValid)
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Invalid username or password"
                    };
                }

                // Update last login
                _logger.LogInformation("Updating last login for user: {UserId}", user.UserId);
                await UpdateLastLoginAsync(user.UserId);

                _logger.LogInformation("Login successful for user: {Username}", request.Username);
                return new LoginResponse
                {
                    Success = true,
                    Message = "Login successful",
                    User = new UserInfo
                    {
                        UserId = user.UserId,
                        Username = user.Username,
                        Email = user.Email,
                        UserType = user.UserType,
                        TeacherId = user.TeacherId,
                        StudentId = user.StudentId,
                        SchoolId = user.SchoolId,
                        LastLoginAt = user.LastLoginAt
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}. Error: {ErrorMessage}", request.Username, ex.Message);
                return new LoginResponse
                {
                    Success = false,
                    Message = $"An error occurred during login: {ex.Message}"
                };
            }
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            try
            {
                _logger.LogInformation("Getting user by username: {Username}", username);
                _logger.LogInformation("Connection string: {ConnectionString}", _dbConnection.GetConnection().Substring(0, 20) + "...");
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                _logger.LogInformation("Database connection opened successfully");

                var query = @"
                    SELECT u.UserId, u.Username, u.Email, u.Password, u.UserType, 
                           u.IsActive, u.IsApproved, u.CreatedAt, u.UpdatedAt, u.LastLoginAt, 
                           u.TeacherId, u.StudentId, COALESCE(t.SchoolId, s.SchoolId) as SchoolId
                    FROM user u
                    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
                    LEFT JOIN student s ON u.StudentId = s.StudentId
                    WHERE u.Username = @Username";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Username", username);
                _logger.LogInformation("Executing query: {Query}", query);

                using var reader = await command.ExecuteReaderAsync();
                _logger.LogInformation("Query executed, reading results");
                
                if (await reader.ReadAsync())
                {
                    _logger.LogInformation("User found in database");
                    var userTypeString = reader.GetString(4);
                    var userType = userTypeString switch
                    {
                        "Admin" => UserType.Admin,
                        "SubjectTeacher" => UserType.SubjectTeacher,
                        "Teacher" => UserType.SubjectTeacher, // legacy fallback
                        "Student" => UserType.Student,
                        "GuidanceCounselor" => UserType.GuidanceCounselor,
                        "Adviser" => UserType.Adviser,
                        "Advisor" => UserType.Adviser, // legacy fallback
                        "Head" => UserType.Head,
                        _ => UserType.Admin
                    };

                    var userId = reader.GetString(0);
                    var schoolId = reader.IsDBNull(12) ? null : reader.GetString(12);
                    _logger.LogInformation("User {UserId} has SchoolId: {SchoolId}", userId, schoolId ?? "NULL");

                    var user = new User
                    {
                        UserId = userId,
                        Username = reader.GetString(1),
                        Email = reader.GetString(2),
                        Password = reader.GetString(3),
                        UserType = userType,
                        IsActive = reader.GetBoolean(5),
                        IsApproved = reader.GetBoolean(6),
                        CreatedAt = reader.GetDateTime(7),
                        UpdatedAt = reader.GetDateTime(8),
                        LastLoginAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        TeacherId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        StudentId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SchoolId = schoolId
                    };
                    _logger.LogInformation("User object created successfully");
                    return user;
                }

                _logger.LogWarning("No user found with username: {Username}", username);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by username: {Username}. Error: {ErrorMessage}", username, ex.Message);
                throw;
            }
        }

        public Task<bool> ValidatePasswordAsync(string password, string storedPassword)
        {
            return Task.FromResult(password == storedPassword);
        }

        public async Task UpdateLastLoginAsync(string userId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "UPDATE user SET LastLoginAt = @LastLoginAt WHERE UserId = @UserId";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@LastLoginAt", DateTime.Now);
            command.Parameters.AddWithValue("@UserId", userId);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<(bool success, string message)> UpdateUserProfileAsync(UpdateProfileRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Check if username is taken by another user
                var checkQuery = "SELECT COUNT(*) FROM user WHERE Username = @Username AND UserId != @UserId";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@Username", request.Username);
                checkCommand.Parameters.AddWithValue("@UserId", request.UserId);
                
                var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                if (count > 0)
                {
                    return (false, "Username is already taken.");
                }

                var updateQuery = @"
                    UPDATE user 
                    SET Username = @Username, 
                        Password = @Password, 
                        IsActive = @IsActive, 
                        UpdatedAt = @UpdatedAt 
                    WHERE UserId = @UserId";

                using var updateCommand = new MySqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@Username", request.Username);
                updateCommand.Parameters.AddWithValue("@Password", request.Password);
                updateCommand.Parameters.AddWithValue("@IsActive", request.IsActive);
                updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                updateCommand.Parameters.AddWithValue("@UserId", request.UserId);

                var rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Profile updated successfully for {UserId}", request.UserId);
                    return (true, "Profile updated successfully.");
                }
                
                return (false, "User not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user profile: {UserId}", request.UserId);
                return (false, $"An error occurred updating profile: {ex.Message}");
            }
        }

    }
}
