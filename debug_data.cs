using MySql.Data.MySqlClient;
using ServerAtrrak.Data;

var db = new Dbconnection();
string connString = db.GetConnection();

using (var conn = new MySqlConnection(connString))
{
    await conn.OpenAsync();
    
    string guidanceUserId = "844f15d2-1c1b-11f1-935b-e2aaab582b53";
    
    Console.WriteLine($"--- DEBUGGING FOR USER: {guidanceUserId} ---");
    
    // 1. Check Counselor User and Teacher record
    using (var cmd = new MySqlCommand(@"
        SELECT u.UserId, u.UserName, u.UserType, t.TeacherId, t.FullName, t.SchoolId 
        FROM user u 
        INNER JOIN teacher t ON u.TeacherId = t.TeacherId 
        WHERE u.UserId = @UserId", conn))
    {
        cmd.Parameters.AddWithValue("@UserId", guidanceUserId);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                Console.WriteLine($"User Found: {reader["UserName"]} ({reader["UserType"]})");
                Console.WriteLine($"Teacher: {reader["FullName"]}");
                Console.WriteLine($"School ID: {reader["SchoolId"]}");
            }
            else
            {
                Console.WriteLine("User/Teacher record NOT FOUND in DB!");
            }
        }
    }
    
    // 2. Check Students for that school
    // We'll just check a few students in the database regardless of school first
    Console.WriteLine("\n--- SAMPLE STUDENTS ---");
    using (var cmd = new MySqlCommand("SELECT StudentId, FullName, SchoolId FROM student LIMIT 5", conn))
    {
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                Console.WriteLine($"Student: {reader["FullName"]} | ID: {reader["StudentId"]} | School: {reader["SchoolId"]}");
            }
        }
    }

    // 3. Check the specific student from user's screenshot
    string specificStudentId = "b280477f-8a8b-47b1-aab8-bcee87d3ec3c";
    Console.WriteLine($"\n--- CHECKING SPECIFIC STUDENT: {specificStudentId} ---");
    using (var cmd = new MySqlCommand("SELECT StudentId, FullName, SchoolId FROM student WHERE StudentId = @Id", conn))
    {
        cmd.Parameters.AddWithValue("@Id", specificStudentId);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                Console.WriteLine($"Found Student: {reader["FullName"]} | School: {reader["SchoolId"]}");
            }
            else
            {
                Console.WriteLine("Student NOT FOUND in database!");
            }
        }
    }
}
