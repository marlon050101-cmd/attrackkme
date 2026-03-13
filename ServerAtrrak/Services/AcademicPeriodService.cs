using AttrackSharedClass.Models;
using MySql.Data.MySqlClient;
using ServerAtrrak.Data;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Data;

namespace ServerAtrrak.Services
{
    public interface IAcademicPeriodService
    {
        Task<AcademicPeriod?> GetActivePeriodAsync(string schoolId);
        Task<List<AcademicPeriod>> GetAllPeriodsAsync(string schoolId);
        Task<bool> SetActivePeriodAsync(string schoolId, string periodId);
        Task<bool> CreatePeriodAsync(CreatePeriodRequest request);
    }

    public class AcademicPeriodService : IAcademicPeriodService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<AcademicPeriodService> _logger;

        public AcademicPeriodService(Dbconnection dbConnection, ILogger<AcademicPeriodService> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<AcademicPeriod?> GetActivePeriodAsync(string schoolId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT * FROM academic_period WHERE SchoolId = @SchoolId AND IsActive = TRUE LIMIT 1";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new AcademicPeriod
                    {
                        PeriodId = reader.GetString("PeriodId"),
                        SchoolId = reader.GetString("SchoolId"),
                        SchoolYear = reader.GetString("SchoolYear"),
                        Semester = reader.GetString("Semester"),
                        IsActive = reader.GetBoolean("IsActive"),
                        StartDate = reader.IsDBNull(reader.GetOrdinal("StartDate")) ? null : reader.GetDateTime("StartDate"),
                        EndDate = reader.IsDBNull(reader.GetOrdinal("EndDate")) ? null : reader.GetDateTime("EndDate"),
                        CreatedAt = reader.GetDateTime("CreatedAt")
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active period for school {SchoolId}", schoolId);
            }
            return null;
        }

        public async Task<List<AcademicPeriod>> GetAllPeriodsAsync(string schoolId)
        {
            var periods = new List<AcademicPeriod>();
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT * FROM academic_period WHERE SchoolId = @SchoolId ORDER BY StartDate DESC";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SchoolId", schoolId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    periods.Add(new AcademicPeriod
                    {
                        PeriodId = reader.GetString("PeriodId"),
                        SchoolId = reader.GetString("SchoolId"),
                        SchoolYear = reader.GetString("SchoolYear"),
                        Semester = reader.GetString("Semester"),
                        IsActive = reader.GetBoolean("IsActive"),
                        StartDate = reader.IsDBNull(reader.GetOrdinal("StartDate")) ? null : reader.GetDateTime("StartDate"),
                        EndDate = reader.IsDBNull(reader.GetOrdinal("EndDate")) ? null : reader.GetDateTime("EndDate"),
                        CreatedAt = reader.GetDateTime("CreatedAt")
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all periods for school {SchoolId}", schoolId);
            }
            return periods;
        }

        public async Task<bool> SetActivePeriodAsync(string schoolId, string periodId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // 1. Deactivate all periods for this school
                    var deactivateQuery = "UPDATE academic_period SET IsActive = FALSE WHERE SchoolId = @SchoolId";
                    using (var cmd = new MySqlCommand(deactivateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                        cmd.Transaction = transaction;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 2. Activate the selected period
                    var activateQuery = "UPDATE academic_period SET IsActive = TRUE WHERE PeriodId = @PeriodId AND SchoolId = @SchoolId";
                    using (var cmd = new MySqlCommand(activateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@PeriodId", periodId);
                        cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                        cmd.Transaction = transaction;
                        var rows = await cmd.ExecuteNonQueryAsync();
                        
                        if (rows > 0)
                        {
                            await transaction.CommitAsync();
                            return true;
                        }
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting active period {PeriodId} for school {SchoolId}", periodId, schoolId);
            }
            return false;
        }

        public async Task<bool> CreatePeriodAsync(CreatePeriodRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"INSERT INTO academic_period (PeriodId, SchoolId, SchoolYear, Semester, StartDate, EndDate) 
                             VALUES (@PeriodId, @SchoolId, @SchoolYear, @Semester, @StartDate, @EndDate)";
                
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@PeriodId", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("@SchoolId", request.SchoolId);
                command.Parameters.AddWithValue("@SchoolYear", request.SchoolYear);
                command.Parameters.AddWithValue("@Semester", request.Semester);
                command.Parameters.AddWithValue("@StartDate", (object)request.StartDate ?? DBNull.Value);
                command.Parameters.AddWithValue("@EndDate", (object)request.EndDate ?? DBNull.Value);

                var rows = await command.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating academic period for school {SchoolId}", request.SchoolId);
            }
            return false;
        }
    }
}
