using AttrackSharedClass.Models;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Attrak.Services
{
    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _jsRuntime;
        private UserInfo? _currentUser;
        private const string USER_KEY = "current_user";

        public bool IsAuthenticated => _currentUser != null;
        public event Action<bool> AuthenticationStateChanged = delegate { };

        public AuthService(HttpClient httpClient, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _jsRuntime = jsRuntime;
            // Don't load user from storage - always start fresh
            _currentUser = null;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                Console.WriteLine($"Attempting login for user: {request.Username}");
                Console.WriteLine($"API URL: {ApiConfig.BaseUrl}api/auth/login");
                
                var response = await _httpClient.PostAsJsonAsync($"{ApiConfig.BaseUrl}api/auth/login", request);
                
                Console.WriteLine($"Response Status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    Console.WriteLine($"Login Response: {JsonSerializer.Serialize(loginResponse)}");
                    
                    if (loginResponse?.Success == true && loginResponse.User != null)
                    {
                        _currentUser = loginResponse.User;
                        // Don't save to storage - session only lasts while app is open
                        AuthenticationStateChanged.Invoke(true);
                    }
                    return loginResponse ?? new LoginResponse { Success = false, Message = "Login failed" };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error Response: {errorContent}");
                    
                    var errorResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    return errorResponse ?? new LoginResponse { Success = false, Message = $"Login failed with status: {response.StatusCode}" };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during login: {ex.Message}");
                return new LoginResponse { Success = false, Message = $"Network error occurred: {ex.Message}" };
            }
        }

        public async Task LogoutAsync()
        {
            _currentUser = null;
            // Clear any existing storage data
            await ClearUserFromStorageAsync();
            AuthenticationStateChanged.Invoke(false);
        }

        public async Task ClearSessionAsync()
        {
            _currentUser = null;
            // Clear any existing storage data
            await ClearUserFromStorageAsync();
            AuthenticationStateChanged.Invoke(false);
        }

        public Task<UserInfo?> GetCurrentUserAsync()
        {
            // Don't load from storage - always return current session only
            return Task.FromResult(_currentUser);
        }

        private async Task LoadUserFromStorageAsync()
        {
            try
            {
                var userJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", USER_KEY);
                if (!string.IsNullOrEmpty(userJson))
                {
                    var savedUser = JsonSerializer.Deserialize<UserInfo>(userJson);
                    
                    // Check if the saved session is still valid (not expired)
                    if (savedUser != null && IsSessionValid(savedUser))
                    {
                        _currentUser = savedUser;
                    }
                    else
                    {
                        // Clear invalid session
                        await ClearUserFromStorageAsync();
                        _currentUser = null;
                    }
                }
            }
            catch
            {
                _currentUser = null;
                await ClearUserFromStorageAsync();
            }
        }

        private bool IsSessionValid(UserInfo user)
        {
            // Check if user has a valid last login time (within last 24 hours)
            if (user.LastLoginAt.HasValue)
            {
                var timeSinceLogin = DateTime.Now - user.LastLoginAt.Value;
                return timeSinceLogin.TotalHours < 24; // Session expires after 24 hours
            }
            return false;
        }

        private async Task ClearUserFromStorageAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", USER_KEY);
            }
            catch
            {
                // Ignore errors when clearing storage
            }
        }



    }
}
