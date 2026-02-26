using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace NewscannerMAUI.Services
{
    public class GsmSmsService
    {
        private readonly ILogger<GsmSmsService> _logger;
        private string? _detectedPort = null;
        public string? DetectedPort => _detectedPort;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _detectionLock = new SemaphoreSlim(1, 1);

        public GsmSmsService(ILogger<GsmSmsService> logger)
        {
            _logger = logger;
        }

        public async Task<string?> DetectModemAsync()
        {
            return await GetDetectedPortAsync();
        }

        public async Task SendSmsAsync(string phoneNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return;
            phoneNumber = NormalizePhoneNumber(phoneNumber);

            try
            {
                var port = await GetDetectedPortAsync();
                if (port == null)
                {
                    _logger.LogWarning("[SMS] No GSM modem found.");
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
                    
                    // Init
                    await SendAtCommandAsync(serialPort, "AT", 500);
                    await SendAtCommandAsync(serialPort, "ATE0", 500);
                    await SendAtCommandAsync(serialPort, "AT+CMGF=1", 500);

                    // Send
                    serialPort.Write($"AT+CMGS=\"{phoneNumber}\"\r");
                    
                    var promptFound = false;
                    var capture = string.Empty;
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(200);
                        if (serialPort.BytesToRead > 0)
                        {
                            capture += serialPort.ReadExisting();
                            if (capture.Contains(">")) { promptFound = true; break; }
                        }
                    }

                    if (!promptFound)
                    {
                        serialPort.Close();
                        return;
                    }

                    serialPort.Write(message + "\x1A");
                    
                    var response = string.Empty;
                    for (int i = 0; i < 25; i++)
                    {
                        await Task.Delay(1000);
                        if (serialPort.BytesToRead > 0)
                        {
                            response += serialPort.ReadExisting();
                            if (response.Contains("OK") || response.Contains("+CMGS:")) break;
                        }
                    }
                    serialPort.Close();
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SMS] Error sending SMS");
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
                var availablePorts = SerialPort.GetPortNames();
                foreach (var portName in availablePorts)
                {
                    try
                    {
                        using var testPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One) { ReadTimeout = 1500 };
                        testPort.Open();
                        testPort.Write("AT\r");
                        await Task.Delay(500);
                        var response = testPort.ReadExisting();
                        testPort.Close();
                        if (response.Contains("OK"))
                        {
                            _detectedPort = portName;
                            return _detectedPort;
                        }
                    }
                    catch { }
                }
                return null;
            }
            finally { _detectionLock.Release(); }
        }

        private async Task SendAtCommandAsync(SerialPort port, string command, int waitMs)
        {
            port.Write(command + "\r");
            await Task.Delay(waitMs);
            try { port.ReadExisting(); } catch { }
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
