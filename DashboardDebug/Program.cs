using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        string connString = "Server=shinkansen.proxy.rlwy.net;Port=42561;Database=attrackme;User=root;Password=IllOmAVXDrPmHvZuvFvzmKBLlJzEKMvV;";
        string schoolId = "1d543c73-eebc-4c2c-99ff-4beb2dbfc12f";
        int days = 30;

        using (var conn = new MySqlConnection(connString))
        {
            await conn.OpenAsync();
            Console.WriteLine("Connection Opened.");

            // 1. Run the EXACT daily query from GuidanceService
            Console.WriteLine("\n--- RUNNING DAILY QUERY ---");
            var dailyQuery = @"
                SELECT s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender,
                       COUNT(DISTINCT sds.Date) as TotalDays,
                       COUNT(DISTINCT CASE WHEN sds.Status IN ('Present', 'Late', 'Partial') THEN sds.Date ELSE NULL END) as PresentDays,
                       COUNT(DISTINCT CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as AbsentDays,
                       COUNT(DISTINCT CASE WHEN sds.Status = 'Late' THEN sds.Date ELSE NULL END) as LateDays,
                       MIN(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as FirstAbsentDate,
                       MAX(CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END) as LastAbsentDate,
                       GROUP_CONCAT(DISTINCT CASE WHEN sds.Status = 'Absent' THEN sds.Date ELSE NULL END ORDER BY sds.Date SEPARATOR ', ') as AbsentDates
                FROM student s
                LEFT JOIN student_daily_summary sds ON s.StudentId = sds.StudentId 
                     AND sds.Date >= DATE_SUB(CURDATE(), INTERVAL @Days DAY)
                WHERE s.SchoolId = @SchoolId AND s.IsActive = true
                GROUP BY s.StudentId, s.FullName, s.GradeLevel, s.Section, s.Strand, s.Gender
                HAVING AbsentDays > 0";

            using (var command = new MySqlCommand(dailyQuery, conn))
            {
                command.Parameters.AddWithValue("@SchoolId", schoolId);
                command.Parameters.AddWithValue("@Days", days);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    bool found = false;
                    while (await reader.ReadAsync())
                    {
                        found = true;
                        Console.WriteLine($"Student: {reader["FullName"]} | AbsentDays: {reader["AbsentDays"]} | TotalDays: {reader["TotalDays"]}");
                    }
                    if (!found) Console.WriteLine("NO ROWS RETURNED by Daily Query.");
                }
            }

            // 2. Check curdade on server
            using (var command = new MySqlCommand("SELECT CURDATE()", conn))
            {
                var curdate = await command.ExecuteScalarAsync();
                Console.WriteLine($"\nServer CURDATE(): {curdate}");
            }
        }
    }
}
