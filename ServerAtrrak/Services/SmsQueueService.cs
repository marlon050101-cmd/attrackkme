using MySql.Data.MySqlClient;
using ServerAtrrak.Data;
using Microsoft.AspNetCore.SignalR;
using ServerAtrrak.Hubs;

namespace ServerAtrrak.Services
{
    public class SmsQueueService
    {
        private readonly Dbconnection _dbConnection;
        private readonly ILogger<SmsQueueService> _logger;
        private readonly IHubContext<SmsQueueHub> _smsQueueHub;

        public SmsQueueService(
            Dbconnection dbConnection,
            ILogger<SmsQueueService> logger,
            IHubContext<SmsQueueHub> smsQueueHub)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _smsQueueHub = smsQueueHub;
        }

        public async Task QueueSmsAsync(string? parentNumber, string studentName, string attendanceType, DateTime time, string studentId = "Unknown", string? subjectName = null)
        {
            if (string.IsNullOrWhiteSpace(parentNumber)) return;

            // Normalize phone number if needed (GsmSmsService.BuildSmsMessage is static and does the formatting)
            var message = GsmSmsService.BuildSmsMessage(studentName, attendanceType, time, subjectName);

            try
            {
                using var connection = new MySqlConnection(_dbConnection.GetConnection());
                await connection.OpenAsync();

                var sql = @"INSERT INTO sms_queue (PhoneNumber, Message, StudentId, ScheduledAt, IsSent) 
                           VALUES (@Phone, @Msg, @Sid, @Date, 0)";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Phone", parentNumber);
                cmd.Parameters.AddWithValue("@Msg", message);
                cmd.Parameters.AddWithValue("@Sid", studentId);
                cmd.Parameters.AddWithValue("@Date", DateTime.Now);

                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("[SMS-QUEUE] Queued message for {Name} ({Type})", studentName, attendanceType);

                // Notify the Office PC dispatcher (SignalR) to fetch/send immediately
                await _smsQueueHub.Clients.Group(SmsQueueHub.GroupName)
                    .SendAsync("SmsQueueChanged", new { Reason = "Queued", StudentName = studentName, AttendanceType = attendanceType });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SMS-QUEUE] Failed to queue message for {Phone}", parentNumber);
            }
        }
    }
}
