using System.IO.Ports;

namespace ServerAtrrak.Services
{
    /// <summary>
    /// Sends SMS via GSM800C modem connected through a serial (COM) port.
    /// The correct port is auto-detected on first use by probing all available ports
    /// with the "AT" command and checking for an "OK" response.
    /// This service is registered as a Singleton so the detected port is cached
    /// for the lifetime of the application.
    /// </summary>
    public class GsmSmsService
    {
        private readonly ILogger<GsmSmsService> _logger;
        private string? _detectedPort = null;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _detectionLock = new SemaphoreSlim(1, 1);

        public GsmSmsService(ILogger<GsmSmsService> logger)
        {
            _logger = logger;
        }

        public async Task SendSmsAsync(string phoneNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                _logger.LogWarning("[SMS] Skipped: Phone number is empty.");
                return;
            }

            phoneNumber = NormalizePhoneNumber(phoneNumber);

            try
            {
                var port = await GetDetectedPortAsync();
                if (port == null)
                {
                    _logger.LogWarning("[SMS] No GSM modem found. SMS not sent.");
                    return;
                }

                await _lock.WaitAsync();
                try
                {
                    using var serialPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 5000,
                        WriteTimeout = 5000,
                        NewLine = "\r\n"
                    };

                    serialPort.Open();
                    _logger.LogInformation("[SMS] Port {Port} opened. Target: {Phone}", port, phoneNumber);

                    // 1. Reset/Init
                    await SendAtCommandAsync(serialPort, "AT", 500); // Wake up
                    await SendAtCommandAsync(serialPort, "ATE0", 500); // Echo off
                    await SendAtCommandAsync(serialPort, "AT+CMEE=2", 500); // Verbose error reports
                    await SendAtCommandAsync(serialPort, "AT+CMGF=1", 500); // Text mode
                    await SendAtCommandAsync(serialPort, "AT+CPMS=\"ME\",\"ME\",\"ME\"", 500);

                    // 2. Start Send
                    _logger.LogInformation("[SMS] Sending AT+CMGS...");
                    serialPort.Write($"AT+CMGS=\"{phoneNumber}\"\r");
                    
                    // Wait for the prompt '>'
                    var promptFound = false;
                    var capture = string.Empty;
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(200);
                        if (serialPort.BytesToRead > 0)
                        {
                            var chunk = serialPort.ReadExisting();
                            capture += chunk;
                            if (capture.Contains(">")) { promptFound = true; break; }
                        }
                    }

                    if (!promptFound)
                    {
                        _logger.LogWarning("[SMS] Prompt ('>') NOT found. Captured: {Capture}", capture.Replace("\r\n", " "));
                        serialPort.Close();
                        return;
                    }

                    // 3. Write message and Ctrl+Z
                    serialPort.Write(message + "\x1A");
                    _logger.LogInformation("[SMS] Payload sent. Waiting for network status...");

                    // 4. Wait for OK or +CMGS (up to 25 seconds)
                    var response = string.Empty;
                    for (int i = 0; i < 25; i++)
                    {
                        await Task.Delay(1000);
                        if (serialPort.BytesToRead > 0)
                        {
                            var chunk = serialPort.ReadExisting();
                            response += chunk;
                            _logger.LogDebug("[SMS] Modem chunk: {Chunk}", chunk.Replace("\r\n", " "));
                            if (response.Contains("OK") || response.Contains("+CMGS:")) break;
                            if (response.Contains("ERROR")) break;
                        }
                    }

                    serialPort.Close();

                    if (response.Contains("+CMGS:") || response.Contains("OK"))
                    {
                        _logger.LogInformation("[SMS] SUCCESS! Student: {StudentName}, Phone: {Phone}, Details: {Details}", 
                            message.Split(':')[1].Split('h')[0].Trim(), phoneNumber, response.Replace("\r\n", " ").Trim());
                    }
                    else
                    {
                        _logger.LogError("[SMS] FAILED to send. Phone: {Phone}, Response: {Response}", 
                            phoneNumber, response.Replace("\r\n", " ").Trim());
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SMS] CRITICAL Error during send to {Phone}. Resetting cache.", phoneNumber);
                _detectedPort = null;
            }
        }

        private async Task<string?> GetDetectedPortAsync()
        {
            if (_detectedPort != null) return _detectedPort;

            await _detectionLock.WaitAsync();
            try
            {
                if (_detectedPort != null) return _detectedPort;

                _logger.LogInformation("[SMS] Probing all COM ports for GSM800C...");
                var availablePorts = SerialPort.GetPortNames();
                
                foreach (var portName in availablePorts)
                {
                    try
                    {
                        using var testPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
                        {
                            ReadTimeout = 2000,
                            WriteTimeout = 2000
                        };

                        testPort.Open();
                        testPort.Write("AT\r");
                        await Task.Delay(800);
                        testPort.Write("AT\r"); // Second probe
                        await Task.Delay(800);

                        var response = testPort.ReadExisting();
                        testPort.Close();

                        if (response.Contains("OK"))
                        {
                            _logger.LogInformation("[SMS] GSM modem FOUND on {Port}", portName);
                            _detectedPort = portName;
                            return _detectedPort;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("[SMS] Failed probing {Port}: {Msg}", portName, ex.Message);
                    }
                }
                return null;
            }
            finally
            {
                _detectionLock.Release();
            }
        }

        public void ResetDetectedPort()
        {
            _detectedPort = null;
            _logger.LogInformation("[SMS] Port cache cleared.");
        }

        private async Task SendAtCommandAsync(SerialPort port, string command, int waitMs)
        {
            _logger.LogDebug("[SMS] Executing: {Command}", command);
            port.Write(command + "\r");
            await Task.Delay(waitMs);
            try 
            { 
                var resp = port.ReadExisting();
                if (resp.Contains("ERROR")) _logger.LogWarning("[SMS] Command {Command} returned ERROR: {Resp}", command, resp.Replace("\r\n", " "));
            } 
            catch { }
        }

        public static string BuildSmsMessage(string studentName, string attendanceType, DateTime time)
        {
            var action = attendanceType == "TimeIn" ? "arrived at" : "left";
            return $"Attrak: {studentName} has {action} school at {time:hh:mm tt}, {time:MMMM dd, yyyy}.";
        }

        private static string NormalizePhoneNumber(string phone)
        {
            phone = phone.Trim().Replace(" ", "").Replace("-", "");
            if (phone.StartsWith("09") && phone.Length == 11) return "+63" + phone.Substring(1);
            if (phone.StartsWith("9") && phone.Length == 10) return "+63" + phone;
            return phone;
        }
    }
}
