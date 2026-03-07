using AttrackSharedClass.Models;
using System.Text.Json;
using System.Net.Http.Json;

namespace NewscannerMAUI.Services
{
    public class AuthService
    {
        private UserInfo? _currentUser;
        private readonly OfflineDataService _offlineDataService;
        private readonly IHttpClientFactory _httpClientFactory;
        private const string USER_KEY = "current_user";

        public bool IsAuthenticated => _currentUser != null;
        public bool IsOfflineMode { get; private set; } = false;
        public event Action<bool> AuthenticationStateChanged = delegate { };

        public AuthService(OfflineDataService offlineDataService, IHttpClientFactory httpClientFactory)
        {
            _offlineDataService = offlineDataService;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<UserInfo?> GetCurrentUserAsync()
        {
            return await Task.FromResult(_currentUser);
        }

        public async Task<TeacherInfo?> GetCurrentTeacherAsync()
        {
            var user = await GetCurrentUserAsync();
            // Support both SubjectTeacher and Adviser accounts — both have TeacherId and can use the scanner
            if ((user?.UserType == UserType.SubjectTeacher || user?.UserType == UserType.Adviser)
                && !string.IsNullOrEmpty(user.TeacherId))
            {
                return new TeacherInfo
                {
                    TeacherId = user.TeacherId,
                    FullName = user.FullName ?? user.Username,
                    Email = user.Email,
                    SchoolId = user.SchoolId ?? "",
                    SchoolName = user.SchoolId ?? "School",
                    GradeLevel = user.GradeLevel ?? 0,
                    Section = user.Section ?? "",
                    Strand = user.Strand
                };
            }
            return null;
        }

        public void SetCurrentUser(UserInfo user)
        {
            _currentUser = user;
            SaveUserToStorage();
            AuthenticationStateChanged.Invoke(true);
        }

        public void Logout()
        {
            _currentUser = null;
            ClearUserFromStorage();
            AuthenticationStateChanged.Invoke(false);
        }

        private void SaveUserToStorage()
        {
            if (_currentUser != null)
            {
                var userJson = JsonSerializer.Serialize(_currentUser);
                // In a real app, you would save to secure storage
                // For now, we'll just keep it in memory
            }
        }

        private void LoadUserFromStorage()
        {
            // In a real app, you would load from secure storage
            // For now, we'll start with no user
            _currentUser = null;
        }

        private void ClearUserFromStorage()
        {
            // Clear from storage
        }

        // Offline Authentication Methods
        public async Task<bool> LoginOfflineAsync(string username, string password)
        {
            try
            {
                var isAuthenticated = await _offlineDataService.AuthenticateUserOfflineAsync(username, password);
                
                if (isAuthenticated)
                {
                    IsOfflineMode = true;
                    
                    // Create a basic user info for offline mode
                    _currentUser = new UserInfo
                    {
                        Username = username,
                        UserType = UserType.SubjectTeacher, // Default to teacher for offline mode
                        Email = $"{username}@offline.local",
                        IsActive = true
                    };
                    
                    SaveUserToStorage();
                    AuthenticationStateChanged.Invoke(true);
                    
                    System.Diagnostics.Debug.WriteLine($"Offline login successful for user: {username}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in offline login: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> LoginOnlineAsync(string username, string password, string serverUrl)
        {
            try
            {
                // Check if server is reachable
                // Note: Connection checking is now handled by HybridQRValidationService
                // For now, try online authentication first

                // Make API call to server for authentication
                var httpClient = _httpClientFactory.CreateClient("AttrakAPI");
                var loginRequest = new
                {
                    Username = username,
                    Password = password
                };

                var response = await httpClient.PostAsJsonAsync("api/auth/login", loginRequest);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    var userInfo = result?.User;
                    
                    if (userInfo != null)
                    {
                        _currentUser = userInfo;
                        IsOfflineMode = false;
                        SaveUserToStorage();
                        AuthenticationStateChanged.Invoke(true);
                        
                        // Download all students and class offerings for offline use
                        if ((userInfo.UserType == UserType.SubjectTeacher || userInfo.UserType == UserType.Adviser) && !string.IsNullOrEmpty(userInfo.TeacherId))
                        {
                            System.Diagnostics.Debug.WriteLine($"Downloading data for teacher/adviser: {userInfo.TeacherId}");
                            _ = Task.Run(async () => 
                            {
                                try
                                {
                                    bool isAdviser = userInfo.UserType == UserType.Adviser;
                                    
                                    // 1. Download students for offline scanning
                                    var success = await _offlineDataService.DownloadAllStudentsForTeacherAsync(userInfo.TeacherId, serverUrl);
                                    System.Diagnostics.Debug.WriteLine($"Student download {(success ? "successful" : "failed")} for {userInfo.TeacherId}");
                                    
                                    // 2. Download class offerings for offline subject selection
                                    await _offlineDataService.DownloadAndCacheClassOfferingsAsync(userInfo.TeacherId, serverUrl, isAdviser);
                                    System.Diagnostics.Debug.WriteLine($"Class offerings cached for {(isAdviser ? "adviser" : "teacher")} {userInfo.TeacherId}");
                                    
                                    // 3. Store credentials for offline login
                                    if (success)
                                    {
                                        var userTypeStr = isAdviser ? "Adviser" : "SubjectTeacher";
                                        await _offlineDataService.AddOfflineUserAsync(username, password, userTypeStr, userInfo.FullName ?? userInfo.Username);
                                        System.Diagnostics.Debug.WriteLine($"Credentials stored for offline login: {username}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error pre-loading offline data: {ex.Message}");
                                }
                            });
                        }

                        
                        System.Diagnostics.Debug.WriteLine($"Online login successful for user: {username}");
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in online login: {ex.Message}");
                // Fallback to offline mode
                return await LoginOfflineAsync(username, password);
            }
        }

        public async Task<bool> SetupOfflineUserAsync(string username, string password, string userType, string fullName)
        {
            try
            {
                var success = await _offlineDataService.AddOfflineUserAsync(username, password, userType, fullName);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"Offline user setup successful: {username}");
                }
                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting up offline user: {ex.Message}");
                return false;
            }
        }

        public string GetConnectionStatus()
        {
            return "Connection status handled by HybridQRValidationService";
        }

        public bool IsOnline()
        {
            // Connection status is now handled by HybridQRValidationService
            return true; // Default to true, actual status checked by HybridQRValidationService
        }
    }
}
