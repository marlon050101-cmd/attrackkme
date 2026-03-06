using AttrackSharedClass.Models;

namespace ServerAtrrak.Services
{
    public interface IAuthService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<bool> ValidatePasswordAsync(string password, string storedPassword);
        Task UpdateLastLoginAsync(string userId);
        Task<(bool success, string message)> UpdateUserProfileAsync(UpdateProfileRequest request);
    }
}
