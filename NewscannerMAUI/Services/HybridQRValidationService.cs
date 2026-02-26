using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AttrackSharedClass.Models;

namespace NewscannerMAUI.Services
{
    public class HybridQRValidationService
    {
        private readonly HttpClient _httpClient;
        private readonly OfflineDataService _offlineDataService;
        private readonly AuthService _authService;
        private readonly ConnectionStatusService _connectionStatusService;
        private readonly string _serverBaseUrl;

        public HybridQRValidationService(
            HttpClient httpClient, 
            OfflineDataService offlineDataService,
            AuthService authService,
            ConnectionStatusService connectionStatusService)
        {
            _httpClient = httpClient;
            _offlineDataService = offlineDataService;
            _authService = authService;
            _connectionStatusService = connectionStatusService;
            _serverBaseUrl = "https://attrack-sr9l.onrender.com";
        }

        public async Task<string> GetCurrentTeacherIdAsync()
        {
            var teacher = await _authService.GetCurrentTeacherAsync();
            return teacher?.TeacherId ?? "";
        }

        public async Task<string> GetOfflineStudentNameAsync(string studentId)
        {
            return await _offlineDataService.GetStudentNameForDisplayAsync(studentId);
        }

        public async Task<QRValidationResult> ValidateQRCodeAsync(string qrCode, string? attendanceType = null, string? teacherId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== VALIDATING QR CODE (Hybrid) for Teacher: {teacherId ?? "Unknown"} ===");
                System.Diagnostics.Debug.WriteLine($"=== Validating QR Code: {qrCode} ({attendanceType}) ===");
                
                // 1. Basic Format Validation
                // Expected format: "STUDENT:[ID]" or just "[ID]" depending on your QR generation
                string studentId = qrCode;
                if (qrCode.StartsWith("STUDENT:"))
                {
                    studentId = qrCode.Substring(8);
                }
                
                if (string.IsNullOrEmpty(studentId))
                {
                    return new QRValidationResult 
                    { 
                        IsValid = false, 
                        Message = "Invalid QR code format",
                        ErrorType = QRValidationErrorType.InvalidFormat
                    };
                }

                // Get current teacher info - OPTIMIZED: Skip if teacherId is already provided
                string resolvedTeacherId = teacherId;
                if (string.IsNullOrEmpty(resolvedTeacherId))
                {
                    var currentTeacher = await _authService.GetCurrentTeacherAsync();
                    resolvedTeacherId = currentTeacher?.TeacherId ?? "Unknown";
                }
                
                // 2. Online Validation (Preferred)
                // Use ConnectionStatusService to check if we're online
                bool isOnline = _connectionStatusService.IsOnline;
                
                // Double check actual connectivity if service says online, to be sure
                if (isOnline)
                {
                    // Quick ping to ensure we really can reach the server
                    // This prevents "hanging" on the API call if connection is flaky
                    try
                    {
                        // We can skip this if ConnectionStatusService just checked, but good for safety
                        // For now, trust the service to avoid extra latency
                    }
                    catch
                    {
                        isOnline = false;
                    }
                }

                if (isOnline)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Online mode: Validating with server...");
                        
                        // Current timestamp
                        var timestamp = DateTime.Now;
                        
                        // OPTIMIZATION: Skip separate QR validation - attendance endpoint validates automatically
                        // This reduces validation time by ~50% (one HTTP call instead of two)
                        System.Diagnostics.Debug.WriteLine("=== OPTIMIZED: Direct attendance save (validation included) ===");
                        var currentTime = DateTime.Now;
                        
                        HttpResponseMessage response;
                        
                        // STRICT MODE: always use the explicitly chosen attendanceType if provided
                        // If "Auto", determine based on current state
                        string resolvedAttendanceType = attendanceType ?? "Auto"; // Default to Auto if null
                        if (resolvedAttendanceType == "Auto")
                        {
                            resolvedAttendanceType = await DetermineAttendanceTypeAsync(studentId, resolvedTeacherId);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Saving attendance as: {resolvedAttendanceType} for student {studentId}");
                        
                        // PRE-CHECK: Check local status even in online mode to prevent redundant success messages for duplicates
                        var localStatus = await CheckOfflineAttendanceStatusAsync(studentId, resolvedAttendanceType, resolvedTeacherId);
                        if (resolvedAttendanceType == "TimeIn" && localStatus.HasTimeIn)
                        {
                            var name = await _offlineDataService.GetStudentNameForDisplayAsync(studentId);
                            return new QRValidationResult
                            {
                                IsValid = false,
                                Message = $"{name} - Already Timed In at {localStatus.TimeIn}",
                                ErrorType = QRValidationErrorType.ValidationError
                            };
                        }
                        if (resolvedAttendanceType == "TimeOut" && localStatus.HasTimeOut)
                        {
                            var name = await _offlineDataService.GetStudentNameForDisplayAsync(studentId);
                            return new QRValidationResult
                            {
                                IsValid = false,
                                Message = $"{name} - Already Timed Out at {localStatus.TimeOut}",
                                ErrorType = QRValidationErrorType.ValidationError
                            };
                        }

                        // Determine Status based on Time (Business Rules)
                        string status = "Present";
                        if (resolvedAttendanceType == "TimeIn")
                        {
                            var nowTime = currentTime.TimeOfDay;
                            // Morning: After 7:00 AM is late
                            if (nowTime > new TimeSpan(7, 0, 0) && nowTime < new TimeSpan(11, 0, 0))
                            {
                                status = "Late";
                            }
                            // Break: 11:00 AM - 1:04 PM is NOT late
                            else if (nowTime >= new TimeSpan(11, 0, 0) && nowTime <= new TimeSpan(13, 4, 59))
                            {
                                status = "Present";
                            }
                            // Afternoon: 1:05 PM or later is late
                            else if (nowTime >= new TimeSpan(13, 5, 0))
                            {
                                status = "Late";
                            }
                        }

                        if (resolvedAttendanceType == "TimeIn")
                        {
                            var request = new DailyTimeInRequest
                            {
                                StudentId = studentId,
                                Date = currentTime.Date,
                                TimeIn = currentTime.TimeOfDay,
                                TeacherId = resolvedTeacherId
                            };
                            
                            response = await _httpClient.PostAsJsonAsync($"{_serverBaseUrl}/api/dailyattendance/daily-timein", request);
                        }
                        else
                        {
                            var request = new DailyTimeOutRequest
                            {
                                StudentId = studentId,
                                Date = currentTime.Date,
                                TimeOut = currentTime.TimeOfDay,
                                TeacherId = resolvedTeacherId
                            };
                            
                            response = await _httpClient.PostAsJsonAsync($"{_serverBaseUrl}/api/dailyattendance/daily-timeout", request);
                        }
                        
                        // Update attendanceType for messages/logging below
                        attendanceType = resolvedAttendanceType;
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var responseData = await response.Content.ReadFromJsonAsync<dynamic>();
                            System.Diagnostics.Debug.WriteLine($"Online save successful: {responseData}");
                            
                            // Save backup to local SQLite for immediate display in dashboard
                            // NOTE: Removing the first save here as it's duplicated below

                            
                            // Get student name for display - ID hidden as per user request
                            string studentName = "Student";
                            try 
                            { 
                                // Try to extract name from response if available
                                if (responseData != null)
                                {
                                    // Check common properties for student name
                                    if (responseData.studentName != null) studentName = responseData.studentName.ToString();
                                    else if (responseData.fullName != null) studentName = responseData.fullName.ToString();
                                    else if (responseData.firstName != null && responseData.lastName != null) 
                                        studentName = $"{responseData.firstName} {responseData.lastName}";
                                }

                                // If still default, try to get from cache
                                if (studentName == "Student")
                                {
                                    var cachedName = await _offlineDataService.GetStudentNameForDisplayAsync(studentId);
                                    if (!string.IsNullOrEmpty(cachedName) && cachedName != "Student") 
                                        studentName = cachedName;
                                }

                                // Cache the name for future offline use
                                await _offlineDataService.CacheStudentNameAsync(studentId, studentName);
                            } 
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing student name: {ex.Message}");
                            }

                            // BACKUP: Save to SQLite as "Synced" record for history/backup
                            await _offlineDataService.SaveOfflineAttendanceAsync(
                                studentId, 
                                attendanceType, 
                                System.Environment.MachineName, 
                                DateTime.Now, 
                                isSynced: true, // Mark as already synced
                                studentName: studentName,
                                teacherId: resolvedTeacherId
                            );

                            return new QRValidationResult
                            {
                                IsValid = true,
                                Message = $"{studentName} - {attendanceType} Successful",
                                ErrorType = QRValidationErrorType.None,
                                StudentData = new StudentQRData 
                                { 
                                    StudentId = studentId,
                                    FullName = studentName
                                }
                            };
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"Server error: {response.StatusCode} - {errorContent}");
                            
                            // Try to extract a clean message from the JSON response if possible
                            try
                            {
                                var errorData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(errorContent);
                                if (errorData != null && errorData.ContainsKey("message"))
                                {
                                    string msg = errorData["message"].ToString();
                                    // Map specific server messages to user-requested phrased if they match
                                    if (msg.Contains("Section mismatch")) return new QRValidationResult { IsValid = false, Message = "This student is from other section", ErrorType = QRValidationErrorType.ValidationError };
                                    
                                    return new QRValidationResult { IsValid = false, Message = msg, ErrorType = QRValidationErrorType.ValidationError };
                                }
                            }
                            catch { /* Ignore parsing errors and fallback to predefined checks */ }

                            // Check for specific error messages
                            if (errorContent.Contains("already recorded") || errorContent.Contains("already timed in") || errorContent.Contains("already timed out"))
                            {
                                return new QRValidationResult
                                {
                                    IsValid = false,
                                    Message = "Already recorded for today",
                                    ErrorType = QRValidationErrorType.ValidationError
                                };
                            }
                            
                            if (errorContent.Contains("No Time In record found"))
                            {
                                return new QRValidationResult
                                {
                                    IsValid = false,
                                    Message = "Cannot Time Out: No Time In record found for today",
                                    ErrorType = QRValidationErrorType.ValidationError
                                };
                            }
                            
                            // Check for student-teacher assignment mismatch
                            if (errorContent.Contains("Grade level mismatch"))
                            {
                                return new QRValidationResult { IsValid = false, Message = "Validation Failed: Grade Level Mismatch", ErrorType = QRValidationErrorType.ValidationError };
                            }

                            if (errorContent.Contains("Section mismatch"))
                            {
                                return new QRValidationResult { IsValid = false, Message = "This student is from other section", ErrorType = QRValidationErrorType.ValidationError };
                            }

                            if (errorContent.Contains("not assigned") || errorContent.Contains("not in your class"))
                            {
                                return new QRValidationResult
                                {
                                    IsValid = false,
                                    Message = "This student is not in your assigned class list",
                                    ErrorType = QRValidationErrorType.ValidationError
                                };
                            }
                            
                            // If it's a 500 or connection error, fall back to offline
                            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 408)
                            {
                                System.Diagnostics.Debug.WriteLine("Server error (5xx), falling back to offline mode...");
                                isOnline = false; // Trigger fallback
                            }
                            else
                            {
                                // Client error (4xx) - likely actual validation error
                                return new QRValidationResult
                                {
                                    IsValid = false,
                                    Message = $"Server error: {errorContent}",
                                    ErrorType = QRValidationErrorType.ValidationError
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Online validation exception: {ex.Message}");
                        // Network error occurred calling API, fall back to offline
                        isOnline = false; 
                    }
                }

                // 3. Offline Validation (Fallback or Default)
                if (!isOnline)
                {
                    return await ValidateOfflineAsync(studentId, attendanceType ?? "Auto", resolvedTeacherId);
                }

                return new QRValidationResult
                {
                    IsValid = false,
                    Message = "Unknown error occurred",
                    ErrorType = QRValidationErrorType.ValidationError
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Validation Critical Error: {ex.Message}");
                return new QRValidationResult
                {
                    IsValid = false,
                    Message = $"System error: {ex.Message}",
                    ErrorType = QRValidationErrorType.ValidationError
                };
            }
        }
        
        private async Task<QRValidationResult> ValidateOfflineAsync(string studentId, string attendanceType, string? teacherId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"--- VALIDATING OFFLINE for Teacher: {teacherId ?? "Unknown"} ---");
                System.Diagnostics.Debug.WriteLine($"Offline mode: Validating locally for {studentId}...");

                // Handle "Auto" mode in offline
                if (attendanceType == "Auto")
                {
                    var status = await CheckOfflineAttendanceStatusAsync(studentId, "Auto", teacherId);
                    attendanceType = status.HasTimeIn ? "TimeOut" : "TimeIn";
                    System.Diagnostics.Debug.WriteLine($"Offline Auto resolved to: {attendanceType}");
                }
                
                // 1. Lax Validation for Offline Mode (Allow anyone, validate during sync)
                var studentProfile = await _offlineDataService.GetStudentProfileAsync(studentId);
                var studentName = studentProfile?.FullName;
                
                // If profile is missing, it's an "Unknown" student. Generate a "Student X" name.
                if (string.IsNullOrEmpty(studentName))
                {
                    studentName = await _offlineDataService.GetStudentNameForDisplayAsync(studentId);
                }

                // Removed strict teacher-student profile matching here. 
                // It will now be performed during the Sync process.
                System.Diagnostics.Debug.WriteLine($"OFFLINE VALIDATION (LAX): Allowing scan for {studentName} ({studentId})");

                // Check if already scanned today offline
                var offlineStatus = await CheckOfflineAttendanceStatusAsync(studentId, attendanceType, teacherId);
                
                // NEW RULE: If it's an "Unknown" student (Student X), they can only scan ONCE per day offline.
                // We don't allow TimeOuts for unknown students offline to prevent duplicates in the pending list.
                bool isUnknownStudent = studentProfile == null;
                
                if (isUnknownStudent && (offlineStatus.HasTimeIn || offlineStatus.HasTimeOut))
                {
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = $"{studentName} has already been scanned!",
                        ErrorType = QRValidationErrorType.ValidationError
                    };
                }
                
                // Standard duplicate checks for known students
                if (!isUnknownStudent && attendanceType == "TimeIn" && offlineStatus.HasTimeIn)
                {
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = $"{studentName} - Already Timed In at {offlineStatus.TimeIn}",
                        ErrorType = QRValidationErrorType.ValidationError
                    };
                }
                
                if (!isUnknownStudent && attendanceType == "TimeOut" && offlineStatus.HasTimeOut)
                {
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = $"{studentName} - Already Timed Out at {offlineStatus.TimeOut}",
                        ErrorType = QRValidationErrorType.ValidationError
                    };
                }
                
                if (attendanceType == "TimeOut" && !offlineStatus.HasTimeIn)
                {
                    // In offline mode, we might allow TimeOut without TimeIn if the TimeIn was synced
                    // But if we strictly enforce flow:
                    // For now, allow it and let server reconcile, or check local specific logic
                    // A lax approach is better for offline to avoid blocking users
                }
                
                // Save to local SQLite
                var saved = await _offlineDataService.SaveOfflineAttendanceAsync(
                    studentId, 
                    attendanceType, 
                    System.Environment.MachineName,
                    DateTime.Now,
                    isSynced: false,
                    studentName: studentName,
                    teacherId: teacherId ?? "Offline"
                );
                
                if (saved)
                {
                    return new QRValidationResult
                    {
                        IsValid = true,
                        Message = $"{studentName} - {attendanceType} Successful",
                        ErrorType = QRValidationErrorType.None,
                        StudentData = new StudentQRData 
                        { 
                            StudentId = studentId,
                            FullName = studentName
                        }
                    };
                }
                else
                {
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = "Failed to save offline record",
                        ErrorType = QRValidationErrorType.ValidationError
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in offline validation: {ex.Message}");
                return new QRValidationResult
                {
                    IsValid = false,
                    Message = $"Offline save error: {ex.Message}",
                    ErrorType = QRValidationErrorType.ValidationError
                };
            }
        }
        
        public async Task<OfflineAttendanceStatus> CheckOfflineAttendanceStatusAsync(string studentId, string attendanceType, string? teacherId = null)
        {
            var status = new OfflineAttendanceStatus { HasTimeIn = false, HasTimeOut = false, TimeIn = "", TimeOut = "" };
            
            try
            {
                var today = DateTime.Today;
                
                // Get existing records from offline database (including synced ones for today)
                // This ensures we can prevent duplicates even after a sync
                var dailyRecords = await _offlineDataService.GetAllDailyAttendanceForDateAsync(today, teacherId);
                
                // Check for TimeIn record
                var timeInRecord = dailyRecords
                    .FirstOrDefault(r => r.StudentId == studentId && 
                                       r.AttendanceType == "TimeIn");
                if (timeInRecord != null)
                {
                    status.HasTimeIn = true;
                    status.TimeIn = timeInRecord.TimeIn ?? "";
                }
                
                // Check for TimeOut record
                var timeOutRecord = dailyRecords
                    .FirstOrDefault(r => r.StudentId == studentId && 
                                       r.AttendanceType == "TimeOut");
                if (timeOutRecord != null)
                {
                    status.HasTimeOut = true;
                    status.TimeOut = timeOutRecord.TimeOut ?? "";
                }
                
                System.Diagnostics.Debug.WriteLine($"Offline attendance status for {studentId}: TimeIn={status.HasTimeIn} ({status.TimeIn}), TimeOut={status.HasTimeOut} ({status.TimeOut})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking offline attendance status: {ex.Message}");
            }
            
            return status;
        }
        
        public class OfflineAttendanceStatus
        {
            public bool HasTimeIn { get; set; }
            public bool HasTimeOut { get; set; }
            public string TimeIn { get; set; } = "";
            public string TimeOut { get; set; } = "";
        }

        private async Task<string> DetermineAttendanceTypeAsync(string studentId, string teacherId)
        {
            try
            {
                // Check if student has Time In for today
                var today = DateTime.Today;
                var statusUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/dailyattendance/daily-status/{studentId}?date={today:yyyy-MM-dd}" : $"{_serverBaseUrl}/api/dailyattendance/daily-status/{studentId}?date={today:yyyy-MM-dd}";
                var timeInResponse = await _httpClient.GetFromJsonAsync<DailyAttendanceStatus>(statusUrl);
                var hasTimeIn = timeInResponse?.TimeIn != null;

                // Check if student has Time Out for today
                var todayUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/dailyattendance/today/{teacherId}" : $"{_serverBaseUrl}/api/dailyattendance/today/{teacherId}";
                var todayResponse = await _httpClient.GetFromJsonAsync<List<DailyAttendanceRecord>>(todayUrl);
                var hasTimeOut = todayResponse?.Any(r => r.StudentId == studentId && !string.IsNullOrEmpty(r.TimeOut)) == true;

                System.Diagnostics.Debug.WriteLine($"Student {studentId} - HasTimeIn: {hasTimeIn}, HasTimeOut: {hasTimeOut}");

                // Auto-determine attendance type
                if (!hasTimeIn)
                {
                    return "TimeIn";
                }
                else if (!hasTimeOut)
                {
                    return "TimeOut";
                }
                else
                {
                    // Both Time In and Time Out already exist - this shouldn't happen in normal flow
                    return "TimeIn"; // Default fallback
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error determining attendance type: {ex.Message}");
                // Default to TimeIn if we can't determine
                return "TimeIn";
            }
        }
        // Sync offline data to server when online
        public async Task<SyncResult> SyncOfflineDataAsync(Action<int, int>? progressCallback = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Starting Detailed Offline Data Sync ===");

                var teacher = await _authService.GetCurrentTeacherAsync();
                if (teacher == null)
                {
                    return new SyncResult { Success = false, Message = "No teacher logged in" };
                }

                // Use the revamped one-by-one sync for progress reporting and detailed results
                var result = await _offlineDataService.AutoSyncOfflineDataAsync(_serverBaseUrl, teacher.TeacherId, progressCallback);
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in sync: {ex.Message}");
                return new SyncResult { Success = false, Message = ex.Message };
            }
        }

        private async Task<bool> SendAttendanceToServerAsync(OfflineAttendanceRecord record)
        {
            try
            {
                var request = new
                {
                    StudentId = record.StudentId,
                    AttendanceType = record.AttendanceType,
                    Timestamp = record.ScanTime,
                    DeviceId = record.DeviceId
                };

                var response = await _httpClient.PostAsJsonAsync($"{_serverBaseUrl}/api/attendance/record", request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ServerQRValidationResult
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [JsonPropertyName("errorType")]
        public ServerQRValidationErrorType ErrorType { get; set; } = ServerQRValidationErrorType.None;
        
        [JsonPropertyName("studentData")]
        public StudentQRData? StudentData { get; set; }
        
        [JsonPropertyName("qrStudentInfo")]
        public QRStudentInfo? QRStudentInfo { get; set; }
    }

    public enum ServerQRValidationErrorType
    {
        None,
        TeacherNotFound,
        StudentNotFound,
        InvalidFormat,
        SchoolMismatch,
        GradeMismatch,
        SectionMismatch,
        ValidationError
    }

    public class QRStudentInfo
    {
        [JsonPropertyName("studentId")]
        public string StudentId { get; set; } = string.Empty;
        
        [JsonPropertyName("fullName")]
        public string FullName { get; set; } = string.Empty;
        
        [JsonPropertyName("gradeLevel")]
        public int GradeLevel { get; set; }
        
        [JsonPropertyName("section")]
        public string Section { get; set; } = string.Empty;
        
        [JsonPropertyName("schoolId")]
        public string SchoolId { get; set; } = string.Empty;
        
        [JsonPropertyName("strand")]
        public string? Strand { get; set; }
    }

    public class QRValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public QRValidationErrorType ErrorType { get; set; }
        public StudentQRData? StudentData { get; set; }
        public string? StudentName { get; set; }
    }

    public enum QRValidationErrorType
    {
        None,
        NoTeacher,
        StudentNotFound,
        InvalidFormat,
        SchoolMismatch,
        GradeMismatch,
        SectionMismatch,
        ValidationError
    }

    public class StudentQRData
    {
        [JsonPropertyName("studentId")]
        public string StudentId { get; set; } = string.Empty;
        
        [JsonPropertyName("fullName")]
        public string FullName { get; set; } = string.Empty;
        
        [JsonPropertyName("gradeLevel")]
        public int GradeLevel { get; set; }
        
        [JsonPropertyName("section")]
        public string Section { get; set; } = string.Empty;
        
        [JsonPropertyName("schoolId")]
        public string SchoolId { get; set; } = string.Empty;
        
        [JsonPropertyName("strand")]
        public string? Strand { get; set; }
    }
}
