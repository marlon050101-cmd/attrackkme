using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using AttrackSharedClass.Models;
using Microsoft.Extensions.Http;
using Microsoft.AspNetCore.SignalR.Client;

namespace NewscannerMAUI.Services
{
    /// <summary>
    /// Service that polls the cloud server (Render) for pending SMS messages
    /// and dispatches them using the local GSM modem.
    /// This effectively turns the Windows MAUI app into an SMS Gateway.
    /// </summary>
    public class SmsHubService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly GsmSmsService _gsmService;
        private readonly ILogger<SmsHubService> _logger;
        private readonly SemaphoreSlim _dispatchLock = new SemaphoreSlim(1, 1);
        private HubConnection? _hubConnection;
        private bool _isRunning = false;
        private CancellationTokenSource? _cts;
        private DateTime _lastHeartbeatLogAtUtc = DateTime.MinValue;

        // Stats for Dashboard
        public int PendingCount { get; private set; }
        public int TotalSentCount { get; private set; }
        public string? ModemPort => _gsmService.DetectedPort;
        public List<HubLogEntry> Logs { get; } = new();
        public bool IsRunning => _isRunning;
        public event Action? OnStatusChanged;

        // Diagnostics (helps confirm polling is working)
        public DateTime? LastPollAtLocal { get; private set; }
        public string LastPendingFetchStatus { get; private set; } = "Never";
        public string ApiBaseUrl => ApiConfig.BaseUrl;
        public string RealtimeStatus { get; private set; } = "Not connected";
        public DateTime? LastRealtimeSignalAtLocal { get; private set; }

        public SmsHubService(IHttpClientFactory httpClientFactory, GsmSmsService gsmService, ILogger<SmsHubService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _gsmService = gsmService;
            _logger = logger;
        }

        /// <summary>
        /// Manually trigger a modem rescan. This allows the operator to
        /// move the GSM device to another USB/COM port without restarting
        /// the entire app.
        /// </summary>
        public async Task<string?> RefreshModemAsync()
        {
            var port = await _gsmService.RefreshAndDetectModemAsync();

            if (port != null)
            {
                AddHubLog($"GSM Modem re-detected on {port}", "SUCCESS");
            }
            else
            {
                AddHubLog("GSM Modem not detected on any COM port.", "WARN");
            }

            OnStatusChanged?.Invoke();
            return port;
        }

        /// <summary>
        /// Force an immediate check to the server (pending-sms endpoint)
        /// using the existing SmsHubService pipeline. Helpful when the
        /// server was temporarily slow or unreachable and the user wants
        /// to retry without waiting for the next automatic poll.
        /// </summary>
        public async Task ManualRefreshAsync()
        {
            if (!_isRunning)
            {
                AddHubLog("Manual refresh ignored because service is stopped.", "WARN");
                OnStatusChanged?.Invoke();
                return;
            }

            AddHubLog("Manual server refresh requested...", "INFO");
            var token = _cts?.Token ?? CancellationToken.None;
            await DispatchPendingOnceAsync("manual", token);
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
            
            AddHubLog("SmsHubService started. Realtime (SignalR) + fallback polling enabled...");
            _logger.LogInformation("SmsHubService started. Polling Render for pending SMS...");
            OnStatusChanged?.Invoke();
            
            Task.Run(() => StartRealtimeAsync(_cts.Token));
            Task.Run(() => FallbackPollLoop(_cts.Token));
            _ = Task.Run(() => DispatchPendingOnceAsync("startup", _cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _isRunning = false;
            AddHubLog("SmsHubService stopped.", "WARN");
            _logger.LogInformation("SmsHubService stopped.");
            OnStatusChanged?.Invoke();

            _ = Task.Run(async () =>
            {
                try { if (_hubConnection != null) await _hubConnection.StopAsync(); } catch { }
                try { if (_hubConnection != null) await _hubConnection.DisposeAsync(); } catch { }
            });
        }

        private async Task StartRealtimeAsync(CancellationToken ct)
        {
            try
            {
                var hubUrl = new Uri(ApiConfig.BaseUri, "hubs/smsqueue");

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        // Keep same "SSL bypass" behavior as your HttpClient handler
                        options.HttpMessageHandlerFactory = _ =>
                        {
                            var handler = new HttpClientHandler();
                            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                            return handler;
                        };
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.Reconnecting += error =>
                {
                    RealtimeStatus = "Reconnecting...";
                    OnStatusChanged?.Invoke();
                    AddHubLog($"SignalR reconnecting: {error?.Message}", "WARN");
                    return Task.CompletedTask;
                };

                _hubConnection.Reconnected += async _ =>
                {
                    RealtimeStatus = "Connected";
                    OnStatusChanged?.Invoke();
                    AddHubLog("SignalR reconnected.", "SUCCESS");
                    try { await _hubConnection.InvokeAsync("JoinSmsHub", ct); } catch { }
                };

                _hubConnection.Closed += error =>
                {
                    RealtimeStatus = "Disconnected";
                    OnStatusChanged?.Invoke();
                    AddHubLog($"SignalR closed: {error?.Message}", "WARN");
                    return Task.CompletedTask;
                };

                _hubConnection.On<object>("SmsQueueChanged", payload =>
                {
                    LastRealtimeSignalAtLocal = DateTime.Now;
                    RealtimeStatus = "Connected";
                    OnStatusChanged?.Invoke();
                    _ = DispatchPendingOnceAsync("signalr", ct);
                });

                RealtimeStatus = "Connecting...";
                OnStatusChanged?.Invoke();
                await _hubConnection.StartAsync(ct);
                RealtimeStatus = "Connected";
                OnStatusChanged?.Invoke();

                AddHubLog($"SignalR connected: {hubUrl}", "SUCCESS");
                try { await _hubConnection.InvokeAsync("JoinSmsHub", ct); } catch { }
            }
            catch (Exception ex)
            {
                RealtimeStatus = $"Failed: {ex.Message}";
                OnStatusChanged?.Invoke();
                AddHubLog($"SignalR start failed: {ex.Message}", "ERROR");
            }
        }

        private async Task FallbackPollLoop(CancellationToken ct)
        {
            // If SignalR is blocked by hosting/proxy, this still guarantees delivery.
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await DispatchPendingOnceAsync("fallback", ct);
                }
                catch { }

                // Slow fallback to reduce load; SignalR should handle realtime.
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }

        private async Task DispatchPendingOnceAsync(string reason, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (!await _dispatchLock.WaitAsync(0, ct)) return;

            try
            {
                LastPollAtLocal = DateTime.Now;
                OnStatusChanged?.Invoke();

                // Ensure Modem is detected (Proactive detection for Dashboard)
                if (string.IsNullOrEmpty(ModemPort))
                {
                    var port = await _gsmService.DetectModemAsync();
                    if (port != null)
                    {
                        AddHubLog($"GSM Modem detected on {port}", "SUCCESS");
                    }
                }

                var client = _httpClientFactory.CreateClient("AttrakAPI");
                var pendingUrl = new Uri(ApiConfig.BaseUri, "api/dailyattendance/pending-sms");
                var response = await client.GetAsync(pendingUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response, ct);
                    LastPendingFetchStatus = $"HTTP {(int)response.StatusCode}: {body}";
                    OnStatusChanged?.Invoke();
                    AddHubLog($"pending-sms HTTP {(int)response.StatusCode}: {body}", "WARN");
                    return;
                }

                var pendingItems = await response.Content.ReadFromJsonAsync<List<SmsQueueItem>>(cancellationToken: ct);
                PendingCount = pendingItems?.Count ?? 0;
                LastPendingFetchStatus = $"OK ({(int)response.StatusCode}) Count={PendingCount} via {reason}";
                OnStatusChanged?.Invoke();

                if (PendingCount == 0)
                {
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - _lastHeartbeatLogAtUtc) > TimeSpan.FromSeconds(60))
                    {
                        _lastHeartbeatLogAtUtc = nowUtc;
                        AddHubLog("No pending messages.", "INFO");
                    }
                    return;
                }

                AddHubLog($"Dispatch ({reason}): Found {PendingCount} pending.", "INFO");

                foreach (var item in pendingItems!)
                {
                    if (ct.IsCancellationRequested) break;
                    AddHubLog($"Sending to {item.PhoneNumber}...");
                    await _gsmService.SendSmsAsync(item.PhoneNumber, item.Message);

                    try
                    {
                        var markUrl = new Uri(ApiConfig.BaseUri, "api/dailyattendance/mark-sms-sent");
                        var markResponse = await client.PostAsJsonAsync(markUrl, item.Id, ct);
                        if (markResponse.IsSuccessStatusCode)
                        {
                            TotalSentCount++;
                            AddHubLog($"Sent successfully to {item.PhoneNumber}.", "SUCCESS");
                        }
                        else
                        {
                            var body = await SafeReadBodyAsync(markResponse, ct);
                            AddHubLog($"Mark-sent failed ({(int)markResponse.StatusCode}): {body}", "WARN");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddHubLog($"Failed to mark ID {item.Id}: {ex.Message}", "ERROR");
                    }
                }
            }
            catch (Exception ex)
            {
                LastPendingFetchStatus = $"EX ({reason}): {ex.Message}";
                OnStatusChanged?.Invoke();
                AddHubLog($"Dispatch error ({reason}): {ex.Message}", "ERROR");
            }
            finally
            {
                _dispatchLock.Release();
            }
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(text)) return "(empty)";
                return text.Length > 200 ? text.Substring(0, 200) + "..." : text;
            }
            catch
            {
                return "(unreadable body)";
            }
        }
    }
}
