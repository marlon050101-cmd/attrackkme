using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using NewscannerMAUI.Services;
using System.Net.Http.Json;
using AttrackSharedClass.Models;
using System.Text.Json;

namespace NewscannerMAUI.Pages
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
        private string _teacherId = string.Empty;
        private readonly string _serverBaseUrl = "https://attrack-sr9l.onrender.com/";

        public event EventHandler<string>? QRCodeScanned;
        public event EventHandler<string>? AttendanceTypeSelected;
        public event EventHandler? ScannerClosed;

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
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // Ensure teacher ID is set before validation
                                    if (string.IsNullOrEmpty(_teacherId))
                                    {
                                        _teacherId = await _qrValidationService.GetCurrentTeacherIdAsync();
                                    }

                                    // 1. Instant Local Check (Pre-Validation for speed)
                                    var localStatus = await _qrValidationService.CheckOfflineAttendanceStatusAsync(result.Value, _currentAttendanceType, _teacherId);
                                    bool isLocalDuplicate = (_currentAttendanceType == "TimeIn" && localStatus.HasTimeIn) || 
                                                           (_currentAttendanceType == "TimeOut" && localStatus.HasTimeOut);

                                    if (isLocalDuplicate)
                                    {
                                        var studentName = await _qrValidationService.GetOfflineStudentNameAsync(result.Value);
                                        var time = _currentAttendanceType == "TimeIn" ? localStatus.TimeIn : localStatus.TimeOut;
                                        
                                        MainThread.BeginInvokeOnMainThread(() =>
                                        {
                                            PlayErrorSound();
                                             string action = _currentAttendanceType == "TimeIn" ? "Timed In" : "Timed Out";
                                             resultLabel.Text = $"{studentName} - Already {action} at {time}";
                                            resultLabel.TextColor = Colors.Orange;
                                            resultLabel.IsVisible = true;
                                            statusLabel.Text = $"Already {action}";
                                            statusLabel.TextColor = Colors.Orange;
                                            
                                            Task.Delay(1500).ContinueWith(_ => {
                                                MainThread.BeginInvokeOnMainThread(() => {
                                                    resultLabel.IsVisible = false;
                                                    _isProcessing = false;
                                                });
                                            });
                                        });
                                        return;
                                    }

                                    // 2. Full Validation (Hybrid)
                                    var validationResult = await _qrValidationService.ValidateQRCodeAsync(result.Value, _currentAttendanceType, _teacherId);
                                    
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        try
                                        {
                                            if (validationResult.IsValid)
                                            {
                                                PlaySuccessSound();
                                                
                                                resultLabel.Text = validationResult.Message ?? "✓ Success";
                                                resultLabel.TextColor = Colors.Green;
                                                resultLabel.IsVisible = true;
                                                
                                                statusLabel.Text = "Scan Recorded Successfully";
                                                statusLabel.TextColor = Colors.Green;
                                                
                                                QRCodeScanned?.Invoke(this, result.Value);
                                                
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
                                                PlayErrorSound();
                                                
                                                bool isAlreadyRecorded = validationResult.Message.Contains("Already") || 
                                                                         validationResult.Message.Contains("already");
                                                
                                                resultLabel.Text = isAlreadyRecorded ? validationResult.Message : $"✗ {validationResult.Message}";
                                                resultLabel.TextColor = isAlreadyRecorded ? Colors.Orange : Colors.Red;
                                                resultLabel.IsVisible = true;
                                                
                                                 string statusAction = _currentAttendanceType == "TimeIn" ? "Timed In" : "Timed Out";
                                                 statusLabel.Text = isAlreadyRecorded ? $"Already {statusAction}" : "Invalid QR code - Please try again";
                                                 statusLabel.TextColor = isAlreadyRecorded ? Colors.Orange : Colors.Red;
                                                
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
                                            _isProcessing = false;
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error validating QR code: {ex.Message}");
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        resultLabel.Text = $"✗ Error: {ex.Message}";
                                        resultLabel.TextColor = Colors.Red;
                                        resultLabel.IsVisible = true;
                                        _isProcessing = false;
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
                                    _isProcessing = false;
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
            System.Diagnostics.Debug.WriteLine($"Scanner mode set to: {_currentAttendanceType}");
            
            // Try to get teacher ID if not already set
            if (string.IsNullOrEmpty(_teacherId) && _qrValidationService != null)
            {
                _ = Task.Run(async () => {
                   _teacherId = await _qrValidationService.GetCurrentTeacherIdAsync();
                });
            }

            UpdateModeDisplay();
            
            // Update UI to show the mode
            MainThread.BeginInvokeOnMainThread(() => {
                string friendlyMode = (_currentAttendanceType == "TimeIn" || string.IsNullOrEmpty(_currentAttendanceType)) ? "TIME IN" : "TIME OUT";
                statusLabel.Text = $"Ready - Mode: {friendlyMode}";
                statusLabel.TextColor = (_currentAttendanceType == "TimeOut") ? Colors.Red : Colors.Green;
            });
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
            ScannerClosed?.Invoke(this, EventArgs.Empty);
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
            // Scanning is always allowed according to recent requirements
            return true;
        }

        private string GetScanningStatusMessage()
        {
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
            // Notify any Blazor components that the scanner has closed (e.g. Android back button)
            ScannerClosed?.Invoke(this, EventArgs.Empty);
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
