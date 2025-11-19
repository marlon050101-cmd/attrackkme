using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using ScannerMaui.Services;
using System.Net.Http;
using System.Net.Http.Json;
using AttrackSharedClass.Models;
using System.Text.Json;

namespace ScannerMaui.Pages
{
    public partial class NativeQRScannerPage : ContentPage
    {
        private bool _isScanning = true;
        private CameraLocation _currentCameraLocation = CameraLocation.Rear;
        private bool _isTorchOn = false;
        private string _currentAttendanceType = string.Empty;
        private HybridQRValidationService? _qrValidationService;
        private string _lastScannedCode = string.Empty;
        private DateTime _lastScanTime = DateTime.MinValue;
        private bool _isProcessing = false;
        private readonly string _serverBaseUrl = "https://attrak-8gku.onrender.com/";

        public event EventHandler<string>? QRCodeScanned;
        public event EventHandler<string>? AttendanceTypeSelected;

        public NativeQRScannerPage()
        {
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("NativeQRScannerPage created successfully");
            
            // Configure camera for faster QR code scanning
            cameraView.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                AutoRotate = true,
                TryHarder = false, // Set to false for faster scanning
                TryInverted = false, // Set to false for faster scanning
                Multiple = false // Only scan one code at a time for speed
            };
        }

        public NativeQRScannerPage(HybridQRValidationService qrValidationService) : this()
        {
            _qrValidationService = qrValidationService;
        }

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            try
            {
                if (e.Results?.Any() == true)
                {
                    var result = e.Results.FirstOrDefault();
                    if (result != null && !string.IsNullOrEmpty(result.Value))
                    {
                        // Prevent duplicate processing of the same QR code (reduced to 0.2s for faster scanning)
                        var currentTime = DateTime.Now;
                        if (result.Value == _lastScannedCode && 
                            (currentTime - _lastScanTime).TotalSeconds < 0.2)
                        {
                            System.Diagnostics.Debug.WriteLine($"Duplicate QR code detected, ignoring: {result.Value}");
                            return;
                        }
                        
                        // Prevent multiple simultaneous processing
                        if (_isProcessing)
                        {
                            System.Diagnostics.Debug.WriteLine("Already processing a QR code, ignoring new scan");
                            return;
                        }
                        
                        _lastScannedCode = result.Value;
                        _lastScanTime = currentTime;
                        _isProcessing = true;
                        
                        System.Diagnostics.Debug.WriteLine($"QR Code detected: {result.Value}");
                        
                        // Show immediate feedback that QR was detected
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            statusLabel.Text = "🔍 QR Code detected! Validating...";
                            statusLabel.TextColor = Colors.Orange;
                        });
                        
                        // Check if scanning is allowed at current time
                        if (!IsScanningAllowed())
                        {
                            var statusMessage = GetScanningStatusMessage();
                            
                            // Play error sound for scanning not allowed
                            PlayErrorSound();
                            
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                resultLabel.Text = "Scanning Not Allowed";
                                resultLabel.TextColor = Colors.Red;
                                resultLabel.IsVisible = true;
                                
                                statusLabel.Text = statusMessage;
                                statusLabel.TextColor = Colors.Red;
                            });
                            
                            // Clear the error message after 3 seconds
                            Task.Delay(3000).ContinueWith(_ => 
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    resultLabel.IsVisible = false;
                                    resultLabel.Text = "";
                                    statusLabel.Text = "Ready to scan next QR code";
                                    statusLabel.TextColor = Colors.Green;
                                    _isProcessing = false;
                                });
                            });
                            
                            return; // Don't process the QR code
                        }
                        
                        // Validate QR code if validation service is available - OPTIMIZED FOR SPEED
                        if (_qrValidationService != null)
                        {
                            // Start validation immediately without waiting for status check
                            // The server will handle duplicate checks, making it faster
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"=== QR Code Scanned: {result.Value} ===");
                                    System.Diagnostics.Debug.WriteLine($"Attendance Type: {_currentAttendanceType}");
                                    
                                    // OPTIMIZATION: Validate immediately and let server handle duplicate checks
                                    // This is faster than checking status first, then validating
                                    var validationResult = await _qrValidationService.ValidateQRCodeAsync(result.Value, _currentAttendanceType);
                                    
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        try
                                        {
                                            if (validationResult.IsValid)
                                            {
                                                // Play success sound
                                                PlaySuccessSound();
                                                
                                                // Show success message with attendance type
                                                resultLabel.Text = validationResult.Message ?? "✓ Success";
                                                resultLabel.TextColor = Colors.Green;
                                                resultLabel.IsVisible = true;
                                                
                                                statusLabel.Text = "QR code scanned successfully";
                                                statusLabel.TextColor = Colors.Green;
                                                
                                                // Notify parent page about the scanned code
                                                QRCodeScanned?.Invoke(this, result.Value);
                                                
                                                // Clear the result after 1.5 seconds (faster for next scan - smoother queue)
                                                Task.Delay(1500).ContinueWith(_ => 
                                                {
                                                    MainThread.BeginInvokeOnMainThread(() =>
                                                    {
                                                        resultLabel.IsVisible = false;
                                                        resultLabel.Text = "";
                                                        statusLabel.Text = "Ready to scan next QR code";
                                                        statusLabel.TextColor = Colors.Green;
                                                        _isProcessing = false;
                                                    });
                                                });
                                            }
                                            else
                                            {
                                                // Check if this is an "already recorded" warning
                                                bool isAlreadyRecorded = validationResult.Message.Contains("Already") || 
                                                                         validationResult.Message.Contains("already");
                                                
                                                if (isAlreadyRecorded)
                                                {
                                                    // Play error sound for warning
                                                    PlayErrorSound();
                                                    
                                                    // Show warning message in orange color
                                                    resultLabel.Text = validationResult.Message;
                                                    resultLabel.TextColor = Colors.Orange;
                                                    resultLabel.IsVisible = true;
                                                    
                                                    statusLabel.Text = "Student already marked as attended";
                                                    statusLabel.TextColor = Colors.Orange;
                                                }
                                                else
                                                {
                                                    // Play error sound for actual error
                                                    PlayErrorSound();
                                                    
                                                    // Show error message in red color
                                                    resultLabel.Text = $"✗ {validationResult.Message}";
                                                    resultLabel.TextColor = Colors.Red;
                                                    resultLabel.IsVisible = true;
                                                    
                                                    statusLabel.Text = "Invalid QR code - Please try again";
                                                    statusLabel.TextColor = Colors.Red;
                                                }
                                                
                                                // Clear the message after 2 seconds (faster for next scan - smoother queue)
                                                Task.Delay(2000).ContinueWith(_ => 
                                                {
                                                    MainThread.BeginInvokeOnMainThread(() =>
                                                    {
                                                        resultLabel.IsVisible = false;
                                                        resultLabel.Text = "";
                                                        statusLabel.Text = "Ready to scan next QR code";
                                                        statusLabel.TextColor = Colors.Green;
                                                        _isProcessing = false;
                                                    });
                                                });
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}");
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error validating QR code: {ex.Message}");
                                    
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        resultLabel.Text = $"✗ Error validating QR code: {ex.Message}";
                                        resultLabel.TextColor = Colors.Red;
                                        resultLabel.IsVisible = true;
                                        
                                        statusLabel.Text = "Validation error - Please try again";
                                        statusLabel.TextColor = Colors.Red;
                                        
                                        // Reset processing flag after error
                                        Task.Delay(3000).ContinueWith(_ => 
                                        {
                                            MainThread.BeginInvokeOnMainThread(() =>
                                            {
                                                _isProcessing = false;
                                            });
                                        });
                                    });
                                }
                            });
                        }
                        else
                        {
                            // Fallback to original behavior if no validation service
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                try
                                {
                                    // Show the scanned result
                                    resultLabel.Text = $"Scanned: {result.Value}";
                                    resultLabel.IsVisible = true;
                                    
                                    // Update status
                                    statusLabel.Text = "QR Code detected!";
                                    statusLabel.TextColor = Colors.Green;
                                    
                                    // Notify parent page about the scanned code
                                    QRCodeScanned?.Invoke(this, result.Value);
                                    
                                    // Show success feedback briefly
                                    resultLabel.Text = $"✓ Success: {result.Value}";
                                    resultLabel.TextColor = Colors.Green;
                                    
                                    // Clear the result after 2 seconds
                                    Task.Delay(2000).ContinueWith(_ => 
                                    {
                                        MainThread.BeginInvokeOnMainThread(() =>
                                        {
                                            resultLabel.IsVisible = false;
                                            resultLabel.Text = "";
                                            statusLabel.Text = "Ready to scan next QR code";
                                            statusLabel.TextColor = Colors.Green;
                                            _isProcessing = false;
                                        });
                                    });
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}");
                                }
                            });
                        }
                        
                        // Continue scanning for continuous mode
                        // Don't stop scanning - let it continue automatically
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnBarcodesDetected: {ex.Message}");
            }
        }


        private void OnTorchClicked(object? sender, EventArgs e)
        {
            _isTorchOn = !_isTorchOn;
            cameraView.IsTorchOn = _isTorchOn;
            
            torchButton.Text = _isTorchOn ? "Flashlight ON" : "Flashlight";
            torchButton.BackgroundColor = _isTorchOn ? Colors.Yellow : Colors.Orange;
            
            statusLabel.Text = _isTorchOn ? "Flashlight turned ON" : "Flashlight turned OFF";
            statusLabel.TextColor = Colors.Blue;
        }

        private void OnSwitchCameraClicked(object? sender, EventArgs e)
        {
            _currentCameraLocation = _currentCameraLocation == CameraLocation.Rear 
                ? CameraLocation.Front 
                : CameraLocation.Rear;
            
            cameraView.CameraLocation = _currentCameraLocation;
            
            var cameraType = _currentCameraLocation == CameraLocation.Rear ? "rear" : "front";
            statusLabel.Text = $"Switched to {cameraType} camera";
            statusLabel.TextColor = Colors.Blue;
        }

        public void SetAttendanceType(string attendanceType)
        {
            _currentAttendanceType = attendanceType;
            UpdateModeDisplay();
            
            statusLabel.Text = $"{(_currentAttendanceType == "TimeIn" ? "Time In" : "Time Out")} mode - Ready to scan";
            statusLabel.TextColor = Colors.Green;
        }

        private void UpdateModeDisplay()
        {
            if (!string.IsNullOrEmpty(_currentAttendanceType))
            {
                var modeText = _currentAttendanceType == "TimeIn" ? "Time In" : "Time Out";
                var icon = _currentAttendanceType == "TimeIn" ? "⏰" : "🔔";
                modeLabel.Text = $"{icon} {modeText}";
                
                // Set different colors for different modes
                modeLabel.TextColor = _currentAttendanceType == "TimeIn" ? Colors.LightBlue : Colors.LightYellow;
            }
            else
            {
                modeLabel.Text = "❓ No mode selected";
                modeLabel.TextColor = Colors.White;
            }
        }

        private async void OnDoneClicked(object? sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async void PlaySuccessSound()
        {
            try
            {
                // Play vibration feedback for mobile devices
                if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    // Use HapticFeedback for mobile devices
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
                
                // Play actual beep sound using AudioService
                await AudioService.PlaySuccessBeepAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error playing success sound: {ex.Message}");
            }
        }
        
        
        private async void PlayErrorSound()
        {
            try
            {
                // Play error vibration feedback for mobile devices
                if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    // Use different haptic feedback for error
                    HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
                }
                
                // Play actual error beep sound using AudioService
                await AudioService.PlayErrorBeepAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error playing error sound: {ex.Message}");
            }
        }
        

        private bool IsScanningAllowed()
        {
            var currentTime = DateTime.Now.TimeOfDay;
            var currentDay = DateTime.Now.DayOfWeek;
            
            // No scanning on weekends
            if (currentDay == DayOfWeek.Saturday || currentDay == DayOfWeek.Sunday)
            {
                return false;
            }
            
            // Defined windows (must match QRScanner.razor)
            var morningInStart = new TimeSpan(5, 50, 0);  // 5:50 AM
            var morningInEnd   = new TimeSpan(8, 30, 0);  // 8:30 AM
            var halfDayStart   = new TimeSpan(11, 30, 0); // 11:30 AM
            var halfDayEnd     = new TimeSpan(12, 0, 0);  // 12:00 PM
            var pmStart        = new TimeSpan(12, 20, 0); // 12:20 PM
            var pmEnd          = new TimeSpan(18, 40, 0); // 6:40 PM
            
            if (!string.IsNullOrEmpty(_currentAttendanceType))
            {
                if (_currentAttendanceType == "TimeIn")
                {
                    // Time In allowed: 5:50 AM - 8:30 AM (Morning) OR 11:30 AM-12:00 PM (Half Day)
                    var inAllowed =
                        (currentTime >= morningInStart && currentTime <= morningInEnd) ||
                        (currentTime >= halfDayStart   && currentTime <= halfDayEnd);
                    return inAllowed;
                }
                else if (_currentAttendanceType == "TimeOut")
                {
                    // Time Out allowed: 11:30 AM-12:00 PM (Half Day) OR 12:20 PM-6:40 PM (PM)
                    var outAllowed =
                        (currentTime >= halfDayStart && currentTime <= halfDayEnd) ||
                        (currentTime >= pmStart      && currentTime <= pmEnd);
                    return outAllowed;
                }
            }
            
            var anyAllowed =
                (currentTime >= morningInStart && currentTime <= morningInEnd) ||
                (currentTime >= halfDayStart   && currentTime <= halfDayEnd)   ||
                (currentTime >= pmStart        && currentTime <= pmEnd);
            return anyAllowed;
        }

        private string GetScanningStatusMessage()
        {
            var currentTime = DateTime.Now.TimeOfDay;
            var currentDay = DateTime.Now.DayOfWeek;
            
            // Weekend
            if (currentDay == DayOfWeek.Saturday || currentDay == DayOfWeek.Sunday)
            {
                return "Scanning not allowed: Weekend";
            }
            
            // Windows
            var morningInStart = new TimeSpan(5, 50, 0);
            var morningInEnd   = new TimeSpan(8, 30, 0);
            var halfDayStart   = new TimeSpan(11, 30, 0);
            var halfDayEnd     = new TimeSpan(12, 0, 0);
            var pmStart        = new TimeSpan(12, 20, 0);
            var pmEnd          = new TimeSpan(18, 40, 0);
            
            bool inMorningWindow = currentTime >= morningInStart && currentTime <= morningInEnd;
            bool inHalfDayWindow = currentTime >= halfDayStart && currentTime <= halfDayEnd;
            bool inPmWindow      = currentTime >= pmStart && currentTime <= pmEnd;
            
            if (!inMorningWindow && !inHalfDayWindow && !inPmWindow)
            {
                if (currentTime < morningInStart) return "Scanning not allowed: Too early (before 5:50 AM)";
                if (currentTime > pmEnd) return "Scanning not allowed: Too late (after 6:40 PM)";
                if (currentTime > halfDayEnd && currentTime < pmStart) return "Scanning paused: Break (12:00 PM–12:20 PM)";
                if (currentTime > morningInEnd && currentTime < halfDayStart) return "Scanning paused: Outside allowed window";
                return "Scanning not allowed at this time";
            }
            
            if (!string.IsNullOrEmpty(_currentAttendanceType))
            {
                if (_currentAttendanceType == "TimeIn")
                {
                    if (inMorningWindow) return "Time In allowed: 5:50 AM–8:30 AM";
                    if (inHalfDayWindow) return "Time In allowed (Half-day): 11:30 AM–12:00 PM";
                    return "Time In not allowed now";
                }
                else if (_currentAttendanceType == "TimeOut")
                {
                    if (inHalfDayWindow) return "Time Out allowed (Half-day): 11:30 AM–12:00 PM";
                    if (inPmWindow) return "Time Out allowed: 12:20 PM–6:40 PM";
                    return "Time Out not allowed now";
                }
            }
            
            return "Scanning allowed: Active window";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            System.Diagnostics.Debug.WriteLine("NativeQRScannerPage OnAppearing called");
            
            try
            {
                // Auto-check and request permissions first
                await CheckAndRequestPermissions();
                
                // Check if scanning is allowed at current time
                if (!IsScanningAllowed())
                {
                    var statusMessage = GetScanningStatusMessage();
                    statusLabel.Text = statusMessage;
                    statusLabel.TextColor = Colors.Red;
                    
                    // Disable camera detection
                    cameraView.IsDetecting = false;
                    _isScanning = false;
                    
                    System.Diagnostics.Debug.WriteLine($"Scanning not allowed: {statusMessage}");
                    return;
                }
                
                if (!_isScanning)
                {
                    _isScanning = true;
                    cameraView.IsDetecting = true;
                    statusLabel.Text = "Camera ready - point at QR code";
                    statusLabel.TextColor = Colors.Green;
                    System.Diagnostics.Debug.WriteLine("Camera initialized and ready");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing camera: {ex.Message}");
                statusLabel.Text = "Camera error - please check permissions";
                statusLabel.TextColor = Colors.Red;
            }
        }
        
        private async Task CheckAndRequestPermissions()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Checking permissions...");
                
                // Check if all permissions are granted
                var allGranted = await PermissionService.CheckAllPermissionsAsync();
                
                if (!allGranted)
                {
                    System.Diagnostics.Debug.WriteLine("Some permissions missing, requesting...");
                    statusLabel.Text = "Requesting permissions...";
                    statusLabel.TextColor = Colors.Orange;
                    
                    // Request all required permissions
                    var granted = await PermissionService.RequestAllRequiredPermissionsAsync();
                    
                    if (granted)
                    {
                        System.Diagnostics.Debug.WriteLine("All permissions granted!");
                        statusLabel.Text = "Permissions granted - Camera ready";
                        statusLabel.TextColor = Colors.Green;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Some permissions denied");
                        statusLabel.Text = "Some permissions denied - App may not work properly";
                        statusLabel.TextColor = Colors.Red;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("All permissions already granted");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking permissions: {ex.Message}");
                statusLabel.Text = "Permission check failed";
                statusLabel.TextColor = Colors.Red;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isScanning = false;
            cameraView.IsDetecting = false;
        }

        private async Task<AttendanceStatus> CheckAttendanceStatusAsync(string qrCodeData)
        {
            var status = new AttendanceStatus { HasTimeIn = false, HasTimeOut = false };
            
            try
            {
                // Extract studentId from QR code data
                string studentId = ExtractStudentIdFromQRCode(qrCodeData);
                if (string.IsNullOrEmpty(studentId))
                {
                    System.Diagnostics.Debug.WriteLine("Could not extract studentId from QR code");
                    // If we can't extract, let the validation service handle it
                    return status;
                }
                
                System.Diagnostics.Debug.WriteLine($"Checking attendance status for student: {studentId}");
                
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var today = DateTime.Today;
                
                // Use daily-history endpoint with days=1 to get today's record which includes both TimeIn and TimeOut
                try
                {
                    var historyUrl = $"{_serverBaseUrl}api/dailyattendance/daily-history/{studentId}?days=1";
                    System.Diagnostics.Debug.WriteLine($"Checking attendance at URL: {historyUrl}");
                    
                    var historyResponse = await httpClient.GetFromJsonAsync<List<DailyAttendanceRecord>>(historyUrl);
                    
                    if (historyResponse != null)
                    {
                        // Find today's record
                        var todayRecord = historyResponse.FirstOrDefault(r => r.Date.Date == today);
                        
                        if (todayRecord != null)
                        {
                            status.HasTimeIn = !string.IsNullOrEmpty(todayRecord.TimeIn);
                            status.HasTimeOut = !string.IsNullOrEmpty(todayRecord.TimeOut);
                            
                            System.Diagnostics.Debug.WriteLine($"Found today's record: TimeIn={status.HasTimeIn}, TimeOut={status.HasTimeOut}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("No record found for today");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking attendance history: {ex.Message}");
                    
                    // Fallback: Check Time In status only using daily-status endpoint
                    try
                    {
                        var statusUrl = $"{_serverBaseUrl}api/dailyattendance/daily-status/{studentId}?date={today:yyyy-MM-dd}";
                        var timeInResponse = await httpClient.GetFromJsonAsync<DailyAttendanceStatus>(statusUrl);
                        status.HasTimeIn = timeInResponse?.TimeIn != null && !string.IsNullOrEmpty(timeInResponse.TimeIn);
                    }
                    catch
                    {
                        // If both fail, return default status
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Attendance status for {studentId}: TimeIn={status.HasTimeIn}, TimeOut={status.HasTimeOut}");
                
                return status;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking attendance status: {ex.Message}");
                // If we can't check, return default (both false) and let the validation service handle it
                return status;
            }
        }
        
        private class AttendanceStatus
        {
            public bool HasTimeIn { get; set; }
            public bool HasTimeOut { get; set; }
        }

        private string ExtractStudentIdFromQRCode(string qrCodeData)
        {
            try
            {
                // Try JSON parsing first
                var jsonResult = JsonSerializer.Deserialize<StudentQRData>(qrCodeData);
                if (jsonResult != null && !string.IsNullOrEmpty(jsonResult.StudentId))
                {
                    return jsonResult.StudentId;
                }
            }
            catch
            {
                // Not JSON, try pipe-separated format
            }
            
            // Try pipe-separated format
            var parts = qrCodeData.Split('|');
            if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0].Trim();
            }
            
            // If all else fails, assume the whole string is the studentId
            return qrCodeData.Trim();
        }

        private class StudentQRData
        {
            public string StudentId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public int GradeLevel { get; set; }
            public string Section { get; set; } = string.Empty;
            public string SchoolId { get; set; } = string.Empty;
        }
    }
}
