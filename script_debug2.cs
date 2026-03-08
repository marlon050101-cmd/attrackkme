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

            string schoolId = "1d543c73-eebc-4c2c-99ff-4beb2dbfc12f";

            var teachersQuery = @"
                SELECT 
                    CASE 
                        WHEN u.UserType IS NOT NULL THEN u.UserType
                        WHEN t.Section IS NOT NULL AND t.Section != '' THEN 'Adviser'
                        ELSE 'SubjectTeacher'
                    END as UserType,
                    t.TeacherId
                FROM teacher t
                LEFT JOIN user u ON t.TeacherId = u.TeacherId
                LEFT JOIN school s ON t.SchoolId = s.SchoolId
                WHERE (UPPER(TRIM(t.SchoolId)) = UPPER(TRIM(@schoolId)) 
                   OR UPPER(TRIM(s.SchoolName)) = UPPER(TRIM(@schoolId)))
                AND (u.IsActive IS NULL OR u.IsActive = 1)";
            
            using (var cmd = new MySqlCommand(teachersQuery, connection))
            {
                cmd.Parameters.AddWithValue("@schoolId", schoolId.Trim());
                using var reader = await cmd.ExecuteReaderAsync();
                int totalActiveTeachers = 0;
                while (await reader.ReadAsync())
                {
                    totalActiveTeachers++;
                }
                Console.WriteLine($"teachersQuery successful. Count = {totalActiveTeachers}");
            }

            var pendingQuery = @"
                SELECT COUNT(*) 
                FROM user u 
                INNER JOIN teacher t ON u.TeacherId = t.TeacherId
                LEFT JOIN school s ON t.SchoolId = s.SchoolId
                WHERE (UPPER(TRIM(t.SchoolId)) = UPPER(TRIM(@schoolId)) 
                   OR UPPER(TRIM(s.SchoolName)) = UPPER(TRIM(@schoolId)))
                AND u.IsApproved = 0 
                AND u.UserType IN ('Teacher', 'SubjectTeacher', 'Adviser', 'Advisor')";
            using var pendingCommand = new MySqlCommand(pendingQuery, connection);
            pendingCommand.Parameters.AddWithValue("@schoolId", schoolId.Trim());
            var pendingApprovals = Convert.ToInt32(await pendingCommand.ExecuteScalarAsync());
            Console.WriteLine($"pendingQuery successful. Count = {pendingApprovals}");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error caught: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
