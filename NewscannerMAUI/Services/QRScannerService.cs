using NewscannerMAUI.Pages;

namespace NewscannerMAUI.Services
{
    public class QRScannerService
    {
        private readonly OfflineDataService _offlineDataService;
        private readonly HybridQRValidationService _qrValidationService;
        private string _currentAttendanceType = string.Empty;
        private static readonly System.Threading.SemaphoreSlim _navLock = new(1, 1);

        public event EventHandler<string>? QRCodeScanned;
        public event EventHandler<string>? OfflineDataSaved;
        public event EventHandler? ScannerClosed;

        public QRScannerService(OfflineDataService offlineDataService, HybridQRValidationService qrValidationService)
        {
            _offlineDataService = offlineDataService;
            _qrValidationService = qrValidationService;
        }


        public async Task OpenNativeQRScanner(string attendanceType = "")
        {
            if (!await _navLock.WaitAsync(500)) 
            {
                System.Diagnostics.Debug.WriteLine("Navigation lock busy. Ignoring scan request.");
                return;
            }

            try
            {
                _currentAttendanceType = attendanceType;

                // Sync check on UI thread
                bool alreadyOpen = false;
                await MainThread.InvokeOnMainThreadAsync(() => {
                    var mainPage = Application.Current?.MainPage;
                    if (mainPage == null) return;
                    
                    var navStack = mainPage.Navigation.NavigationStack;
                    alreadyOpen = navStack.Any(p => p is NativeQRScannerPage);
                });

                if (alreadyOpen)
                {
                    System.Diagnostics.Debug.WriteLine("Native QR scanner is already open. Preventing duplicate.");
                    return;
                }

                var scannerPage = new NativeQRScannerPage(_qrValidationService);
                
                // Set the attendance type if provided
                if (!string.IsNullOrEmpty(attendanceType))
                {
                    scannerPage.SetAttendanceType(attendanceType);
                }
                
                // Subscribe to QR code detection
                scannerPage.QRCodeScanned += OnQRCodeScanned;
                scannerPage.ScannerClosed += OnScannerClosed;
                
                await MainThread.InvokeOnMainThreadAsync(async () => {
                    try 
                    {
                        var mainPage = Application.Current?.MainPage;
                        var navigation = mainPage?.Navigation;
                        
                        if (navigation != null)
                        {
                            await navigation.PushAsync(scannerPage);
                            System.Diagnostics.Debug.WriteLine($"Opened native QR scanner with attendance type: {attendanceType}");
                            
                            // Increased delay (1000ms) to ensure Android fragment manager 
                            // has fully finished the layout transition before releasing the lock.
                            await Task.Delay(1000);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("CRITICAL: Navigation is null. Cannot open scanner.");
                        }
                    }
                    catch (Exception pushEx)
                    {
                         System.Diagnostics.Debug.WriteLine($"PushAsync Error: {pushEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening QR scanner: {ex.Message}");
            }
            finally
            {
                _navLock.Release();
            }
        }
        
        private async void OnQRCodeScanned(object? sender, string qrCode)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"QR Code Scanned: {qrCode}");
                
                // The QR code has already been processed by NativeQRScannerPage
                // Just notify subscribers about the scanned QR code
                QRCodeScanned?.Invoke(this, qrCode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing QR code: {ex.Message}");
            }
        }
        
        private void OnScannerClosed(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Scanner closed - notifying subscribers");
            ScannerClosed?.Invoke(this, EventArgs.Empty);
        }

        // These methods are no longer needed since NativeQRScannerPage handles all processing

        private async Task TryAutoSyncAsync()
        {
            try
            {
                // Get API base URL from configuration or use default
                var apiBaseUrl = ApiConfig.BaseUrl; // Configured server URL
                
                // Get current teacher ID (you'll need to implement this)
                var teacherId = "current_teacher_id"; // Replace with actual teacher ID
                
                var syncResult = await _offlineDataService.AutoSyncOfflineDataAsync(apiBaseUrl, teacherId);
                
                if (syncResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-sync completed successfully: {syncResult.Message}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-sync completed with issues: {syncResult.Message}");
                }
                
                // Log invalid students if any
                if (syncResult.InvalidStudents?.Any() == true)
                {
                    System.Diagnostics.Debug.WriteLine($"Removed {syncResult.InvalidStudents.Count} invalid students during auto-sync");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in auto-sync: {ex.Message}");
            }
        }

        public async Task<int> GetOfflineRecordCountAsync()
        {
            return await _offlineDataService.GetUnsyncedCountAsync();
        }

        public async Task<string> ExportOfflineDataAsync()
        {
            return await _offlineDataService.ExportAttendanceDataAsync();
        }

        public async Task<bool> SaveExportToFileAsync(string fileName = null)
        {
            return await _offlineDataService.SaveExportToFileAsync(fileName);
        }

        public bool IsOnline()
        {
            // Connection status is now handled by HybridQRValidationService
            return true; // Default to true, actual status checked by HybridQRValidationService
        }

        public string GetConnectionStatus()
        {
            return "Connection status handled by HybridQRValidationService";
        }

        public async Task ProcessQRCode(string qrCode, string attendanceType = "TimeIn")
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Processing QR Code: {qrCode} ===");
                System.Diagnostics.Debug.WriteLine($"Attendance Type: {attendanceType}");
                
                // Use HybridQRValidationService to validate the QR code
                // This service automatically handles online/offline switching and saving
                var result = await _qrValidationService.ValidateQRCodeAsync(qrCode, attendanceType);
                
                if (result.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"QR code validation successful: {result.Message}");
                    OfflineDataSaved?.Invoke(this, qrCode);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"QR code validation failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing QR code: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task<bool> SyncIndividualRecordAsync(OfflineAttendanceRecord record)
        {
            try
            {
                // Use HybridQRValidationService to sync the record
                var result = await _qrValidationService.SyncOfflineDataAsync();
                
                if (result != null && result.Success)
                {
                    // Mark as synced in local database
                    await _offlineDataService.MarkAttendanceAsSyncedAsync(record.Id);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing individual record: {ex.Message}");
                return false;
            }
        }
    }
}
