using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

class Program
{
    static string connString = "Server=shinkansen.proxy.rlwy.net;Port=42561;Database=attrackme;User=root;Password=IllOmAVXDrPmHvZuvFvzmKBLlJzEKMvV;";

    static async Task Main()
    {
        try
        {
            using var connection = new MySqlConnection(connString);
            await connection.OpenAsync();
            Console.WriteLine("Connected to DB.");

            // 1. Get Head user info
            var headCmd = new MySqlCommand("SELECT UserId, Username, UserType, TeacherId FROM user WHERE Username = 'head'", connection);
            using (var reader = await headCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    Console.WriteLine($"Head User: {reader["Username"]}, Type: {reader["UserType"]}, TeacherId: {reader["TeacherId"]}");
                }
            }

            // 2. Get Head teacher info to see SchoolId
            var headTeacherCmd = new MySqlCommand("SELECT t.SchoolId, s.SchoolName FROM teacher t LEFT JOIN school s ON t.SchoolId = s.SchoolId JOIN user u ON u.TeacherId = t.TeacherId WHERE u.Username = 'head'", connection);
            string headSchoolId = "";
            using (var reader = await headTeacherCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    headSchoolId = reader["SchoolId"]?.ToString() ?? "NULL";
                    Console.WriteLine($"Head SchoolId: '{headSchoolId}', SchoolName: '{reader["SchoolName"]}'");
                }
            }

            // 3. Dump some teachers
            var pCmd = new MySqlCommand("SELECT TeacherId, FullName, SchoolId, Gradelvl, Section FROM teacher LIMIT 5", connection);
            Console.WriteLine("\n--- Sample Teachers ---");
            using (var reader = await pCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"TeacherId: {reader["TeacherId"]}, Name: {reader["FullName"]}, SchoolId: '{reader["SchoolId"]}', Section: '{reader["Section"]}'");
                }
            }

            // 4. Dump some users joined to teachers
            var uCmd = new MySqlCommand("SELECT u.Username, u.UserType, u.IsActive, t.SchoolId FROM user u JOIN teacher t ON u.TeacherId = t.TeacherId LIMIT 5", connection);
            Console.WriteLine("\n--- Sample Users + Teachers ---");
            using (var reader = await uCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"User: {reader["Username"]}, Type: {reader["UserType"]}, IsActive: {reader["IsActive"]}, SchoolId: '{reader["SchoolId"]}'");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
