using AttrackSharedClass.Models;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ScannerMaui.Services
{
    public class HybridQRValidationService
    {
        private readonly AuthService _authService;
        private readonly OfflineDataService _offlineDataService;
        private readonly HttpClient _httpClient;
        private readonly string _serverBaseUrl;

        public HybridQRValidationService(AuthService authService, OfflineDataService offlineDataService, HttpClient httpClient)
        {
            _authService = authService;
            _offlineDataService = offlineDataService;
            _httpClient = httpClient;

            // Set timeout for HTTP client (reduced to 5 seconds for faster response)
            _httpClient.Timeout = TimeSpan.FromSeconds(5);

            // Get server URL from configuration or use default
            _serverBaseUrl = "https://attrak-8gku.onrender.com/"; // Production server URL
        }

        public async Task<QRValidationResult> ValidateQRCodeAsync(string qrCodeData, string attendanceType = "TimeIn")
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Starting QR Code Validation ===");
                System.Diagnostics.Debug.WriteLine($"QR Code Data: '{qrCodeData}'");
                System.Diagnostics.Debug.WriteLine($"Attendance Type: '{attendanceType}'");

                var teacher = await _authService.GetCurrentTeacherAsync();
                if (teacher == null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: No teacher logged in");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = "No teacher logged in. Please login first.",
                        ErrorType = QRValidationErrorType.NoTeacher
                    };
                }

                System.Diagnostics.Debug.WriteLine($"Teacher found: {teacher.TeacherId}, School: {teacher.SchoolId}");

                var studentData = ParseQRCodeData(qrCodeData, teacher);
                if (studentData is null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Failed to parse QR code data");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = "Invalid QR code format. Please scan a valid student QR code.",
                        ErrorType = QRValidationErrorType.InvalidFormat
                    };
                }

                System.Diagnostics.Debug.WriteLine($"Student data parsed successfully: {studentData.StudentId}");

                // Always try online first, fallback to offline only if online fails
                System.Diagnostics.Debug.WriteLine("=== Attempting Online Mode First ===");

                try
                {
                    // Try online mode first
                    var onlineResult = await ValidateOnlineWithBackupAsync(studentData, teacher, attendanceType);

                    if (onlineResult.IsValid)
                    {
                        System.Diagnostics.Debug.WriteLine("Online mode successful");
                        return onlineResult;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Online mode failed: {onlineResult.Message}");

                        // Check if this is a server error that should fallback to offline
                        if (onlineResult.Message.Contains("Connection error") ||
                            onlineResult.Message.Contains("timeout") ||
                            onlineResult.Message.Contains("network"))
                        {
                            System.Diagnostics.Debug.WriteLine("=== Connection issue detected - Falling back to Offline Mode ===");
                            return await ValidateOfflineAsync(studentData, teacher, attendanceType);
                        }
                        else
                        {
                            // This is a business logic error (like "No Time In found"), don't fallback
                            System.Diagnostics.Debug.WriteLine("=== Business logic error - Not falling back to offline ===");
                            return onlineResult;
                        }
                    }
                }
                catch (Exception onlineEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Online mode error: {onlineEx.Message}");

                    // Check if this is a connection-related exception
                    if (onlineEx.Message.Contains("timeout") ||
                        onlineEx.Message.Contains("network") ||
                        onlineEx.Message.Contains("connection") ||
                        onlineEx is HttpRequestException ||
                        onlineEx is TaskCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("=== Connection exception detected - Falling back to Offline Mode ===");
                        return await ValidateOfflineAsync(studentData, teacher, attendanceType);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("=== Non-connection exception - Not falling back to offline ===");
                        return new QRValidationResult
                        {
                            IsValid = false,
                            Message = $"❌ Error: {onlineEx.Message}",
                            ErrorType = QRValidationErrorType.ValidationError
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new QRValidationResult
                {
                    IsValid = false,
                    Message = $"Error validating QR code: {ex.Message}",
                    ErrorType = QRValidationErrorType.ValidationError
                };
            }
        }

        private StudentQRData? ParseQRCodeData(string qrCodeData, TeacherInfo teacher)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Parsing QR Code Data ===");
                System.Diagnostics.Debug.WriteLine($"QR Code Data: '{qrCodeData}'");
                System.Diagnostics.Debug.WriteLine($"Teacher School ID: '{teacher.SchoolId}'");

                // Try JSON parsing first
                var jsonResult = JsonSerializer.Deserialize<StudentQRData>(qrCodeData);
                System.Diagnostics.Debug.WriteLine($"JSON parsing successful: {jsonResult != null}");
                return jsonResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON parsing failed: {ex.Message}");

                var parts = qrCodeData.Split('|');
                System.Diagnostics.Debug.WriteLine($"Split into {parts.Length} parts: [{string.Join(", ", parts)}]");

                if (parts.Length >= 5)
                {
                    System.Diagnostics.Debug.WriteLine("Using pipe-separated format (5+ parts)");
                    return new StudentQRData
                    {
                        StudentId = parts[0],
                        FullName = parts[1],
                        GradeLevel = int.TryParse(parts[2], out int grade) ? grade : 0,
                        Section = parts[3],
                        SchoolId = parts[4]
                    };
                }
                else if (parts.Length == 1)
                {
                    System.Diagnostics.Debug.WriteLine("Using single UUID format");
                    return new StudentQRData
                    {
                        StudentId = qrCodeData.Trim(),
                        FullName = "Unknown",
                        GradeLevel = 0,
                        Section = "Unknown",
                        SchoolId = teacher.SchoolId
                    };
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Unsupported format: {parts.Length} parts");
                }
            }
            return null;
        }

        private async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Checking Internet Connection ===");

                // Simple check: if we have internet access, assume we're online
                var hasInternet = Connectivity.NetworkAccess == NetworkAccess.Internet;
                System.Diagnostics.Debug.WriteLine($"Internet connectivity: {hasInternet}");

                if (hasInternet)
                {
                    System.Diagnostics.Debug.WriteLine("Internet connection detected - assuming online");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No internet connection - will use offline mode");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Connection check error: {ex.Message}");
                // If we can't determine connection status, assume online
                return true;
            }
        }

        private async Task<QRValidationResult> ValidateOnlineWithBackupAsync(StudentQRData studentData, TeacherInfo teacher, string attendanceType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== ONLINE MODE: Saving Attendance (Optimized - Single Call) ===");
                System.Diagnostics.Debug.WriteLine($"Student ID: {studentData.StudentId}");
                System.Diagnostics.Debug.WriteLine($"Teacher ID: {teacher.TeacherId}");
                System.Diagnostics.Debug.WriteLine($"Attendance Type: {attendanceType}");
                System.Diagnostics.Debug.WriteLine($"Server Base URL: {_serverBaseUrl}");

                // OPTIMIZATION: Skip separate QR validation - attendance endpoint validates automatically
                // This reduces validation time by ~50% (one HTTP call instead of two)
                System.Diagnostics.Debug.WriteLine("=== OPTIMIZED: Direct attendance save (validation included) ===");
                var currentTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"Current time: {currentTime}");

                HttpResponseMessage response;

                if (attendanceType == "TimeIn")
                {
                    var request = new DailyTimeInRequest
                    {
                        StudentId = studentData.StudentId,
                        Date = currentTime.Date, // Use local date to match user's timezone
                        TimeIn = currentTime.TimeOfDay,
                        TeacherId = teacher.TeacherId
                    };

                    System.Diagnostics.Debug.WriteLine($"Sending request - Date: {request.Date}");
                    var timeInUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/dailyattendance/daily-timein" : $"{_serverBaseUrl}/api/dailyattendance/daily-timein";
                    System.Diagnostics.Debug.WriteLine($"Full URL: {timeInUrl}");
                    response = await _httpClient.PostAsJsonAsync(timeInUrl, request);
                }
                else
                {
                    var request = new DailyTimeOutRequest
                    {
                        StudentId = studentData.StudentId,
                        Date = currentTime.Date,
                        TimeOut = currentTime.TimeOfDay,
                        TeacherId = teacher.TeacherId
                    };

                    System.Diagnostics.Debug.WriteLine($"Sending Time Out request to server");
                    var timeOutUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/dailyattendance/daily-timeout" : $"{_serverBaseUrl}/api/dailyattendance/daily-timeout";
                    System.Diagnostics.Debug.WriteLine($"Full URL: {timeOutUrl}");
                    response = await _httpClient.PostAsJsonAsync(timeOutUrl, request);
                }

                System.Diagnostics.Debug.WriteLine($"Server response status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Server response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Server response content: {responseContent}");

                    var displayType = attendanceType == "TimeIn" ? "Time In" : "Time Out";

                    // IMPORTANT: Check for "already recorded" message FIRST before treating as success
                    // The server returns Success=true even when already recorded, so we must check the message
                    var isAlreadyRecorded = false;
                    string? existingTime = null;
                    
                    try
                    {
                        if (attendanceType == "TimeIn")
                        {
                            var timeInResponse = System.Text.Json.JsonSerializer.Deserialize<DailyTimeInResponse>(responseContent);
                            if (timeInResponse != null)
                            {
                                var message = timeInResponse.Message ?? "";
                                System.Diagnostics.Debug.WriteLine($"TimeIn Response - Success: {timeInResponse.Success}, Message: '{message}', TimeIn: '{timeInResponse.TimeIn}'");
                                
                                // Check for various "already recorded" patterns - MUST check message content
                                if (message.Contains("already recorded", StringComparison.OrdinalIgnoreCase) || 
                                    message.Contains("already marked", StringComparison.OrdinalIgnoreCase) ||
                                    message.Contains("Time In already", StringComparison.OrdinalIgnoreCase) ||
                                    message.Contains("SUCCESS: Time In already", StringComparison.OrdinalIgnoreCase))
                                {
                                    System.Diagnostics.Debug.WriteLine($"✅ Already recorded response detected - Time In already exists");
                                    isAlreadyRecorded = true;
                                    
                                    // Try to get time from response, or extract from message, or use current time as fallback
                                    if (!string.IsNullOrEmpty(timeInResponse.TimeIn) && timeInResponse.TimeIn != "00:00:00")
                                    {
                                        existingTime = timeInResponse.TimeIn;
                                    }
                                    else
                                    {
                                        // Try to extract time from message (e.g., "Time In already recorded at 07:30")
                                        var timeMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{1,2}:\d{2}(?::\d{2})?)");
                                        if (timeMatch.Success)
                                        {
                                            existingTime = timeMatch.Groups[1].Value;
                                        }
                                        else
                                        {
                                            // If we can't find time, just show "Already Time In" without time
                                            existingTime = null; // Will show without time
                                        }
                                    }
                                }
                            }
                        }
                        else if (attendanceType == "TimeOut")
                        {
                            var timeOutResponse = System.Text.Json.JsonSerializer.Deserialize<DailyTimeOutResponse>(responseContent);
                            if (timeOutResponse != null)
                            {
                                var message = timeOutResponse.Message ?? "";
                                System.Diagnostics.Debug.WriteLine($"TimeOut Response - Success: {timeOutResponse.Success}, Message: '{message}', TimeOut: '{timeOutResponse.TimeOut}'");
                                
                                // Check for various "already recorded" patterns
                                if (message.Contains("already recorded", StringComparison.OrdinalIgnoreCase) || 
                                    message.Contains("already marked", StringComparison.OrdinalIgnoreCase) ||
                                    message.Contains("Time Out already", StringComparison.OrdinalIgnoreCase) ||
                                    message.Contains("SUCCESS: Time Out already", StringComparison.OrdinalIgnoreCase))
                                {
                                    System.Diagnostics.Debug.WriteLine($"✅ Already recorded response detected - Time Out already exists");
                                    isAlreadyRecorded = true;
                                    
                                    // Try to get time from response, or extract from message
                                    if (!string.IsNullOrEmpty(timeOutResponse.TimeOut) && timeOutResponse.TimeOut != "00:00:00")
                                    {
                                        existingTime = timeOutResponse.TimeOut;
                                    }
                                    else
                                    {
                                        // Try to extract time from message
                                        var timeMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{1,2}:\d{2}(?::\d{2})?)");
                                        if (timeMatch.Success)
                                        {
                                            existingTime = timeMatch.Groups[1].Value;
                                        }
                                        else
                                        {
                                            existingTime = null; // Will show without time
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing response: {parseEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"Response content: {responseContent}");
                    }
                    
                    // Fallback: Also check raw response content if JSON parsing failed or didn't catch it
                    if (!isAlreadyRecorded)
                    {
                        if (responseContent.Contains("already recorded", StringComparison.OrdinalIgnoreCase) || 
                            responseContent.Contains("already marked", StringComparison.OrdinalIgnoreCase) ||
                            responseContent.Contains("Time In already", StringComparison.OrdinalIgnoreCase) ||
                            responseContent.Contains("Time Out already", StringComparison.OrdinalIgnoreCase) ||
                            responseContent.Contains("SUCCESS: Time In already", StringComparison.OrdinalIgnoreCase) ||
                            responseContent.Contains("SUCCESS: Time Out already", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"✅ Already recorded detected via raw string check");
                            isAlreadyRecorded = true;
                        }
                    }
                    
                    // If already recorded, return warning message
                    if (isAlreadyRecorded)
                    {
                        string message;
                        if (!string.IsNullOrEmpty(existingTime))
                        {
                            // Format time nicely (remove seconds if present)
                            var timeDisplay = existingTime.Length > 5 ? existingTime.Substring(0, 5) : existingTime;
                            message = attendanceType == "TimeIn" 
                                ? $"⚠️ Already Time In ({timeDisplay})" 
                                : $"⚠️ Already Time Out ({timeDisplay})";
                        }
                        else
                        {
                            // Show message without time if we can't extract it
                            message = attendanceType == "TimeIn" 
                                ? $"⚠️ Already Time In" 
                                : $"⚠️ Already Time Out";
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Returning already recorded message: {message}");
                        return new QRValidationResult
                        {
                            IsValid = false, // Set to false to show as warning/error
                            Message = message,
                            StudentData = studentData
                        };
                    }

                    // SUCCESS: Save to MySQL server, now also save to SQLite as backup
                    System.Diagnostics.Debug.WriteLine($"Server save successful, now saving to SQLite as backup...");
                    var backupSuccess = await _offlineDataService.SaveOfflineAttendanceAsync(
                        studentData.StudentId,
                        attendanceType,
                        null, // deviceId - let the service generate it
                        true // isOnlineMode = true for backup records
                    );

                    if (backupSuccess)
                    {
                        // Mark the SQLite record as synced since it's already in MySQL
                        await _offlineDataService.MarkAsSyncedByStudentIdAsync(studentData.StudentId);
                        System.Diagnostics.Debug.WriteLine($"SQLite backup saved and marked as synced");
                    }

                    return new QRValidationResult
                    {
                        IsValid = true,
                        Message = $"✓ Success - {displayType}",
                        StudentData = studentData
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Server error: {errorContent}");
                    System.Diagnostics.Debug.WriteLine($"Error status: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"Error reason: {response.ReasonPhrase}");

                    // Check if this is a "No Time In found" error for TimeOut
                    if (attendanceType == "TimeOut" && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        try
                        {
                            var errorResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(errorContent);
                            if (errorResponse != null && errorResponse.ContainsKey("message"))
                            {
                                var errorMessage = errorResponse["message"]?.ToString();
                                if (errorMessage != null && errorMessage.Contains("No Time In found"))
                                {
                                    System.Diagnostics.Debug.WriteLine("TimeOut called without TimeIn - returning error instead of fallback");
                                    return new QRValidationResult
                                    {
                                        IsValid = false,
                                        Message = "❌ No Time In found for today. Please mark Time In first.",
                                        StudentData = studentData
                                    };
                                }
                                else if (errorMessage != null && errorMessage.Contains("Time Out already marked"))
                                {
                                    System.Diagnostics.Debug.WriteLine("TimeOut already exists - returning error instead of fallback");
                                    return new QRValidationResult
                                    {
                                        IsValid = false,
                                        Message = "❌ Time Out already marked for today.",
                                        StudentData = studentData
                                    };
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error parsing server response: {parseEx.Message}");
                        }
                    }

                    // For other server errors, return error (don't fallback to SQLite when online)
                    System.Diagnostics.Debug.WriteLine("Server error - returning error message");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = $"❌ Server error: {response.StatusCode} - {errorContent}",
                        StudentData = studentData
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Online validation error: {ex.Message}");

                // Check if this is a connection-related exception
                if (ex.Message.Contains("timeout") ||
                    ex.Message.Contains("network") ||
                    ex.Message.Contains("connection") ||
                    ex is HttpRequestException ||
                    ex is TaskCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Connection error - falling back to SQLite");
                    return await ValidateOfflineAsync(studentData, teacher, attendanceType);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Non-connection error - returning error");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = $"❌ Connection error: {ex.Message}",
                        StudentData = studentData
                    };
                }
            }
        }

        private async Task<QRValidationResult> ValidateOnlineAsync(StudentQRData studentData, TeacherInfo teacher, string attendanceType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== ONLINE MODE: Validating QR Code First ===");
                System.Diagnostics.Debug.WriteLine($"Student ID: {studentData.StudentId}");
                System.Diagnostics.Debug.WriteLine($"Teacher ID: {teacher.TeacherId}");
                System.Diagnostics.Debug.WriteLine($"Attendance Type: {attendanceType}");

                // FIRST: Validate QR code with server (this includes section validation)
                System.Diagnostics.Debug.WriteLine("=== STEP 1: Server QR Validation ===");
                var validationRequest = new
                {
                    QRCodeData = studentData.StudentId,
                    TeacherId = teacher.TeacherId
                };

                var validationUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/qrvalidation/validate" : $"{_serverBaseUrl}/api/qrvalidation/validate";
                System.Diagnostics.Debug.WriteLine($"Validation URL: {validationUrl}");

                var validationResponse = await _httpClient.PostAsJsonAsync(validationUrl, validationRequest);
                var validationContent = await validationResponse.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Server validation response: {validationResponse.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Server validation content: {validationContent}");

                if (!validationResponse.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Server validation failed: {validationResponse.StatusCode}");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = "Server validation failed. Please try again.",
                        ErrorType = QRValidationErrorType.ValidationError
                    };
                }

                // Parse validation response
                var validationResult = System.Text.Json.JsonSerializer.Deserialize<ServerQRValidationResult>(validationContent);
                if (validationResult == null || !validationResult.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"QR validation failed: {validationResult?.Message}");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = validationResult?.Message ?? "QR code validation failed",
                        ErrorType = QRValidationErrorType.ValidationError
                    };
                }

                System.Diagnostics.Debug.WriteLine($"✅ Server validation passed: {validationResult.Message}");

                // SECOND: Save attendance to server
                System.Diagnostics.Debug.WriteLine("=== STEP 2: Saving Attendance ===");
                var currentTime = DateTime.Now;

                HttpResponseMessage response;

                if (attendanceType == "TimeIn")
                {
                    var request = new DailyTimeInRequest
                    {
                        StudentId = studentData.StudentId,
                        Date = currentTime.Date,
                        TimeIn = currentTime.TimeOfDay
                    };

                    System.Diagnostics.Debug.WriteLine($"Sending Time In request to server");
                    var timeInUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/dailyattendance/daily-timein" : $"{_serverBaseUrl}/api/dailyattendance/daily-timein";
                    System.Diagnostics.Debug.WriteLine($"Full URL: {timeInUrl}");
                    response = await _httpClient.PostAsJsonAsync(timeInUrl, request);
                }
                else
                {
                    var request = new DailyTimeOutRequest
                    {
                        StudentId = studentData.StudentId,
                        Date = currentTime.Date,
                        TimeOut = currentTime.TimeOfDay,
                        TeacherId = teacher.TeacherId
                    };

                    System.Diagnostics.Debug.WriteLine($"Sending Time Out request to server");
                    var timeOutUrl = _serverBaseUrl.EndsWith("/") ? $"{_serverBaseUrl}api/dailyattendance/daily-timeout" : $"{_serverBaseUrl}/api/dailyattendance/daily-timeout";
                    System.Diagnostics.Debug.WriteLine($"Full URL: {timeOutUrl}");
                    response = await _httpClient.PostAsJsonAsync(timeOutUrl, request);
                }

                System.Diagnostics.Debug.WriteLine($"Server response status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Server response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Server response content: {responseContent}");

                    var displayType = attendanceType == "TimeIn" ? "Time In" : "Time Out";
                    return new QRValidationResult
                    {
                        IsValid = true,
                        Message = $"✓ Success - {displayType}",
                        StudentData = studentData
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Server error: {errorContent}");
                    System.Diagnostics.Debug.WriteLine($"Error status: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"Error reason: {response.ReasonPhrase}");

                    // Check if this is a "No Time In found" error for TimeOut
                    if (attendanceType == "TimeOut" && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        try
                        {
                            var errorResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(errorContent);
                            if (errorResponse != null && errorResponse.ContainsKey("message"))
                            {
                                var errorMessage = errorResponse["message"]?.ToString();
                                if (errorMessage != null && errorMessage.Contains("No Time In found"))
                                {
                                    System.Diagnostics.Debug.WriteLine("TimeOut called without TimeIn - returning error instead of fallback");
                                    return new QRValidationResult
                                    {
                                        IsValid = false,
                                        Message = "❌ No Time In found for today. Please mark Time In first.",
                                        StudentData = studentData
                                    };
                                }
                                else if (errorMessage != null && errorMessage.Contains("Time Out already marked"))
                                {
                                    System.Diagnostics.Debug.WriteLine("TimeOut already exists - returning error instead of fallback");
                                    return new QRValidationResult
                                    {
                                        IsValid = false,
                                        Message = "❌ Time Out already marked for today.",
                                        StudentData = studentData
                                    };
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error parsing server response: {parseEx.Message}");
                        }
                    }

                    // For other server errors, return error (don't fallback to SQLite when online)
                    System.Diagnostics.Debug.WriteLine("Server error - returning error message");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = $"❌ Server error: {response.StatusCode} - {errorContent}",
                        StudentData = studentData
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Online validation error: {ex.Message}");

                // Online failed - return error (don't fallback to SQLite when online)
                System.Diagnostics.Debug.WriteLine("Online validation failed - returning error");
                return new QRValidationResult
                {
                    IsValid = false,
                    Message = $"❌ Connection error: {ex.Message}",
                    StudentData = studentData
                };
            }
        }

        private string GetDeviceId()
        {
            try
            {
                // Generate a unique device ID for this session
                return $"MAUI_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmmss}";
            }
            catch
            {
                return $"MAUI_Device_{DateTime.Now:yyyyMMddHHmmss}";
            }
        }

        private async Task<QRValidationResult> ValidateOfflineAsync(StudentQRData studentData, TeacherInfo teacher, string attendanceType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== OFFLINE MODE: Saving to SQLite ===");
                System.Diagnostics.Debug.WriteLine($"Student ID: {studentData.StudentId}");
                System.Diagnostics.Debug.WriteLine($"Teacher ID: {teacher.TeacherId}");
                System.Diagnostics.Debug.WriteLine($"Attendance Type: {attendanceType}");

                // FIRST: Check if attendance already exists (same as online mode)
                var existingAttendance = await CheckOfflineAttendanceStatusAsync(studentData.StudentId, attendanceType);
                
                System.Diagnostics.Debug.WriteLine($"Offline check result - HasTimeIn: {existingAttendance.HasTimeIn} ({existingAttendance.TimeIn}), HasTimeOut: {existingAttendance.HasTimeOut} ({existingAttendance.TimeOut})");
                
                if (attendanceType == "TimeIn" && existingAttendance.HasTimeIn)
                {
                    System.Diagnostics.Debug.WriteLine($"Time In already exists in offline mode: {existingAttendance.TimeIn}");
                    return new QRValidationResult
                    {
                        IsValid = false, // Set to false to show as warning
                        Message = $"⚠️ Already Time In ({existingAttendance.TimeIn})",
                        StudentData = studentData
                    };
                }
                
                if (attendanceType == "TimeOut" && existingAttendance.HasTimeOut)
                {
                    System.Diagnostics.Debug.WriteLine($"Time Out already exists in offline mode: {existingAttendance.TimeOut}");
                    return new QRValidationResult
                    {
                        IsValid = false, // Set to false to show as warning
                        Message = $"⚠️ Already Time Out ({existingAttendance.TimeOut})",
                        StudentData = studentData
                    };
                }
                
                // Check if Time In exists before allowing Time Out
                if (attendanceType == "TimeOut" && !existingAttendance.HasTimeIn)
                {
                    System.Diagnostics.Debug.WriteLine("Time Out requested but no Time In found in offline mode");
                    return new QRValidationResult
                    {
                        IsValid = false,
                        Message = "❌ No Time In found for today. Please mark Time In first.",
                        StudentData = studentData
                    };
                }

                // SECOND: Save to SQLite database if no duplicate
                var success = await _offlineDataService.SaveOfflineAttendanceAsync(
                    studentData.StudentId,
                    attendanceType, // Use the provided attendance type
                    GetDeviceId(), // Generate device ID for offline records
                    false // isOnlineMode = false for offline records
                );

                System.Diagnostics.Debug.WriteLine($"SQLite save result: {success}");

                if (success)
                {
                    var displayType = attendanceType == "TimeIn" ? "Time In" : "Time Out";
                    return new QRValidationResult
                    {
                        IsValid = true,
                        Message = $"✓ Success - {displayType}",
                        StudentData = studentData
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
        
        private async Task<OfflineAttendanceStatus> CheckOfflineAttendanceStatusAsync(string studentId, string attendanceType)
        {
            var status = new OfflineAttendanceStatus { HasTimeIn = false, HasTimeOut = false, TimeIn = "", TimeOut = "" };
            
            try
            {
                var today = DateTime.Today;
                
                // Get existing records from offline database
                // Check unsynced records to see if student was already marked today
                var unsyncedRecords = await _offlineDataService.GetUnsyncedDailyAttendanceAsync();
                
                // Check for TimeIn record
                var timeInRecord = unsyncedRecords
                    .FirstOrDefault(r => r.StudentId == studentId && 
                                       r.Date.Date == today && 
                                       r.AttendanceType == "TimeIn");
                if (timeInRecord != null)
                {
                    status.HasTimeIn = true;
                    status.TimeIn = timeInRecord.TimeIn ?? "";
                }
                
                // Check for TimeOut record
                var timeOutRecord = unsyncedRecords
                    .FirstOrDefault(r => r.StudentId == studentId && 
                                       r.Date.Date == today && 
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
        
        private class OfflineAttendanceStatus
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
        public async Task<bool> SyncOfflineDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Starting Offline Data Sync ===");

                var unsyncedRecords = await _offlineDataService.GetUnsyncedAttendanceAsync();
                System.Diagnostics.Debug.WriteLine($"Found {unsyncedRecords.Count} unsynced records");

                if (!unsyncedRecords.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No unsynced records to sync");
                    return true;
                }

                var teacher = await _authService.GetCurrentTeacherAsync();
                if (teacher == null)
                {
                    System.Diagnostics.Debug.WriteLine("No teacher logged in, cannot sync");
                    return false;
                }

                // Get the actual daily attendance records with their stored TimeIn/TimeOut values
                var offlineRecords = await _offlineDataService.GetUnsyncedDailyAttendanceAsync();

                // Group by student and date, then get the latest record for each group
                var groupedRecords = offlineRecords
                    .GroupBy(r => new { r.StudentId, Date = r.Date })
                    .Select(group =>
                    {
                        // Get the LATEST record for this student/date
                        var latestRecord = group.OrderByDescending(r => r.CreatedAt).FirstOrDefault();

                        if (latestRecord == null)
                        {
                            return null;
                        }

                        // Use the actual stored TimeIn and TimeOut values from the database
                        string timeIn = latestRecord.TimeIn;
                        string timeOut = latestRecord.TimeOut;
                        string status = latestRecord.Status;
                        string remarks = "";

                        // Determine remarks based on what's available
                        if (!string.IsNullOrEmpty(timeIn) && !string.IsNullOrEmpty(timeOut))
                        {
                            // Both TimeIn and TimeOut exist
                            var timeInTime = TimeSpan.Parse(timeIn);
                            var timeOutTime = TimeSpan.Parse(timeOut);

                            // Check if it's a whole day (7:30 AM - 4:30 PM range)
                            if (timeInTime.Hours <= 7 && timeOutTime.Hours >= 16)
                            {
                                remarks = "Whole Day";
                            }
                            else
                            {
                                remarks = "Half Day";
                            }
                        }
                        else if (!string.IsNullOrEmpty(timeIn))
                        {
                            remarks = "Half Day";
                        }
                        else if (!string.IsNullOrEmpty(timeOut))
                        {
                            remarks = "Half Day";
                        }

                        return new
                        {
                            StudentId = group.Key.StudentId,
                            Date = group.Key.Date,
                            TimeIn = timeIn,
                            TimeOut = timeOut,
                            Status = status,
                            Remarks = remarks,
                            DeviceId = latestRecord.DeviceId
                        };
                    }).Where(r => r != null).ToList();

                var syncRequest = new
                {
                    TeacherId = teacher.TeacherId,
                    AttendanceRecords = groupedRecords
                };

                System.Diagnostics.Debug.WriteLine($"Sending {groupedRecords.Count} consolidated records to server for sync");

                // Debug: Log each record being sent
                foreach (var record in groupedRecords)
                {
                    System.Diagnostics.Debug.WriteLine($"Sync Record - StudentId: {record.StudentId}, Date: {record.Date}, TimeIn: {record.TimeIn}, TimeOut: {record.TimeOut}, Status: {record.Status}, Remarks: {record.Remarks}");
                }

                var response = await _httpClient.PostAsJsonAsync($"{_serverBaseUrl}api/dailyattendance/sync-offline-data", syncRequest);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<dynamic>();
                    System.Diagnostics.Debug.WriteLine($"Sync successful: {result}");

                    // Mark all records as synced first, then delete them
                    System.Diagnostics.Debug.WriteLine($"Marking {unsyncedRecords.Count} records as synced...");
                    foreach (var record in unsyncedRecords)
                    {
                        System.Diagnostics.Debug.WriteLine($"Marking record {record.Id} as synced...");
                        var markResult = await _offlineDataService.MarkAsSyncedAsync(record.Id);
                        System.Diagnostics.Debug.WriteLine($"Mark result for record {record.Id}: {markResult}");

                        // If marking failed, try alternative approach
                        if (!markResult)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to mark record {record.Id}, trying alternative approach...");
                            var alternativeResult = await _offlineDataService.MarkAsSyncedByStudentIdAsync(record.StudentId);
                            System.Diagnostics.Debug.WriteLine($"Alternative mark result for student {record.StudentId}: {alternativeResult}");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"Completed marking {unsyncedRecords.Count} records as synced");

                    // Now delete all synced records to clean up the database
                    System.Diagnostics.Debug.WriteLine("Deleting synced records to clean up database...");
                    var deleteResult = await _offlineDataService.DeleteSyncedRecordsAsync();
                    System.Diagnostics.Debug.WriteLine($"Delete synced records result: {deleteResult}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Sync failed with status: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing offline data: {ex.Message}");
                return false;
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
