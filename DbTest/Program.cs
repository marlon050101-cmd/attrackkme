using System;
using MySql.Data.MySqlClient;

namespace DbTest
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string connStr = "Server=shinkansen.proxy.rlwy.net;Port=42561;Database=attrackme;User=root;Password=IllOmAVXDrPmHvZuvFvzmKBLlJzEKMvV;";
                using var conn = new MySqlConnection(connStr);
                conn.Open();
                
                // Summary Counts
                using var cmdCountCO = new MySqlCommand("SELECT COUNT(*) FROM class_offering", conn);
                long totalCO = (long)cmdCountCO.ExecuteScalar();
                Console.WriteLine($"Total Class Offerings in DB: {totalCO}");

                using var cmdCountStud = new MySqlCommand("SELECT COUNT(*) FROM student", conn);
                long totalStud = (long)cmdCountStud.ExecuteScalar();
                Console.WriteLine($"Total Students in DB: {totalStud}");

                // Get class offerings that matter
                Console.WriteLine("\n--- Class Offerings ---");
                using var cmdCO = new MySqlCommand("SELECT ClassOfferingId, AdviserId, TeacherId, GradeLevel, Section FROM class_offering;", conn);
                using var readerCO = cmdCO.ExecuteReader();
                while (readerCO.Read())
                {
                    Console.WriteLine($"CO_ID: {readerCO[0]}, Adviser: {readerCO[1]}, Teacher: {readerCO[2]}, Grade: {readerCO[3]}, Sec: {readerCO[4]}");
                }
                readerCO.Close();

                // Get some students
                Console.WriteLine("\n--- Students Table (Distinct Sections) ---");
                using var cmdStud = new MySqlCommand("SELECT GradeLevel, Section, COUNT(*) as Count FROM student GROUP BY GradeLevel, Section;", conn);
                using var readerStud = cmdStud.ExecuteReader();
                while (readerStud.Read())
                {
                    Console.WriteLine($"Grade: {readerStud[0]}, Sec: {readerStud[1]}, Student Count: {readerStud[2]}");
                }
                readerStud.Close();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
