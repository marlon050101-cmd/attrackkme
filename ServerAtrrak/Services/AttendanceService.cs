using AttrackSharedClass.Models;
using ServerAtrrak.Data;
using ServerAtrrak.Hubs;
using Microsoft.AspNetCore.SignalR;
using MySql.Data.MySqlClient;

namespace ServerAtrrak.Services
{
    public class AttendanceService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<AttendanceService> _logger;
        private readonly IHubContext<AttendanceHub> _hubContext;

        public AttendanceService(Dbconnection dbConnection, ILogger<AttendanceService> logger, IHubContext<AttendanceHub> hubContext)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<AttendanceResponse> MarkAttendanceAsync(AttendanceRequest request)
        {
            try
            {
                _logger.LogInformation("Marking attendance for student {StudentId} with type {AttendanceType}", 
                    request.StudentId, request.AttendanceType);

                // Validate student exists
                var studentName = await GetStudentNameAsync(request.StudentId);
                if (string.IsNullOrEmpty(studentName) || studentName == "Unknown Student")
                {
                    return new AttendanceResponse
                    {
                        Success = true,
                        Message = "Student not found",
                        IsValid = false,
                        StudentName = "Unknown Student"
                    };
                }

                // Check if attendance already marked today for this type
                var alreadyMarked = await IsAttendanceAlreadyMarkedAsync(request.StudentId, request.Timestamp.Date, request.AttendanceType);
                
                if (alreadyMarked)
                {
                    _logger.LogInformation("Attendance already marked for student {StudentId} for type {AttendanceType}", 
                        request.StudentId, request.AttendanceType);
                    
                    return new AttendanceResponse
                    {
                        Success = true,
                        Message = $"{request.AttendanceType} already marked for today",
                        IsValid = true,
                        StudentName = studentName,
                        Status = "Present",
                        AttendanceType = request.AttendanceType
                    };
                }

                // Determine status and remarks
                var (status, remarks) = await DetermineAttendanceStatusAndRemarksAsync(request);

                // Mark attendance
                await InsertAttendanceRecordAsync(request, status, remarks);

                _logger.LogInformation("Successfully marked attendance for student {StudentId} with status {Status} and remarks {Remarks}", 
                    request.StudentId, status, remarks);

                // REALTIME UPDATE: Broadcast to SignalR Hub
                try
                {
                    if (!string.IsNullOrEmpty(request.TeacherId))
                    {
                        var updatePayload = new
                        {
                            StudentId = request.StudentId,
                            StudentName = studentName,
                            Status = status,
                            Remarks = remarks,
                            AttendanceType = request.AttendanceType,
                            Timestamp = request.Timestamp
                        };

                        // Broadcast to the alphanumeric ID group (e.g., T123)
                        await _hubContext.Clients.Group(request.TeacherId).SendAsync("ReceiveAttendanceUpdate", updatePayload);
                        _logger.LogInformation("SignalR: Broadcasted to TeacherId group: {TeacherId}", request.TeacherId);

                        // Attempt to find the UserId to broadcast to that group as well
                        var userId = await GetUserIdFromTeacherIdAsync(request.TeacherId);
                        if (!string.IsNullOrEmpty(userId) && userId != request.TeacherId)
                        {
                            await _hubContext.Clients.Group(userId).SendAsync("ReceiveAttendanceUpdate", updatePayload);
                            _logger.LogInformation("SignalR: Also broadcasted to UserId group: {UserId}", userId);
                        }
                    }
                }
                catch (Exception hubEx)
                {
                    _logger.LogError(hubEx, "SignalR: Error broadcasting update: {ErrorMessage}", hubEx.Message);
                }

                return new AttendanceResponse
                {
                    Success = true,
                    Message = $"{request.AttendanceType} marked successfully",
                    IsValid = true,
                    StudentName = studentName,
                    Status = status,
                    AttendanceType = request.AttendanceType,
                    Remarks = remarks
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking attendance for student {StudentId}: {ErrorMessage}", 
                    request.StudentId, ex.Message);
                
                return new AttendanceResponse
                {
                    Success = false,
                    Message = "An error occurred while marking attendance"
                };
            }
        }

        public async Task<List<AttendanceRecord>> GetTodayAttendanceAsync(string teacherId)
        {
            try
            {
                return await GetAttendanceForTeacherAsync(teacherId, DateTime.Today);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting today's attendance for teacher {TeacherId}: {ErrorMessage}", 
                    teacherId, ex.Message);
                return new List<AttendanceRecord>();
            }
        }

        public async Task<List<AttendanceRecord>> GetAttendanceForTeacherAsync(string teacherId, DateTime date)
        {
            var attendance = new List<AttendanceRecord>();

            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT da.StudentId, s.FullName, da.Date, da.TimeIn, da.TimeOut, da.Status, da.Remarks
                    FROM daily_attendance da
                    INNER JOIN student s ON da.StudentId = s.StudentId
                    WHERE da.Date = @Date
                    ORDER BY da.Date DESC, da.TimeIn DESC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Date", date.Date);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var studentId = reader.GetString(0);
                    var studentName = reader.GetString(1);
                    var attendanceDate = reader.GetDateTime(2);
                    var timeIn = reader.IsDBNull(3) ? (TimeSpan?)null : ((MySql.Data.MySqlClient.MySqlDataReader)reader).GetTimeSpan(3);
                    var timeOut = reader.IsDBNull(4) ? (TimeSpan?)null : ((MySql.Data.MySqlClient.MySqlDataReader)reader).GetTimeSpan(4);
                    var status = reader.GetString(5);
                    var remarks = reader.IsDBNull(6) ? null : reader.GetString(6);

                    // Create separate records for TimeIn and TimeOut if they exist
                    if (timeIn.HasValue)
                    {
                        attendance.Add(new AttendanceRecord
                        {
                            StudentId = studentId,
                            StudentName = studentName,
                            Timestamp = attendanceDate.Date.Add(timeIn.Value),
                            Status = status,
                        IsValid = true,
                        AttendanceType = "TimeIn",
                            Message = "Time In recorded",
                            Remarks = remarks
                        });
                    }

                    if (timeOut.HasValue)
                {
                    attendance.Add(new AttendanceRecord
                    {
                            StudentId = studentId,
                            StudentName = studentName,
                            Timestamp = attendanceDate.Date.Add(timeOut.Value),
                            Status = status,
                        IsValid = true,
                            AttendanceType = "TimeOut",
                            Message = "Time Out recorded",
                            Remarks = remarks
                    });
                    }
                }

                _logger.LogInformation("Retrieved {Count} attendance records for teacher {TeacherId} on {Date}", 
                    attendance.Count, teacherId, date.Date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance for teacher {TeacherId} on {Date}: {ErrorMessage}", 
                    teacherId, date, ex.Message);
            }

            return attendance;
        }

       

        private async Task<bool> IsAttendanceAlreadyMarkedAsync(string studentId, DateTime date, string attendanceType)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = @"
                SELECT COUNT(*) 
                FROM daily_attendance 
                WHERE StudentId = @StudentId 
                AND Date = @Date
                AND (@AttendanceType = 'TimeIn' AND TimeIn IS NOT NULL)
                OR (@AttendanceType = 'TimeOut' AND TimeOut IS NOT NULL)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@StudentId", studentId);
            command.Parameters.AddWithValue("@Date", date.Date);
            command.Parameters.AddWithValue("@AttendanceType", attendanceType);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }

        private async Task<string> GetStudentNameAsync(string studentId)
        {
            using var connection = new MySqlConnection(_dbConnection.GetConnection());
            await connection.OpenAsync();

            var query = "SELECT FullName FROM student WHERE StudentId = @StudentId";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@StudentId", studentId);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown Student";
        }

     

        private async Task<(string status, string remarks)> DetermineAttendanceStatusAndRemarksAsync(AttendanceRequest request)
        {
            try
            {
                var currentTime = request.Timestamp.TimeOfDay;
                var sevenThirtyOne = new TimeSpan(7, 31, 0); // 7:31 AM - late threshold
                var isLate = currentTime >= sevenThirtyOne;

                if (request.AttendanceType == "TimeIn")
                {
                    var status = isLate ? "Late" : "Present";
                    return (status, "");
                }
                else if (request.AttendanceType == "TimeOut")
                {
                    // Get Time In record to calculate total hours
                    var timeInRecord = await GetTimeInRecordAsync(request.StudentId, request.Timestamp.Date);
                    
                    if (timeInRecord.HasValue)
                    {
                        var timeInHour = timeInRecord.Value.Hours;
                        var timeOutHour = request.Timestamp.TimeOfDay.Hours;
                        var timeInWasLate = timeInRecord.Value >= sevenThirtyOne;
                        
                        string dayType;
                        
                        // Check if it's a whole day (7:30 AM - 4:30 PM range)
                        if (timeInHour <= 7 && timeOutHour >= 16) // 7:30 AM to 4:30 PM
                        {
                            dayType = "Whole Day";
                        }
                        else
                        {
                            // All other combinations are Half Day
                            dayType = "Half Day";
                        }
                        
                        var remarks = timeInWasLate ? $"Late - {dayType}" : dayType;
                        return ("Present", remarks);
                    }
                    else
                    {
                        // Fallback if no Time In found
                        var remarks = currentTime < new TimeSpan(12, 0, 0) ? "Half Day" : "Whole Day";
                        return ("Present", remarks);
                    }
                }

                return ("Present", "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining attendance status for student {StudentId}: {ErrorMessage}", 
                    request.StudentId, ex.Message);
                return ("Present", "");
            }
        }

        private async Task<TimeSpan?> GetTimeInRecordAsync(string studentId, DateTime date)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT TimeIn 
                    FROM daily_attendance 
                    WHERE StudentId = @StudentId 
                    AND Date = @Date 
                    AND TimeIn IS NOT NULL";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@Date", date.Date);

                var result = await command.ExecuteScalarAsync();
                return result != null ? (TimeSpan)result : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Time In record for student {StudentId}: {ErrorMessage}", 
                    studentId, ex.Message);
                return null;
            }
        }

        private async Task InsertAttendanceRecordAsync(AttendanceRequest request, string status, string remarks)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Check if attendance record already exists for today
                var existingQuery = @"
                    SELECT AttendanceId FROM daily_attendance 
                    WHERE StudentId = @StudentId 
                    AND Date = @Date";

                using var existingCommand = new MySqlCommand(existingQuery, connection);
                existingCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                existingCommand.Parameters.AddWithValue("@Date", request.Timestamp.Date);

                var existingId = await existingCommand.ExecuteScalarAsync();

                if (existingId != null)
                {
                    // Update existing record - FIX: Use proper column names
                    string updateQuery;
                    if (request.AttendanceType == "TimeIn")
                    {
                        updateQuery = @"
                            UPDATE daily_attendance 
                            SET TimeIn = @TimeValue, 
                                Status = @Status,
                                Remarks = @Remarks,
                                UpdatedAt = @UpdatedAt
                            WHERE AttendanceId = @AttendanceId";
                    }
                    else
                    {
                        updateQuery = @"
                            UPDATE daily_attendance 
                            SET TimeOut = @TimeValue, 
                                Status = @Status,
                                Remarks = @Remarks,
                                UpdatedAt = @UpdatedAt
                            WHERE AttendanceId = @AttendanceId";
                    }

                    using var updateCommand = new MySqlCommand(updateQuery, connection);
                    updateCommand.Parameters.AddWithValue("@TimeValue", request.Timestamp.TimeOfDay);
                    updateCommand.Parameters.AddWithValue("@Status", status);
                    updateCommand.Parameters.AddWithValue("@Remarks", string.IsNullOrEmpty(remarks) ? (object)DBNull.Value : remarks);
                    updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    updateCommand.Parameters.AddWithValue("@AttendanceId", existingId);

                    await updateCommand.ExecuteNonQueryAsync();
                }
                else
                {
                    // Insert new record
                    var insertQuery = @"
                        INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
                        VALUES (@AttendanceId, @StudentId, @Date, @TimeIn, @TimeOut, @Status, @Remarks, @CreatedAt, @UpdatedAt)";

                    using var insertCommand = new MySqlCommand(insertQuery, connection);
                    insertCommand.Parameters.AddWithValue("@AttendanceId", Guid.NewGuid().ToString());
                    insertCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                    insertCommand.Parameters.AddWithValue("@Date", request.Timestamp.Date);
                    insertCommand.Parameters.AddWithValue("@TimeIn", request.AttendanceType == "TimeIn" ? request.Timestamp.TimeOfDay : (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@TimeOut", request.AttendanceType == "TimeOut" ? request.Timestamp.TimeOfDay : (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@Status", status);
                    insertCommand.Parameters.AddWithValue("@Remarks", string.IsNullOrEmpty(remarks) ? (object)DBNull.Value : remarks);
                    insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                    insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                    await insertCommand.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting attendance record for student {StudentId}: {ErrorMessage}", 
                    request.StudentId, ex.Message);
                throw; // Re-throw to be handled by the calling method
            }
        }

        private async Task<string?> GetUserIdFromTeacherIdAsync(string teacherId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = "SELECT UserId FROM user WHERE TeacherId = @TeacherId AND UserType = 'SubjectTeacher' LIMIT 1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@TeacherId", teacherId);

                var result = await command.ExecuteScalarAsync();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting UserId from TeacherId: {ErrorMessage}", ex.Message);
                return null;
            }
        }
    }


    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
    }
}
