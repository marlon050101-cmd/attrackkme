using Microsoft.AspNetCore.Mvc;
using AttrackSharedClass.Models;
using ServerAtrrak.Services;

namespace ServerAtrrak.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new LoginResponse
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                var result = await _authService.LoginAsync(request);
                
                if (result.Success)
                {
                    _logger.LogInformation("Successful login for user: {Username}", request.Username);
                    return Ok(result);
                }
                else
                {
                    _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
                    return Unauthorized(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
                return StatusCode(500, new LoginResponse
                {
                    Success = false,
                    Message = "An internal server error occurred"
                });
            }
        }

        [HttpPost("validate")]
        public async Task<ActionResult<bool>> ValidateUser([FromBody] LoginRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(false);
                }

                var user = await _authService.GetUserByUsernameAsync(request.Username);
                
                if (user == null || !user.IsActive)
                {
                    return Ok(false);
                }

                var isValid = await _authService.ValidatePasswordAsync(request.Password, user.Password);
                return Ok(isValid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user validation for user: {Username}", request.Username);
                return StatusCode(500, false);
            }
        }

        [HttpPut("profile/{userId}")]
        public async Task<ActionResult> UpdateProfile(string userId, [FromBody] UpdateProfileRequest request)
        {
            try
            {
                if (userId != request.UserId)
                {
                    return BadRequest(new { message = "UserId mismatch" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { message = "Invalid request data" });
                }

                var (success, message) = await _authService.UpdateUserProfileAsync(request);
                
                if (success)
                {
                    return Ok(new { message });
                }
                else
                {
                    return BadRequest(new { message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile for user: {UserId}", userId);
                return StatusCode(500, new { message = "An internal server error occurred", error = ex.Message });
            }
        }
    }
}
