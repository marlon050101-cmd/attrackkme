using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Services
{
    public class SchoolService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<SchoolService> _logger;

        public SchoolService(Dbconnection dbConnection, ILogger<SchoolService> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<List<string>> GetRegionsAsync()
        {
            try
            {
                _logger.LogInformation("Getting regions from School table");
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                _logger.LogInformation("Database connection opened successfully");

                var query = "SELECT DISTINCT Region FROM school ORDER BY Region";
                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                var regions = new List<string>();
                while (await reader.ReadAsync())
                {
                    var region = reader.GetString(0);
                    if (!string.IsNullOrEmpty(region))
                    {
                        regions.Add(region);
                        _logger.LogInformation("Found region: {Region}", region);
                    }
                }

                _logger.LogInformation("Total regions found in database: {Count}", regions.Count);

                if (regions.Count == 0)
                {
                    _logger.LogInformation("No regions found in database, using default regions");
                    regions.AddRange(new List<string>
                    {
                        "National Capital Region (NCR)",
                        "Region I - Ilocos Region",
                        "Region II - Cagayan Valley",
                        "Region III - Central Luzon",
                        "Region IV-A - CALABARZON",
                        "Region IV-B - MIMAROPA",
                        "Region V - Bicol Region",
                        "Region VI - Western Visayas",
                        "Region VII - Central Visayas",
                        "Region VIII - Eastern Visayas",
                        "Region IX - Zamboanga Peninsula",
                        "Region X - Northern Mindanao",
                        "Region XI - Davao Region",
                        "Region XII - SOCCSKSARGEN",
                        "Region XIII - Caraga",
                        "Cordillera Administrative Region (CAR)",
                        "Bangsamoro Autonomous Region in Muslim Mindanao (BARMM)"
                    });
                }
                return regions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting regions: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        public async Task<List<string>> GetDivisionsByRegionAsync(string region)
        {
            try
            {
                _logger.LogInformation("Getting divisions for region: {Region}", region);
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                _logger.LogInformation("Database connection opened successfully");

                var query = "SELECT DISTINCT Division FROM school WHERE Region = @Region ORDER BY Division";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Region", region);
                _logger.LogInformation("Executing query: {Query} with parameter: {Region}", query, region);
                
                using var reader = await command.ExecuteReaderAsync();

                var divisions = new List<string>();
                while (await reader.ReadAsync())
                {
                    var division = reader.GetString(0);
                    _logger.LogInformation("Found division: {Division}", division);
                    if (!string.IsNullOrEmpty(division))
                    {
                        divisions.Add(division);
                    }
                }

                _logger.LogInformation("Total divisions found: {Count}", divisions.Count);

                if (divisions.Count == 0)
                {
                    _logger.LogInformation("No divisions found, adding 'Other'");
                    divisions.Add("Other");
                }
                return divisions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting divisions for region: {Region}", region);
                throw;
            }
        }

        public async Task<List<string>> GetDistrictsByDivisionAsync(string division)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT DISTINCT District FROM school WHERE Division = @Division AND District IS NOT NULL ORDER BY District";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Division", division);
                using var reader = await command.ExecuteReaderAsync();

                var districts = new List<string>();
                while (await reader.ReadAsync())
                {
                    var district = reader.GetString(0);
                    if (!string.IsNullOrEmpty(district))
                    {
                        districts.Add(district);
                    }
                }

                if (districts.Count == 0)
                {
                    districts.Add("Other");
                }
                return districts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting districts for division: {Division}", division);
                throw;
            }
        }

        public async Task<string> GetOrCreateSchoolAsync(RegisterRequest request)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var checkQuery = "SELECT SchoolId FROM school WHERE SchoolName = @SchoolName AND Division = @Division AND (District = @District OR (@District IS NULL AND District IS NULL))";
            using var checkCommand = new MySqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@SchoolName", request.SchoolName);
            checkCommand.Parameters.AddWithValue("@Division", request.Division);
            checkCommand.Parameters.AddWithValue("@District", request.District ?? (object)DBNull.Value);

            var existingSchoolId = await checkCommand.ExecuteScalarAsync();
            if (existingSchoolId != null)
            {
                return existingSchoolId.ToString()!;
            }

            var schoolId = Guid.NewGuid().ToString();
            var insertQuery = @"
                INSERT INTO school (SchoolId, SchoolName, Region, Division, District, SchoolAddress)
                VALUES (@SchoolId, @SchoolName, @Region, @Division, @District, @SchoolAddress)";

            using var insertCommand = new MySqlCommand(insertQuery, connection);
            insertCommand.Parameters.AddWithValue("@SchoolId", schoolId);
            insertCommand.Parameters.AddWithValue("@SchoolName", request.SchoolName);
            insertCommand.Parameters.AddWithValue("@Region", request.Region);
            insertCommand.Parameters.AddWithValue("@Division", request.Division);
            insertCommand.Parameters.AddWithValue("@District", request.District ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@SchoolAddress", request.SchoolAddress ?? "");

            await insertCommand.ExecuteNonQueryAsync();
            return schoolId;
        }

        public async Task<bool> SchoolExistsAsync(string schoolName, string division, string? district)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT COUNT(*) FROM school WHERE SchoolName = @SchoolName AND Division = @Division AND (District = @District OR (@District IS NULL AND District IS NULL))";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@SchoolName", schoolName);
            command.Parameters.AddWithValue("@Division", division);
            command.Parameters.AddWithValue("@District", district ?? (object)DBNull.Value);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }

        public async Task<List<SchoolInfo>> GetAllSchoolsAsync()
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT SchoolId, SchoolName, Region, Division, District, SchoolAddress FROM school ORDER BY SchoolName";
                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                var schools = new List<SchoolInfo>();
                while (await reader.ReadAsync())
                {
                    schools.Add(new SchoolInfo
                    {
                        SchoolId = reader.GetString(0),
                        SchoolName = reader.GetString(1),
                        Region = reader.GetString(2),
                        Division = reader.GetString(3),
                        District = reader.IsDBNull(4) ? null : reader.GetString(4),
                        SchoolAddress = reader.IsDBNull(5) ? null : reader.GetString(5)
                    });
                }

                return schools;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all schools: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        public async Task<List<SchoolInfo>> SearchSchoolsAsync(string name)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT SchoolId, SchoolName, Region, Division, District, SchoolAddress 
                    FROM school 
                    WHERE SchoolName LIKE @SearchTerm 
                    ORDER BY 
                        CASE 
                            WHEN SchoolName LIKE @ExactMatch THEN 1
                            WHEN SchoolName LIKE @StartsWith THEN 2
                            ELSE 3
                        END,
                        SchoolName
                    LIMIT 10";

                using var command = new MySqlCommand(query, connection);
                var searchTerm = $"%{name}%";
                var exactMatch = $"{name}%";
                var startsWith = $"{name}%";

                command.Parameters.AddWithValue("@SearchTerm", searchTerm);
                command.Parameters.AddWithValue("@ExactMatch", exactMatch);
                command.Parameters.AddWithValue("@StartsWith", startsWith);

                using var reader = await command.ExecuteReaderAsync();

                var schools = new List<SchoolInfo>();
                while (await reader.ReadAsync())
                {
                    schools.Add(new SchoolInfo
                    {
                        SchoolId = reader.GetString(0),
                        SchoolName = reader.GetString(1),
                        Region = reader.GetString(2),
                        Division = reader.GetString(3),
                        District = reader.IsDBNull(4) ? null : reader.GetString(4),
                        SchoolAddress = reader.IsDBNull(5) ? null : reader.GetString(5)
                    });
                }

                return schools;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching schools: {ErrorMessage}", ex.Message);
                throw;
            }
        }
        public async Task<List<string>> GetSectionsBySchoolAndGradeAsync(string? schoolId, string? schoolName, int gradeLevel)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                if (string.IsNullOrEmpty(schoolId) && !string.IsNullOrEmpty(schoolName))
                {
                    // Get SchoolId first
                    var schoolQuery = "SELECT SchoolId FROM school WHERE SchoolName = @SchoolName LIMIT 1";
                    using var schoolCommand = new MySqlCommand(schoolQuery, connection);
                    schoolCommand.Parameters.AddWithValue("@SchoolName", schoolName);
                    var schoolIdObj = await schoolCommand.ExecuteScalarAsync();
                    
                    if (schoolIdObj != null)
                    {
                        schoolId = schoolIdObj.ToString();
                    }
                }
                
                if (string.IsNullOrEmpty(schoolId)) return new List<string>();

                // Query teacher and student tables for distinct sections
                var query = @"
                    SELECT DISTINCT Section FROM (
                        SELECT Section FROM teacher WHERE SchoolId = @SchoolId AND Gradelvl = @GradeLevel AND Section IS NOT NULL AND Section != ''
                        UNION
                        SELECT Section FROM student WHERE SchoolId = @SchoolId AND GradeLevel = @GradeLevel AND Section IS NOT NULL AND Section != ''
                    ) AS combined_sections
                    ORDER BY Section";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                command.Parameters.AddWithValue("@GradeLevel", gradeLevel);

                using var reader = await command.ExecuteReaderAsync();

                var sections = new List<string>();
                while (await reader.ReadAsync())
                {
                    try
                    {
                        var section = reader.GetString(0);
                        if (!string.IsNullOrEmpty(section))
                        {
                            sections.Add(section.Trim().ToUpper());
                        }
                    }
                    catch { /* Ignore nulls or invalid casting just in case */ }
                }

                return sections.Distinct().OrderBy(s => s).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sections for school {SchoolName} and grade {GradeLevel}", schoolName, gradeLevel);
                throw;
            }
        }
    }
}
