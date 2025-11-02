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
                    SELECT UserId, Username, Email, Password, UserType, 
                           IsActive, CreatedAt, UpdatedAt, LastLoginAt, TeacherId, StudentId
                    FROM user 
                    WHERE Username = @Username";

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
                        "Teacher" => UserType.Teacher,
                        "Student" => UserType.Student,
                        "GuidanceCounselor" => UserType.GuidanceCounselor,
                        _ => UserType.Admin // Default fallback
                    };

                    var user = new User
                    {
                        UserId = reader.GetString(0),
                        Username = reader.GetString(1),
                        Email = reader.GetString(2),
                        Password = reader.GetString(3),
                        UserType = userType,
                        IsActive = reader.GetBoolean(5),
                        CreatedAt = reader.GetDateTime(6),
                        UpdatedAt = reader.GetDateTime(7),
                        LastLoginAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        TeacherId = reader.IsDBNull(9) ? null : reader.GetString(9),
                        StudentId = reader.IsDBNull(10) ? null : reader.GetString(10)
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

    }
}
