using System.Net.Http;
using Microsoft.Maui.Networking;

namespace ScannerMaui.Services
{
    public class ConnectionStatusService
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverBaseUrl;
        private bool _isOnline = false;
        private bool _isChecking = false;
        private Timer? _connectionMonitorTimer;
        private bool _isMonitoring = false;

        public event EventHandler<bool>? ConnectionStatusChanged;

        public bool IsOnline => _isOnline;
        public bool IsChecking => _isChecking;
        public string StatusText => _isChecking ? "Checking..." : (_isOnline ? "Online" : "Offline");

        public ConnectionStatusService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _serverBaseUrl = "https://attrak.onrender.com";
        }

        public async Task CheckConnectionAsync()
        {
            if (_isChecking) return;

            _isChecking = true;
            ConnectionStatusChanged?.Invoke(this, _isOnline);

            try
            {
                // More reliable connection check: test actual server connectivity
                var hasInternet = Connectivity.NetworkAccess == NetworkAccess.Internet;
                var wasOnline = _isOnline;
                
                System.Diagnostics.Debug.WriteLine($"=== Connection Check Started ===");
                System.Diagnostics.Debug.WriteLine($"Network Access: {Connectivity.NetworkAccess}");
                System.Diagnostics.Debug.WriteLine($"Has Internet: {hasInternet}");
                System.Diagnostics.Debug.WriteLine($"Previous Status: {(_isOnline ? "Online" : "Offline")}");
                
                // Test actual server connectivity if we have internet
                if (hasInternet)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)); // Increased timeout
                        var response = await _httpClient.GetAsync($"{_serverBaseUrl}/api/health", cts.Token);
                        _isOnline = response.IsSuccessStatusCode;
                        System.Diagnostics.Debug.WriteLine($"Server connectivity test: {_isOnline} (Status: {response.StatusCode})");
                        
                        // If server responds but with error, we're still "online" but server has issues
                        if (!response.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"Server responded but with error: {response.StatusCode}");
                            _isOnline = false; // Consider offline if server has issues
                        }
                    }
                    catch (TaskCanceledException tce)
                    {
                        System.Diagnostics.Debug.WriteLine($"Server connectivity test timed out: {tce.Message}");
                        _isOnline = false;
                    }
                    catch (HttpRequestException hre)
                    {
                        System.Diagnostics.Debug.WriteLine($"Server connectivity test failed (HTTP): {hre.Message}");
                        _isOnline = false;
                    }
                    catch (Exception serverEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Server connectivity test failed: {serverEx.Message}");
                        _isOnline = false; // If server test fails, we're offline
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No internet connection detected");
                    _isOnline = false;
                }

                System.Diagnostics.Debug.WriteLine($"Final Status: {(_isOnline ? "Online" : "Offline")}");

                // Notify if status changed
                if (wasOnline != _isOnline)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 Connection status changed: {(_isOnline ? "Online" : "Offline")}");
                    ConnectionStatusChanged?.Invoke(this, _isOnline);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Connection status unchanged: {(_isOnline ? "Online" : "Offline")}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Connection check error: {ex.Message}");
                var wasOnline = _isOnline;
                _isOnline = false; // Assume offline if we can't determine

                // Notify if status changed
                if (wasOnline != _isOnline)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 Connection status changed due to error: {(_isOnline ? "Online" : "Offline")}");
                    ConnectionStatusChanged?.Invoke(this, _isOnline);
                }
            }
            finally
            {
                _isChecking = false;
                ConnectionStatusChanged?.Invoke(this, _isOnline);
                System.Diagnostics.Debug.WriteLine($"=== Connection Check Completed ===");
            }
        }

        public async Task<bool> TestServerConnectionAsync(string serverUrl = null)
        {
            try
            {
                var url = serverUrl ?? _serverBaseUrl;
                var response = await _httpClient.GetAsync($"{url}/api/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        // Start continuous connection monitoring
        public void StartConnectionMonitoring()
        {
            if (_isMonitoring) return;
            
            _isMonitoring = true;
            System.Diagnostics.Debug.WriteLine("🔄 Starting connection monitoring...");
            
            // Check connection every 30 seconds when offline, every 2 minutes when online
            var interval = _isOnline ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30);
            
            _connectionMonitorTimer = new Timer(async _ => 
            {
                try
                {
                    await CheckConnectionAsync();
                    
                    // Adjust monitoring frequency based on current status
                    if (_isMonitoring)
                    {
                        var newInterval = _isOnline ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30);
                        _connectionMonitorTimer?.Change(newInterval, newInterval);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Connection monitoring error: {ex.Message}");
                }
            }, null, interval, interval);
        }
        
        // Stop connection monitoring
        public void StopConnectionMonitoring()
        {
            if (!_isMonitoring) return;
            
            _isMonitoring = false;
            System.Diagnostics.Debug.WriteLine("🛑 Stopping connection monitoring...");
            
            _connectionMonitorTimer?.Dispose();
            _connectionMonitorTimer = null;
        }
        
        // Force immediate connection check (useful when user manually triggers)
        public async Task ForceConnectionCheckAsync()
        {
            System.Diagnostics.Debug.WriteLine("🔄 Force connection check requested...");
            await CheckConnectionAsync();
        }
        
        // Dispose resources
        public void Dispose()
        {
            StopConnectionMonitoring();
        }
    }
}
