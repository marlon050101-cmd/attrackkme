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

        public GsmSmsService(ILogger<GsmSmsService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Sends an SMS to the given phone number using the GSM800C module.
        /// Fires and forgets — never throws; all errors are logged.
        /// </summary>
        public async Task SendSmsAsync(string phoneNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                _logger.LogWarning("[SMS] Skipped: Phone number is empty.");
                return;
            }

            // Normalize Philippine numbers: 09XXXXXXXXX → +639XXXXXXXXX
            phoneNumber = NormalizePhoneNumber(phoneNumber);

            try
            {
                var port = await GetDetectedPortAsync();
                if (port == null)
                {
                    _logger.LogWarning("[SMS] No GSM modem found on any COM port. SMS not sent.");
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
                    _logger.LogInformation("[SMS] Port {Port} opened. Sending to {Phone}...", port, phoneNumber);

                    // Set text mode
                    SendAtCommand(serialPort, "AT+CMGF=1", 1000);

                    // Set recipient
                    SendAtCommand(serialPort, $"AT+CMGS=\"{phoneNumber}\"", 2000);

                    // Write message and terminate with Ctrl+Z (0x1A)
                    serialPort.Write(message + "\x1A");

                    // Wait for response (up to 10 seconds for network)
                    await Task.Delay(10000);

                    var response = string.Empty;
                    try { response = serialPort.ReadExisting(); } catch { }

                    serialPort.Close();

                    if (response.Contains("+CMGS:"))
                    {
                        _logger.LogInformation("[SMS] Send SUCCESS to {Phone}. Response: {Response}", phoneNumber, response);
                    }
                    else
                    {
                        _logger.LogWarning("[SMS] Send may have failed to {Phone}. Response: {Response}", phoneNumber, response);
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SMS] Error sending SMS to {Phone}. Resetting COM port cache.", phoneNumber);
                _detectedPort = null; // Reset cache so it re-detects next time
            }

        }

        /// <summary>
        /// Returns cached detected port, or probes all COM ports to find the GSM modem.
        /// Returns null if no modem is found.
        /// </summary>
        private async Task<string?> GetDetectedPortAsync()
        {
            if (_detectedPort != null)
                return _detectedPort;

            _logger.LogInformation("[SMS] Auto-detecting GSM modem COM port...");

            var availablePorts = SerialPort.GetPortNames();
            _logger.LogInformation("[SMS] Available COM ports: {Ports}", string.Join(", ", availablePorts));

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
                    testPort.WriteLine("AT");
                    await Task.Delay(1000);

                    var response = string.Empty;
                    try { response = testPort.ReadExisting(); } catch { }

                    testPort.Close();

                    if (response.Contains("OK"))
                    {
                        _logger.LogInformation("[SMS] GSM modem detected on port: {Port}", portName);
                        _detectedPort = portName;
                        return _detectedPort;
                    }
                    else
                    {
                        _logger.LogDebug("[SMS] Port {Port} did not respond with OK. Response: {Response}", portName, response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("[SMS] Port {Port} probe failed: {Error}", portName, ex.Message);
                }
            }

            _logger.LogWarning("[SMS] No GSM modem found on any available COM port.");
            return null;
        }

        /// <summary>
        /// Forces re-detection of the COM port on next send (call if the modem was reconnected).
        /// </summary>
        public void ResetDetectedPort()
        {
            _detectedPort = null;
            _logger.LogInformation("[SMS] COM port detection reset. Will auto-detect on next send.");
        }

        private void SendAtCommand(SerialPort port, string command, int waitMs)
        {
            port.WriteLine(command);
            Thread.Sleep(waitMs);
            try { port.ReadExisting(); } catch { }
        }

        /// <summary>
        /// Builds the SMS message sent to the parent.
        /// </summary>
        public static string BuildSmsMessage(string studentName, string attendanceType, DateTime time)
        {
            var action = attendanceType == "TimeIn"
                ? "arrived at school"
                : "left school";

            return $"Attrak: {studentName} has {action} at {time:hh:mm tt}, {time:MMMM dd, yyyy}.";
        }

        /// <summary>
        /// Normalizes Philippine phone numbers to international format for GSM AT commands.
        /// 09XXXXXXXXX → +639XXXXXXXXX
        /// </summary>
        private static string NormalizePhoneNumber(string phone)
        {
            phone = phone.Trim().Replace(" ", "").Replace("-", "");

            if (phone.StartsWith("09") && phone.Length == 11)
                return "+63" + phone.Substring(1); // 09XX → +639XX

            if (phone.StartsWith("9") && phone.Length == 10)
                return "+63" + phone; // 9XX → +639XX

            return phone; // Already in international format or unknown format
        }
    }
}
