using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ServerAtrrak.Data;
using AttrackSharedClass.Models;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;

namespace ServerAtrrak.Controllers
{
    [Route("api/studentwalkin")]
    [ApiController]
    public class StudentWalkInController : ControllerBase
    {
        private readonly Dbconnection _dbconnection;

        public StudentWalkInController(Dbconnection dbconnection)
        {
            _dbconnection = dbconnection;
            EnsureTableExists();
        }

        private void EnsureTableExists()
        {
            try
            {
                using var connection = new MySqlConnection(_dbconnection.GetConnection());
                connection.Open();
                string query = @"
                    CREATE TABLE IF NOT EXISTS StudentWalkInLogs (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        FullName VARCHAR(100) NOT NULL,
                        Purpose VARCHAR(100) NOT NULL,
                        PersonToVisit VARCHAR(100) NOT NULL,
                        TimeIn DATETIME NOT NULL,
                        TimeOut DATETIME NULL,
                        Status VARCHAR(50) DEFAULT 'Inside'
                    )";
                using var command = new MySqlCommand(query, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating StudentWalkInLogs table: {ex.Message}");
            }
        }

        [HttpPost("logs")]
        [AllowAnonymous]
        public async Task<IActionResult> AddStudentWalkInLog([FromBody] StudentWalkInLog log)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                using var connection = await _dbconnection.GetConnectionAsync();
                string query = @"
                    INSERT INTO StudentWalkInLogs (FullName, Purpose, PersonToVisit, TimeIn, Status)
                    VALUES (@FullName, @Purpose, @PersonToVisit, @TimeIn, @Status)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@FullName", log.FullName);
                command.Parameters.AddWithValue("@Purpose", log.Purpose);
                command.Parameters.AddWithValue("@PersonToVisit", log.PersonToVisit);
                command.Parameters.AddWithValue("@TimeIn", DateTime.Now);
                command.Parameters.AddWithValue("@Status", "Inside");

                await command.ExecuteNonQueryAsync();

                return Ok(new { success = true, message = "Student walk-in registered successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Database Error: " + ex.Message });
            }
        }
    }
}
