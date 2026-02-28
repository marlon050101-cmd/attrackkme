using System;

namespace NewscannerMAUI.Services
{
    public static class ApiConfig
    {
        // Change this ONE value when you move servers.
        public const string DefaultBaseUrl = "https://attrack-sr9l.onrender.com/";

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
    }
}

