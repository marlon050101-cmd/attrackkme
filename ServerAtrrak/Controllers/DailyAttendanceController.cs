using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using ServerAtrrak.Services;
using ServerAtrrak.Models;
using MySql.Data.MySqlClient;
using ServerAtrrak.Data;
using System.Data;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DailyAttendanceController : ControllerBase
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<DailyAttendanceController> _logger;
        private readonly GsmSmsService _smsService;

        public DailyAttendanceController(Dbconnection dbConnection, ILogger<DailyAttendanceController> logger, GsmSmsService smsService)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _smsService = smsService;
        }


        /// <summary>
        /// Sends an SMS to the parent in a fire-and-forget background task.
        /// </summary>
        /// <summary>
        /// Queues an SMS in the database to be picked up by the local SMS Hub.
        /// </summary>
        private void SendParentSmsFireAndForget(string? parentNumber, string studentName, string attendanceType, DateTime time)
        {
            if (string.IsNullOrWhiteSpace(parentNumber)) return;
            
            var message = GsmSmsService.BuildSmsMessage(studentName, attendanceType, time);
            
            _ = Task.Run(async () => {
                try 
                {
                    using var connection = new MySqlConnection(_dbConnection.GetConnection());
                    await connection.OpenAsync();
                    
                    var sql = @"INSERT INTO sms_queue (PhoneNumber, Message, StudentId, ScheduledAt, IsSent) 
                               VALUES (@Phone, @Msg, @Sid, @Date, 0)";
                    
                    using var cmd = new MySqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@Phone", parentNumber);
                    cmd.Parameters.AddWithValue("@Msg", message);
                    cmd.Parameters.AddWithValue("@Sid", "Unknown"); // Can be updated to real ID if needed
                    cmd.Parameters.AddWithValue("@Date", DateTime.Now);
                    
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("[SMS-QUEUE] Queued message for {Name}", studentName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SMS-QUEUE] Failed to queue message for {Phone}", parentNumber);
                }
            });
        }

        [HttpGet("pending-sms")]
        public async Task<ActionResult<List<SmsQueueItem>>> GetPendingSms()
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                var sql = "SELECT Id, PhoneNumber, Message, StudentId, ScheduledAt FROM sms_queue WHERE IsSent = 0 LIMIT 20";
                using var cmd = new MySqlCommand(sql, connection);
                
                var list = new List<SmsQueueItem>();
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new SmsQueueItem
                    {
                        Id = r.GetInt32(0),
                        PhoneNumber = r.GetString(1),
                        Message = r.GetString(2),
                        StudentId = r.GetString(3),
                        ScheduledAt = r.GetDateTime(4)
                    });
                }
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending SMS");
                return StatusCode(500, "Error fetching pending SMS");
            }
        }

        [HttpPost("mark-sms-sent")]
        public async Task<IActionResult> MarkSmsSent([FromBody] int id)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                
                var sql = "UPDATE sms_queue SET IsSent = 1, SentAt = @Now WHERE Id = @Id";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Now", DateTime.Now);
                cmd.Parameters.AddWithValue("@Id", id);
                
                await cmd.ExecuteNonQueryAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking SMS as sent");
                return StatusCode(500, "Error marking SMS as sent");
            }
        }

        private async Task<(bool ok, string msg)> ValidateTeacherStudentAsync(MySqlConnection connection, string teacherId, string studentId)
        {
            var sql = @"
                SELECT 
                    s.SchoolId AS StudentSchoolId, s.GradeLevel AS StudentGrade, s.Section AS StudentSection,
                    t.SchoolId AS TeacherSchoolId, COALESCE(t.Gradelvl, 0) AS TeacherGrade, COALESCE(t.Section, '') AS TeacherSection
                FROM student s
                INNER JOIN teacher t ON t.TeacherId = @TeacherId
                WHERE s.StudentId = @StudentId
                LIMIT 1";
            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@TeacherId", teacherId);
            cmd.Parameters.AddWithValue("@StudentId", studentId);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return (false, "Student/Teacher not found");

            var sSchool = r.GetString("StudentSchoolId");
            var tSchool = r.GetString("TeacherSchoolId");
            var sGrade  = r.GetInt32("StudentGrade");
            var tGrade  = r.GetInt32("TeacherGrade");
            var sSect   = r.GetString("StudentSection");
            var tSect   = r.GetString("TeacherSection");

            if (!string.Equals(sSchool, tSchool, StringComparison.OrdinalIgnoreCase))
                return (false, "School mismatch");
            if (tGrade > 0 && sGrade > 0 && tGrade != sGrade)
                return (false, "Grade level mismatch");
            if (!string.IsNullOrWhiteSpace(tSect) && !string.IsNullOrWhiteSpace(sSect) && !string.Equals(tSect, sSect, StringComparison.OrdinalIgnoreCase))
                return (false, $"Section mismatch (Teacher: {tSect}, Student: {sSect})");

            return (true, "");
        }

        [HttpPost("test-sms")]
        public async Task<IActionResult> TestSms([FromQuery] string phoneNumber, [FromQuery] string message = "Test message from Attrak system")
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return BadRequest("Phone number is required");

            _logger.LogInformation("[SMS] Test request for {Phone}", phoneNumber);
            await _smsService.SendSmsAsync(phoneNumber, message);
            
            return Ok(new { Message = $"Test SMS sent to {phoneNumber}. Check server logs for results." });
        }

        [HttpGet("daily-status/{studentId}")]
        public async Task<ActionResult<DailyAttendanceStatus>> GetDailyStatus(string studentId, [FromQuery] DateTime date)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                  
                var query = @"
                    SELECT TimeIn, Status, Remarks 
                    FROM daily_attendance 
                    WHERE StudentId = @StudentId AND Date = @Date";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@Date", date.Date);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var timeIn = reader.IsDBNull("TimeIn") ? null : reader.GetString("TimeIn");
                    var status = reader.GetString("Status");
                    
                    return Ok(new DailyAttendanceStatus
                    {
                        Status = status,
                        TimeIn = timeIn
                    });
                }

                return Ok(new DailyAttendanceStatus
                {
                    Status = "Not Marked",
                    TimeIn = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily status for student: {StudentId}", studentId);
                return StatusCode(500, "Error retrieving daily status");
            }
        }

        [HttpPost("daily-timein")]
        public async Task<ActionResult<DailyTimeInResponse>> DailyTimeIn([FromBody] DailyTimeInRequest request)
        {
            try
            {
                if (request == null || !ModelState.IsValid)
                {
                    return BadRequest(new DailyTimeInResponse
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Hard validation: ensure teacher-student match (school/grade/section)
                var (ok, msg) = await ValidateTeacherStudentAsync(connection, request.TeacherId, request.StudentId);
                if (!ok)
                {
                    _logger.LogWarning("Validation failed for TeacherId={TeacherId}, StudentId={StudentId}: {Message}", request.TeacherId, request.StudentId, msg);
                    return BadRequest(new DailyTimeInResponse
                    {
                        Success = false,
                        Message = $"Validation failed: {msg}"
                    });
                }

                _logger.LogInformation("Request - StudentId: {StudentId}, Date: {Date}", 
                    request.StudentId, request.Date);

                // Check if there's already a record for this student on the SAME date
                var checkQuery = "SELECT AttendanceId, TimeIn, TimeOut, Date FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date ORDER BY CreatedAt DESC";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                checkCommand.Parameters.AddWithValue("@Date", DateTime.Now.Date);

                using var reader = await checkCommand.ExecuteReaderAsync();
                var existingId = "";
                var existingTimeIn = "";
                var existingTimeOut = "";
                var hasMultipleRecords = false;
                
                if (await reader.ReadAsync())
                {
                    existingId = reader.GetString("AttendanceId");
                    // Read TimeIn - try TimeSpan first, fallback to string if it fails
                    try
                    {
                        existingTimeIn = reader.IsDBNull("TimeIn") ? "" : ((TimeSpan)reader.GetValue("TimeIn")).ToString(@"hh\:mm\:ss");
                    }
                    catch
                    {
                        // Fallback: read as string if TimeSpan conversion fails
                        existingTimeIn = reader.IsDBNull("TimeIn") ? "" : reader.GetString("TimeIn");
                    }
                    
                    try
                    {
                        existingTimeOut = reader.IsDBNull("TimeOut") ? "" : ((TimeSpan)reader.GetValue("TimeOut")).ToString(@"hh\:mm\:ss");
                    }
                    catch
                    {
                        existingTimeOut = reader.IsDBNull("TimeOut") ? "" : reader.GetString("TimeOut");
                    }
                    
                    // Check if there are multiple records (duplicates)
                    if (await reader.ReadAsync())
                    {
                        hasMultipleRecords = true;
                        _logger.LogWarning("Found duplicate records for student {StudentId} on {Date}. Will consolidate.", request.StudentId, request.Date.Date);
                    }
                }
                reader.Close();
                
                // If there are duplicates, delete all but the first one
                if (hasMultipleRecords)
                {
                    var deleteQuery = "DELETE FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date AND AttendanceId != @KeepId";
                    using var deleteCommand = new MySqlCommand(deleteQuery, connection);
                    deleteCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                    deleteCommand.Parameters.AddWithValue("@Date", DateTime.Now.Date);
                    deleteCommand.Parameters.AddWithValue("@KeepId", existingId);
                    await deleteCommand.ExecuteNonQueryAsync();
                }

                // Determine status based on time (Consolidated with client-side rules)
                var nowTime = request.TimeIn;
                string status = "Present";
                
                // Morning: 7:00 AM - 10:59 AM is Late
                if (nowTime >= new TimeSpan(7, 0, 0) && nowTime <= new TimeSpan(10, 59, 59))
                {
                    status = "Late";
                }
                // Afternoon: 1:05 PM or later is late
                else if (nowTime >= new TimeSpan(13, 5, 0))
                {
                    status = "Late";
                }
                
                bool isLate = status == "Late";

                if (!string.IsNullOrEmpty(existingId))
                {
                    // Check if TimeIn already exists for this student today
                    if (!string.IsNullOrEmpty(existingTimeIn))
                    {
                        _logger.LogInformation("TimeIn already exists for student: {StudentId} on date: {Date}, existing TimeIn: {ExistingTimeIn}", 
                            request.StudentId, request.Date.Date, existingTimeIn);
                        return Ok(new DailyTimeInResponse
                        {
                            Success = true,
                            Message = "SUCCESS: Time In already recorded for today. Please mark Time Out instead.",
                            Status = "Present",
                            TimeIn = existingTimeIn
                        });
                    }
                    
                    // Update existing record (only if TimeIn doesn't exist yet)
                    var updateQuery = @"
                        UPDATE daily_attendance 
                        SET TimeIn = @TimeIn, 
                            Status = @Status,
                            Remarks = @Remarks,
                            UpdatedAt = @UpdatedAt
                        WHERE AttendanceId = @AttendanceId";

                    using var updateCommand = new MySqlCommand(updateQuery, connection);
                    updateCommand.Parameters.AddWithValue("@TimeIn", request.TimeIn.ToString(@"hh\:mm\:ss"));
                    updateCommand.Parameters.AddWithValue("@Status", status);
                    updateCommand.Parameters.AddWithValue("@Remarks", isLate ? "Late arrival" : "");
                    updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    updateCommand.Parameters.AddWithValue("@AttendanceId", existingId);

                    await updateCommand.ExecuteNonQueryAsync();
                }
                else
                {
                    // Insert new record
                    var insertQuery = @"
                        INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, Status, Remarks, CreatedAt)
                        VALUES (@AttendanceId, @StudentId, @Date, @TimeIn, @Status, @Remarks, @CreatedAt)";

                    using var insertCommand = new MySqlCommand(insertQuery, connection);
                    insertCommand.Parameters.AddWithValue("@AttendanceId", Guid.NewGuid().ToString());
                    insertCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                    insertCommand.Parameters.AddWithValue("@Date", DateTime.Now.Date);
                    insertCommand.Parameters.AddWithValue("@TimeIn", request.TimeIn.ToString(@"hh\:mm\:ss"));
                    insertCommand.Parameters.AddWithValue("@Status", status);
                    insertCommand.Parameters.AddWithValue("@Remarks", isLate ? "Late arrival" : "");
                    insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                    await insertCommand.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Daily Time In marked for student: {StudentId}, Status: {Status}", request.StudentId, status);

                // Get student name and parent number for SMS
                var studentNameForSms = "Student";
                string? parentNumberForSms = null;
                try
                {
                    var nameSql = "SELECT FullName, ParentsNumber FROM student WHERE StudentId = @StudentId LIMIT 1";
                    using var nameCmd = new MySqlCommand(nameSql, connection);
                    nameCmd.Parameters.AddWithValue("@StudentId", request.StudentId);
                    using var nameReader = await nameCmd.ExecuteReaderAsync();
                    if (await nameReader.ReadAsync())
                    {
                        studentNameForSms = nameReader.IsDBNull(0) ? "Student" : nameReader.GetString(0);
                        parentNumberForSms = nameReader.IsDBNull(1) ? null : nameReader.GetString(1);
                        _logger.LogInformation("[SMS] TimeIn trigger - Student: {Name}, Parent Phone: {Phone}", studentNameForSms, parentNumberForSms ?? "NOT FOUND");
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "[SMS] TimeIn name lookup failed for StudentId: {StudentId}", request.StudentId); }

                // Fire-and-forget SMS to parent
                SendParentSmsFireAndForget(parentNumberForSms, studentNameForSms, "TimeIn", DateTime.Now);


                
                return Ok(new DailyTimeInResponse
                {
                    Success = true,
                    Message = "Attendance marked successfully",
                    Status = status,
                    TimeIn = request.TimeIn.ToString(@"hh\:mm\:ss")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking daily attendance for student: {StudentId}", request.StudentId);
                return StatusCode(500, new DailyTimeInResponse
                {
                    Success = false,
                    Message = "An error occurred while marking attendance"
                });
            }
        }

        [HttpPost("daily-timeout")]
        public async Task<ActionResult<DailyTimeOutResponse>> DailyTimeOut([FromBody] DailyTimeOutRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for TimeOut request: {ModelState}", ModelState);
                    return BadRequest(new DailyTimeOutResponse
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                _logger.LogInformation("TimeOut request received for student: {StudentId}, Date: {Date}, TimeOut: {TimeOut}", 
                    request.StudentId, request.Date, request.TimeOut);
                
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();
                _logger.LogInformation("Database connection opened successfully");

                // Hard validation: ensure teacher-student match (school/grade/section)
                var (ok, msg) = await ValidateTeacherStudentAsync(connection, request.TeacherId, request.StudentId);
                if (!ok)
                {
                    _logger.LogWarning("Validation failed for TeacherId={TeacherId}, StudentId={StudentId}: {Message}", request.TeacherId, request.StudentId, msg);
                    return BadRequest(new DailyTimeOutResponse
                    {
                        Success = false,
                        Message = $"Validation failed: {msg}"
                    });
                }

                // Check if Time In exists for today - get the LATEST record to avoid duplicates
                var checkQuery = "SELECT AttendanceId, TimeIn, Status, TimeOut FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date ORDER BY CreatedAt DESC LIMIT 1";
                _logger.LogInformation("Executing query: {Query} with StudentId: {StudentId}, Date: {Date}", 
                    checkQuery, request.StudentId, request.Date.Date);
                
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                checkCommand.Parameters.AddWithValue("@Date", DateTime.Now.Date);

                using var reader = await checkCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    _logger.LogWarning("No TimeIn record found for student: {StudentId} on date: {Date}", 
                        request.StudentId, request.Date.Date);
                    return BadRequest(new DailyTimeOutResponse
                    {
                        Success = false,
                        Message = "No Time In found for today. Please mark Time In first."
                    });
                }

                var attendanceId = reader.GetString("AttendanceId");
                // Read TimeIn safely
                string timeIn;
                try
                {
                    timeIn = reader.IsDBNull("TimeIn") ? "" : ((TimeSpan)reader.GetValue("TimeIn")).ToString(@"hh\:mm\:ss");
                }
                catch
                {
                    timeIn = reader.IsDBNull("TimeIn") ? "" : reader.GetString("TimeIn");
                }
                var currentStatus = reader.GetString("Status");
                var existingTimeOut = reader.IsDBNull("TimeOut") ? "" : ((TimeSpan)reader.GetValue("TimeOut")).ToString(@"hh\:mm\:ss");
                reader.Close();
                
                _logger.LogInformation("Found TimeIn record - AttendanceId: {AttendanceId}, TimeIn: {TimeIn}, Status: {Status}, ExistingTimeOut: {ExistingTimeOut}", 
                    attendanceId, timeIn, currentStatus, existingTimeOut);

                // Check if Time Out already exists for this specific record
                if (!string.IsNullOrEmpty(existingTimeOut))
                {
                    _logger.LogInformation("TimeOut already exists for student: {StudentId}, existing TimeOut: {ExistingTimeOut}", 
                        request.StudentId, existingTimeOut);
                    return Ok(new DailyTimeOutResponse
                    {
                        Success = true,
                        Message = "SUCCESS: Time Out already recorded for today",
                        TimeOut = existingTimeOut
                    });
                }

                // Calculate remarks based on time ranges
                var timeInTime = TimeSpan.Parse(timeIn);
                var timeOutTime = request.TimeOut;
                var timeInHour = timeInTime.Hours;
                var timeOutHour = timeOutTime.Hours;
                
                string remarks;
                var nowTime = timeInTime;
                bool isLate = false;

                // Determine if late using same rules as TimeIn
                if (nowTime >= new TimeSpan(7, 0, 0) && nowTime <= new TimeSpan(10, 59, 59)) isLate = true;
                else if (nowTime >= new TimeSpan(13, 5, 0)) isLate = true;
                
                // Check if it's a whole day (Arrived before 7:30 AM and Left after 4:30 PM)
                if (timeInTime <= new TimeSpan(7, 30, 0) && timeOutTime >= new TimeSpan(16, 30, 0))
                {
                    remarks = isLate ? "Late - Whole Day" : "Whole Day";
                }
                else
                {
                    // All other combinations are Half Day
                    remarks = isLate ? "Late - Half Day" : "Half Day";
                }

                // Update the record with Time Out using specific AttendanceId
                var updateQuery = @"
                    UPDATE daily_attendance 
                    SET TimeOut = @TimeOut, 
                        Remarks = @Remarks,
                        UpdatedAt = @UpdatedAt
                    WHERE AttendanceId = @AttendanceId";

                _logger.LogInformation("Executing update query: {Query} with TimeOut: {TimeOut}, Remarks: {Remarks}, AttendanceId: {AttendanceId}", 
                    updateQuery, request.TimeOut.ToString(@"hh\:mm"), remarks, attendanceId);

                using var updateCommand = new MySqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@TimeOut", request.TimeOut.ToString(@"hh\:mm"));
                updateCommand.Parameters.AddWithValue("@Remarks", remarks);
                updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                updateCommand.Parameters.AddWithValue("@AttendanceId", attendanceId);

                var rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Update completed, rows affected: {RowsAffected}", rowsAffected);

                _logger.LogInformation("Daily Time Out marked for student: {StudentId}, Remarks: {Remarks}", request.StudentId, remarks);


                // Get student name and parent number for SMS
                var studentNameForSms = "Student";
                string? parentNumberForSms = null;
                try
                {
                    var nameSql = "SELECT FullName, ParentsNumber FROM student WHERE StudentId = @StudentId LIMIT 1";
                    using var nameCmd = new MySqlCommand(nameSql, connection);
                    nameCmd.Parameters.AddWithValue("@StudentId", request.StudentId);
                    using var nameReader = await nameCmd.ExecuteReaderAsync();
                    if (await nameReader.ReadAsync())
                    {
                        studentNameForSms = nameReader.IsDBNull(0) ? "Student" : nameReader.GetString(0);
                        parentNumberForSms = nameReader.IsDBNull(1) ? null : nameReader.GetString(1);
                        _logger.LogInformation("[SMS] TimeOut trigger - Student: {Name}, Parent Phone: {Phone}", studentNameForSms, parentNumberForSms ?? "NOT FOUND");
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "[SMS] TimeOut name lookup failed for StudentId: {StudentId}", request.StudentId); }

                // Fire-and-forget SMS to parent
                SendParentSmsFireAndForget(parentNumberForSms, studentNameForSms, "TimeOut", DateTime.Now);


                
                return Ok(new DailyTimeOutResponse
                {
                    Success = true,
                    Message = "Time Out marked successfully",
                    Remarks = remarks,
                    TimeOut = request.TimeOut.ToString(@"hh\:mm")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking daily Time Out for student: {StudentId}, TimeOut: {TimeOut}, Exception: {Exception}", 
                    request.StudentId, request.TimeOut, ex.ToString());
                return StatusCode(500, new DailyTimeOutResponse
                {
                    Success = false,
                    Message = $"An error occurred while marking Time Out: {ex.Message}"
                });
            }
        }

        [HttpGet("today/{teacherId}")]
        public async Task<ActionResult<List<DailyAttendanceRecord>>> GetTodayAttendance(string teacherId)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Get teacher's class information directly from teacher table
                var teacherQuery = @"
                    SELECT SchoolId, Gradelvl as GradeLevel, Section 
                    FROM teacher 
                    WHERE TeacherId = @teacherId";
                
                string? schoolId = null;
                int gradeLevel = 0;
                string? section = null;
                
                using (var teacherCommand = new MySqlCommand(teacherQuery, connection))
                {
                    teacherCommand.Parameters.AddWithValue("@teacherId", teacherId);
                    using var teacherReader = await teacherCommand.ExecuteReaderAsync();
                    if (await teacherReader.ReadAsync())
                    {
                        schoolId = teacherReader.IsDBNull("SchoolId") ? null : teacherReader.GetString("SchoolId");
                        gradeLevel = teacherReader.IsDBNull("GradeLevel") ? 0 : teacherReader.GetInt32("GradeLevel");
                        section = teacherReader.IsDBNull("Section") ? null : teacherReader.GetString("Section");
                    }
                }
                
                if (string.IsNullOrEmpty(schoolId) || string.IsNullOrEmpty(section))
                {
                    return Ok(new List<DailyAttendanceRecord>());
                }

                var query = @"
                    SELECT da.StudentId, s.FullName, da.Date, da.TimeIn, da.TimeOut, da.Status, da.Remarks
                    FROM daily_attendance da
                    INNER JOIN student s ON da.StudentId = s.StudentId
                    WHERE da.Date = @Date 
                    ORDER BY da.Date DESC, da.TimeIn DESC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Date", DateTime.Today);

                using var reader = await command.ExecuteReaderAsync();
                var records = new List<DailyAttendanceRecord>();

                Console.WriteLine($"DEBUG: Query executed with parameters - Date: {DateTime.Today:yyyy-MM-dd}, SchoolId: {schoolId}, GradeLevel: {gradeLevel}, Section: {section}");

                while (await reader.ReadAsync())
                {
                    var studentId = reader.GetString(0);
                    var studentName = reader.GetString(1);
                    var attendanceDate = reader.GetDateTime(2);
                    var timeIn = reader.IsDBNull(3) ? "" : reader.GetValue(3).ToString();
                    var timeOut = reader.IsDBNull(4) ? "" : reader.GetValue(4).ToString();
                    var status = reader.GetString(5);
                    var remarks = reader.IsDBNull(6) ? "" : reader.GetString(6);

                    Console.WriteLine($"DEBUG: Found record - StudentId: {studentId}, StudentName: {studentName}, TimeIn: {timeIn}, TimeOut: {timeOut}, Status: {status}, Remarks: {remarks}");

                    records.Add(new DailyAttendanceRecord
                    {
                        StudentId = studentId,
                        StudentName = studentName,
                        Date = attendanceDate,
                        TimeIn = timeIn,
                        TimeOut = timeOut,
                        Status = status,
                        Remarks = remarks
                    });
                }

                Console.WriteLine($"DEBUG: Returning {records.Count} attendance records");
                return Ok(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting today's attendance for teacher: {TeacherId}", teacherId);
                return StatusCode(500, new List<DailyAttendanceRecord>());
            }
        }

        [HttpGet("daily-history/{studentId}")]
        public async Task<ActionResult<List<DailyAttendanceRecord>>> GetDailyHistory(string studentId, [FromQuery] int days = 30)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var query = @"
                    SELECT Date, TimeIn, TimeOut, Status, Remarks 
                    FROM daily_attendance 
                    WHERE StudentId = @StudentId 
                    AND Date >= @StartDate
                    ORDER BY Date DESC 
                    LIMIT @Days";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@StartDate", DateTime.Today.AddDays(-days));
                command.Parameters.AddWithValue("@Days", days);

                using var reader = await command.ExecuteReaderAsync();
                var records = new List<DailyAttendanceRecord>();

                while (await reader.ReadAsync())
                {
                    records.Add(new DailyAttendanceRecord
                    {
                        Date = reader.GetDateTime("Date"),
                        TimeIn = reader.IsDBNull("TimeIn") ? "" : reader.GetString("TimeIn"),
                        TimeOut = reader.IsDBNull("TimeOut") ? "" : ((TimeSpan)reader.GetValue("TimeOut")).ToString(@"hh\:mm\:ss"),
                        Status = reader.GetString("Status"),
                        Remarks = reader.IsDBNull("Remarks") ? "" : reader.GetString("Remarks")
                    });
                }

                return Ok(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily history for student: {StudentId}", studentId);
                return StatusCode(500, new List<DailyAttendanceRecord>());
            }
        }

        [HttpPost("sync-offline-data")]
        public async Task<ActionResult<SyncOfflineDataResponse>> SyncOfflineData([FromBody] SyncOfflineDataRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new SyncOfflineDataResponse
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                _logger.LogInformation("Syncing offline data for teacher: {TeacherId}, Records count: {Count}", 
                    request.TeacherId, request.AttendanceRecords.Count);
                
                // Log each record being synced
                foreach (var record in request.AttendanceRecords)
                {
                    _logger.LogInformation("Syncing record - StudentId: {StudentId}, Date: {Date}, TimeIn: {TimeIn}, TimeOut: {TimeOut}, Status: {Status}, Remarks: {Remarks}", 
                        record.StudentId, record.Date, record.TimeIn, record.TimeOut, record.Status, record.Remarks);
                }

                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var syncedCount = 0;
                var errors = new List<string>();

                foreach (var record in request.AttendanceRecords)
                {
                    try
                    {
                        // Check if record already exists
                        var checkQuery = "SELECT COUNT(*) FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date";
                        using var checkCommand = new MySqlCommand(checkQuery, connection);
                        checkCommand.Parameters.AddWithValue("@StudentId", record.StudentId);
                        checkCommand.Parameters.AddWithValue("@Date", record.Date.Date);

                        var existingCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                        if (existingCount > 0)
                        {
                            // Check if we're trying to update with duplicate TimeIn or TimeOut
                            var checkExistingQuery = "SELECT TimeIn, TimeOut FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date";
                            using var checkExistingCommand = new MySqlCommand(checkExistingQuery, connection);
                            checkExistingCommand.Parameters.AddWithValue("@StudentId", record.StudentId);
                            checkExistingCommand.Parameters.AddWithValue("@Date", record.Date.Date);
                            
                            using var reader = await checkExistingCommand.ExecuteReaderAsync();
                            if (await reader.ReadAsync())
                            {
                                var existingTimeIn = reader.IsDBNull("TimeIn") ? null : reader.GetString("TimeIn");
                                var existingTimeOut = reader.IsDBNull("TimeOut") ? null : ((TimeSpan)reader.GetValue("TimeOut")).ToString(@"hh\:mm\:ss");
                                reader.Close();
                                
                                // Update the record with new values, but don't overwrite existing values with empty ones
                                bool shouldUpdate = true;
                                
                                // Only skip if we're trying to overwrite existing values with the same values
                                if (!string.IsNullOrEmpty(record.TimeIn) && !string.IsNullOrEmpty(existingTimeIn) && record.TimeIn == existingTimeIn)
                                {
                                    _logger.LogInformation("TimeIn already exists with same value for student {StudentId} on {Date}. Skipping duplicate TimeIn.", record.StudentId, record.Date);
                                    shouldUpdate = false;
                                }
                                
                                if (!string.IsNullOrEmpty(record.TimeOut) && !string.IsNullOrEmpty(existingTimeOut) && record.TimeOut == existingTimeOut)
                                {
                                    _logger.LogInformation("TimeOut already exists with same value for student {StudentId} on {Date}. Skipping duplicate TimeOut.", record.StudentId, record.Date);
                                    shouldUpdate = false;
                                }
                                
                                if (shouldUpdate)
                                {
                                    // Determine the correct remarks based on TimeIn and TimeOut availability
                                    string finalRemarks = "";
                                    string finalStatus = "Present";
                                    
                                    // Check what we'll have after the update
                                    string finalTimeIn = !string.IsNullOrEmpty(record.TimeIn) ? record.TimeIn : existingTimeIn;
                                    string finalTimeOut = !string.IsNullOrEmpty(record.TimeOut) ? record.TimeOut : existingTimeOut;
                                    
                                    if (!string.IsNullOrEmpty(finalTimeIn) && !string.IsNullOrEmpty(finalTimeOut))
                                    {
                                        // Both TimeIn and TimeOut exist - check if it's whole day or half day
                                        var timeInTime = TimeSpan.Parse(finalTimeIn);
                                        var timeOutTime = TimeSpan.Parse(finalTimeOut);
                                        
                                        // Check if it's a whole day (7:30 AM - 4:30 PM range)
                                        if (timeInTime.Hours <= 7 && timeOutTime.Hours >= 16)
                                        {
                                            finalRemarks = "Whole Day";
                                        }
                                        else
                                        {
                                            finalRemarks = "Half Day";
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(finalTimeIn))
                                    {
                                        // Only TimeIn exists
                                        finalRemarks = "Half Day";
                                    }
                                    else if (!string.IsNullOrEmpty(finalTimeOut))
                                    {
                                        // Only TimeOut exists
                                        finalRemarks = "Half Day";
                                    }
                                    
                                    // Update existing record - only update fields that have values
                                    var updateQuery = @"
                                        UPDATE daily_attendance 
                                        SET TimeIn = CASE WHEN @TimeIn IS NOT NULL AND @TimeIn != '' THEN @TimeIn ELSE TimeIn END,
                                            TimeOut = CASE WHEN @TimeOut IS NOT NULL AND @TimeOut != '' THEN @TimeOut ELSE TimeOut END,
                                            Status = @Status,
                                            Remarks = @Remarks,
                                            UpdatedAt = @UpdatedAt
                                        WHERE StudentId = @StudentId AND Date = @Date";

                                    using var updateCommand = new MySqlCommand(updateQuery, connection);
                                    updateCommand.Parameters.AddWithValue("@StudentId", record.StudentId);
                                    updateCommand.Parameters.AddWithValue("@Date", record.Date.Date);
                                    updateCommand.Parameters.AddWithValue("@TimeIn", string.IsNullOrEmpty(record.TimeIn) ? (object)DBNull.Value : record.TimeIn);
                                    updateCommand.Parameters.AddWithValue("@TimeOut", string.IsNullOrEmpty(record.TimeOut) ? (object)DBNull.Value : record.TimeOut);
                                    updateCommand.Parameters.AddWithValue("@Status", finalStatus);
                                    updateCommand.Parameters.AddWithValue("@Remarks", finalRemarks);
                                    updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                                    await updateCommand.ExecuteNonQueryAsync();
                                    
                                    _logger.LogInformation("Updated record for student {StudentId} with remarks: {Remarks}", 
                                        record.StudentId, finalRemarks);
                                }
                            }
                            else
                            {
                                reader.Close();
                            }
                        }
                        else
                        {
                            // Determine remarks for new record
                            string newRemarks = "";
                            string newStatus = "Present";
                            
                            if (!string.IsNullOrEmpty(record.TimeIn) && !string.IsNullOrEmpty(record.TimeOut))
                            {
                                // Both TimeIn and TimeOut exist - check if it's whole day or half day
                                var timeInTime = TimeSpan.Parse(record.TimeIn);
                                var timeOutTime = TimeSpan.Parse(record.TimeOut);
                                
                                // Check if it's a whole day (7:30 AM - 4:30 PM range)
                                if (timeInTime.Hours <= 7 && timeOutTime.Hours >= 16)
                                {
                                    newRemarks = "Whole Day";
                                }
                                else
                                {
                                    newRemarks = "Half Day";
                                }
                            }
                            else if (!string.IsNullOrEmpty(record.TimeIn))
                            {
                                // Only TimeIn exists
                                newRemarks = "Half Day";
                            }
                            else if (!string.IsNullOrEmpty(record.TimeOut))
                            {
                                // Only TimeOut exists
                                newRemarks = "Half Day";
                            }
                            
                            // Insert new record
                            var insertQuery = @"
                                INSERT INTO daily_attendance (AttendanceId, StudentId, Date, TimeIn, TimeOut, Status, Remarks, CreatedAt, UpdatedAt)
                                VALUES (@AttendanceId, @StudentId, @Date, @TimeIn, @TimeOut, @Status, @Remarks, @CreatedAt, @UpdatedAt)";

                            using var insertCommand = new MySqlCommand(insertQuery, connection);
                            insertCommand.Parameters.AddWithValue("@AttendanceId", Guid.NewGuid().ToString());
                            insertCommand.Parameters.AddWithValue("@StudentId", record.StudentId);
                            insertCommand.Parameters.AddWithValue("@Date", record.Date.Date);
                            insertCommand.Parameters.AddWithValue("@TimeIn", string.IsNullOrEmpty(record.TimeIn) ? (object)DBNull.Value : record.TimeIn);
                            insertCommand.Parameters.AddWithValue("@TimeOut", string.IsNullOrEmpty(record.TimeOut) ? (object)DBNull.Value : record.TimeOut);
                            insertCommand.Parameters.AddWithValue("@Status", newStatus);
                            insertCommand.Parameters.AddWithValue("@Remarks", newRemarks);
                            insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                            insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                            await insertCommand.ExecuteNonQueryAsync();
                            
                            _logger.LogInformation("Created new record for student {StudentId} with remarks: {Remarks}", 
                                record.StudentId, newRemarks);
                        }

                        syncedCount++;
                        _logger.LogInformation("Synced offline record for student: {StudentId}, Date: {Date}", 
                            record.StudentId, record.Date);

                        // Send SMS to parent for each newly synced record (fire-and-forget)
                        try
                        {
                            // Fetch both student name and parent number in one query
                            string smsStudentName = "Student";
                            string? smsParentNum = null;
                            var smsSql = "SELECT FullName, ParentsNumber FROM student WHERE StudentId = @StudentId LIMIT 1";
                            using var smsCmd = new MySqlCommand(smsSql, connection);
                            smsCmd.Parameters.AddWithValue("@StudentId", record.StudentId);
                            using var smsReader = await smsCmd.ExecuteReaderAsync();
                            if (await smsReader.ReadAsync())
                            {
                                smsStudentName = smsReader.IsDBNull(0) ? "Student" : smsReader.GetString(0);
                                smsParentNum = smsReader.IsDBNull(1) ? null : smsReader.GetString(1);
                            }
                            smsReader.Close();

                            if (!string.IsNullOrEmpty(smsParentNum))
                            {
                                if (!string.IsNullOrEmpty(record.TimeIn))
                                    SendParentSmsFireAndForget(smsParentNum, smsStudentName, "TimeIn",
                                        record.Date.Date.Add(TimeSpan.Parse(record.TimeIn)));
                                if (!string.IsNullOrEmpty(record.TimeOut))
                                    SendParentSmsFireAndForget(smsParentNum, smsStudentName, "TimeOut",
                                        record.Date.Date.Add(TimeSpan.Parse(record.TimeOut)));
                            }
                        }
                        catch (Exception smEx) { _logger.LogWarning("[SMS] Sync SMS failed for {StudentId}: {Error}", record.StudentId, smEx.Message); }
                    }
                    catch (Exception ex)
                    {
                        var error = $"Error syncing record for student {record.StudentId}: {ex.Message}";
                        errors.Add(error);
                        _logger.LogError(ex, "Error syncing offline record for student: {StudentId}", record.StudentId);
                    }
                }

                _logger.LogInformation("Offline sync completed. Synced: {SyncedCount}, Errors: {ErrorCount}", 
                    syncedCount, errors.Count);

                return Ok(new SyncOfflineDataResponse
                {
                    Success = true,
                    Message = $"Successfully synced {syncedCount} records",
                    SyncedCount = syncedCount,
                    ErrorCount = errors.Count,
                    Errors = errors
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing offline data for teacher: {TeacherId}", request.TeacherId);
                return StatusCode(500, new SyncOfflineDataResponse
                {
                    Success = false,
                    Message = "An error occurred while syncing offline data"
                });
            }
        }

        [HttpPost("mark-absent")]
        public async Task<IActionResult> MarkStudentAbsent([FromBody] MarkAbsentRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Check if record already exists for today
                var checkQuery = "SELECT AttendanceId FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                checkCommand.Parameters.AddWithValue("@Date", request.Date.Date);

                var existingId = await checkCommand.ExecuteScalarAsync();

                if (existingId != null)
                {
                    // Update existing record
                    var updateQuery = @"
                        UPDATE daily_attendance 
                        SET Status = @Status, Remarks = @Remarks, UpdatedAt = @UpdatedAt
                        WHERE StudentId = @StudentId AND Date = @Date";

                    using var updateCommand = new MySqlCommand(updateQuery, connection);
                    updateCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                    updateCommand.Parameters.AddWithValue("@Date", request.Date.Date);
                    updateCommand.Parameters.AddWithValue("@Status", request.Status);
                    updateCommand.Parameters.AddWithValue("@Remarks", request.Remarks);
                    updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

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
                    insertCommand.Parameters.AddWithValue("@Date", request.Date.Date);
                    insertCommand.Parameters.AddWithValue("@TimeIn", DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@TimeOut", DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@Status", request.Status);
                    insertCommand.Parameters.AddWithValue("@Remarks", request.Remarks);
                    insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                    insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                    await insertCommand.ExecuteNonQueryAsync();
                }

                return Ok("Student marked as absent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking student as absent: {StudentId}", request.StudentId);
                return StatusCode(500, "Error marking student as absent");
            }
        }

        [HttpPost("cancel-absent")]
        public async Task<IActionResult> CancelAbsent([FromBody] CancelAbsentRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                // Delete the absent record for today
                var deleteQuery = "DELETE FROM daily_attendance WHERE StudentId = @StudentId AND Date = @Date AND Status = 'Absent'";
                using var deleteCommand = new MySqlCommand(deleteQuery, connection);
                deleteCommand.Parameters.AddWithValue("@StudentId", request.StudentId);
                deleteCommand.Parameters.AddWithValue("@Date", request.Date.Date);

                var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok("Absent status cancelled successfully");
                }
                else
                {
                    return NotFound("No absent record found to cancel");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling absent status: {StudentId}", request.StudentId);
                return StatusCode(500, "Error cancelling absent status");
            }
        }

    }

    public class CancelAbsentRequest
    {
        public string StudentId { get; set; } = "";
        public DateTime Date { get; set; }
    }

    public class MarkAbsentRequest
    {
        public string StudentId { get; set; } = "";
        public DateTime Date { get; set; }
        public string Status { get; set; } = "";
        public string Remarks { get; set; } = "";
    }

}
