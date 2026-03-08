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

            Console.WriteLine("Running Total Offerings Query...");
            var totalOfferingsQuery = @"
                SELECT COUNT(*) FROM class_offering co 
                INNER JOIN teacher t ON co.AdvisorId = t.TeacherId 
                LEFT JOIN school s ON t.SchoolId = s.SchoolId
                WHERE (UPPER(TRIM(t.SchoolId)) = UPPER(TRIM(@schoolId)) 
                   OR UPPER(TRIM(s.SchoolName)) = UPPER(TRIM(@schoolId)))";
            using var totalOfferingsCmd = new MySqlCommand(totalOfferingsQuery, connection);
            totalOfferingsCmd.Parameters.AddWithValue("@schoolId", schoolId.Trim());
            int totalClassOfferings = Convert.ToInt32(await totalOfferingsCmd.ExecuteScalarAsync());
            Console.WriteLine($"Total Offerings: {totalClassOfferings}");

            Console.WriteLine("Running Assigned Offerings Query...");
            var assignedOfferingsQuery = @"
                SELECT COUNT(*) FROM class_offering co 
                INNER JOIN teacher t ON co.AdvisorId = t.TeacherId 
                LEFT JOIN school s ON t.SchoolId = s.SchoolId
                WHERE (UPPER(TRIM(t.SchoolId)) = UPPER(TRIM(@schoolId)) 
                   OR UPPER(TRIM(s.SchoolName)) = UPPER(TRIM(@schoolId))) 
                AND co.TeacherId IS NOT NULL AND co.TeacherId != ''";
            using var assignedOfferingsCmd = new MySqlCommand(assignedOfferingsQuery, connection);
            assignedOfferingsCmd.Parameters.AddWithValue("@schoolId", schoolId.Trim());
            int assignedClassOfferings = Convert.ToInt32(await assignedOfferingsCmd.ExecuteScalarAsync());
            Console.WriteLine($"Assigned Offerings: {assignedClassOfferings}");

            Console.WriteLine("Running Active Today Query...");
            var activeTodayQuery = @"
                SELECT DISTINCT t.FullName, u.TeacherId, MAX(sa.UpdatedAt) as MaxUpdatedAt
                FROM subject_attendance sa
                INNER JOIN class_offering co ON sa.ClassOfferingId = co.ClassOfferingId
                INNER JOIN teacher t ON co.TeacherId = t.TeacherId
                INNER JOIN user u ON t.TeacherId = u.TeacherId
                LEFT JOIN school s ON t.SchoolId = s.SchoolId
                WHERE (UPPER(TRIM(t.SchoolId)) = UPPER(TRIM(@schoolId)) 
                   OR UPPER(TRIM(s.SchoolName)) = UPPER(TRIM(@schoolId)))
                AND sa.Date = CURDATE()
                GROUP BY t.TeacherId, t.FullName, u.TeacherId
                ORDER BY MaxUpdatedAt DESC";
            using (var activeTodayCmd = new MySqlCommand(activeTodayQuery, connection))
            {
                activeTodayCmd.Parameters.AddWithValue("@schoolId", schoolId.Trim());
                using var activeReader = await activeTodayCmd.ExecuteReaderAsync();
                int actCount = 0;
                while (await activeReader.ReadAsync()) actCount++;
                Console.WriteLine($"Active Today: {actCount}");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error caught: {ex.Message}");
        }
    }
}
