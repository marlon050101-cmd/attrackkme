using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using AttrackSharedClass.Models;

namespace NewscannerMAUI.Services
{
    /// <summary>
    /// Service that polls the cloud server (Render) for pending SMS messages
    /// and dispatches them using the local GSM modem.
    /// This effectively turns the Windows MAUI app into an SMS Gateway.
    /// </summary>
    public class SmsHubService
    {
        private readonly HttpClient _httpClient;
        private readonly GsmSmsService _gsmService;
        private readonly ILogger<SmsHubService> _logger;
        private readonly string _serverBaseUrl = "https://attrack-sr9l.onrender.com";
        private bool _isRunning = false;
        private CancellationTokenSource? _cts;

        // Stats for Dashboard
        public int PendingCount { get; private set; }
        public int TotalSentCount { get; private set; }
        public string? ModemPort => _gsmService.DetectedPort;
        public List<HubLogEntry> Logs { get; } = new();
        public event Action? OnStatusChanged;

        public SmsHubService(HttpClient httpClient, GsmSmsService gsmService, ILogger<SmsHubService> logger)
        {
            _httpClient = httpClient;
            _gsmService = gsmService;
            _logger = logger;
        }

        private void AddHubLog(string message, string type = "INFO")
        {
            var entry = new HubLogEntry { Message = message, Type = type, Timestamp = DateTime.Now };
            lock (Logs)
            {
                Logs.Insert(0, entry);
                if (Logs.Count > 50) Logs.RemoveAt(50);
            }
            OnStatusChanged?.Invoke();
        }

        public class HubLogEntry
        {
            public string Message { get; set; } = "";
            public string Type { get; set; } = "INFO";
            public DateTime Timestamp { get; set; }
        }

        public void Start()
        {
            if (_isRunning) return;
            
            // Only run on Windows where COM ports are accessible
            if (DeviceInfo.Current.Platform != DevicePlatform.WinUI)
            {
                _logger.LogInformation("SmsHubService skipped: Not running on Windows.");
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            AddHubLog("SmsHubService started. Polling Render for pending SMS...");
            _logger.LogInformation("SmsHubService started. Polling Render for pending SMS...");
            
            Task.Run(() => PollLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _isRunning = false;
            AddHubLog("SmsHubService stopped.", "WARN");
            _logger.LogInformation("SmsHubService stopped.");
        }

        private async Task PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 0. Ensure Modem is detected (Proactive detection for Dashboard)
                    if (string.IsNullOrEmpty(ModemPort))
                    {
                        var port = await _gsmService.DetectModemAsync();
                        if (port != null)
                        {
                            AddHubLog($"GSM Modem detected on {port}", "SUCCESS");
                        }
                    }

                    // 1. Fetch pending SMS from Render
                    var response = await _httpClient.GetAsync($"{_serverBaseUrl}/api/dailyattendance/pending-sms", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var pendingItems = await response.Content.ReadFromJsonAsync<List<SmsQueueItem>>(cancellationToken: ct);
                        
                        PendingCount = pendingItems?.Count ?? 0;
                        OnStatusChanged?.Invoke();

                        if (pendingItems != null && pendingItems.Count > 0)
                        {
                            AddHubLog($"Found {pendingItems.Count} pending messages.");
                            
                            foreach (var item in pendingItems)
                            {
                                if (ct.IsCancellationRequested) break;

                                AddHubLog($"Sending to {item.PhoneNumber}...");
                                
                                // 2. Send via local GSM hardware
                                await _gsmService.SendSmsAsync(item.PhoneNumber, item.Message);
                                
                                // 3. Mark as sent on the server
                                try 
                                {
                                    var markResponse = await _httpClient.PostAsJsonAsync($"{_serverBaseUrl}/api/dailyattendance/mark-sms-sent", item.Id, ct);
                                    if (markResponse.IsSuccessStatusCode)
                                    {
                                        TotalSentCount++;
                                        AddHubLog($"Sent successfully to {item.PhoneNumber}.", "SUCCESS");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AddHubLog($"Failed to mark ID {item.Id}: {ex.Message}", "ERROR");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddHubLog($"Polling error: {ex.Message}", "WARN");
                }

                // Poll every 10 seconds
                await Task.Delay(10000, ct);
            }
        }
    }
}
