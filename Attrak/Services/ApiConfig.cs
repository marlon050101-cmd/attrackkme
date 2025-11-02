namespace Attrak.Services
{
    public static class ApiConfig
    {
        // Change this URL for different environments
        public static string BaseUrl { get; set; } = "";
        
        // Get full API URL for an endpoint
       
        
        // Just use ApiConfig.BaseUrl + your API path
        // Example: ApiConfig.BaseUrl + "/api/register/students"
    }
}
