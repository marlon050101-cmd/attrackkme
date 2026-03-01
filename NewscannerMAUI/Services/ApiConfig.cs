using System;

namespace NewscannerMAUI.Services
{
    public static class ApiConfig
    {
        // Change this ONE value when you move servers.
        public const string DefaultBaseUrl = "https://attrack-sr9l.onrender.com/";
        
        // Optional: set a default modem COM port here (e.g., "COM3") if auto-detect fails.
        public const string? DefaultModemPort = null;

        /// <summary>
        /// Effective base URL. Can be overridden on the Office PC by setting
        /// environment variable: ATTRAK_API_BASE_URL
        /// </summary>
        public static string BaseUrl
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("ATTRAK_API_BASE_URL");
                var raw = string.IsNullOrWhiteSpace(env) ? DefaultBaseUrl : env.Trim();
                return EnsureTrailingSlash(raw);
            }
        }

        public static Uri BaseUri => new Uri(BaseUrl, UriKind.Absolute);

        public static string BaseUrlNoTrailingSlash => BaseUrl.TrimEnd('/');

        private static string EnsureTrailingSlash(string url) => url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";

        /// <summary>
        /// Effective GSM modem port. Can be overridden by environment variable ATTRAK_MODEM_PORT.
        /// If both env var and DefaultModemPort are null, auto-detection is used.
        /// </summary>
        public static string? ModemPort
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("ATTRAK_MODEM_PORT");
                if (!string.IsNullOrWhiteSpace(env))
                    return env.Trim();

                return string.IsNullOrWhiteSpace(DefaultModemPort) ? null : DefaultModemPort;
            }
        }
    }
}

