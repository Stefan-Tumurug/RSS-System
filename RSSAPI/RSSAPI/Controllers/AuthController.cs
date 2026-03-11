using Microsoft.AspNetCore.Mvc;
using RSSAPI.Models;
using RSSAPI.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Diagnostics;

namespace RSSAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController(UserService userService, IConfiguration configuration) : ControllerBase
    {
        private readonly UserService userService = userService;
        private readonly IConfiguration configuration = configuration;

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {
            try
            {
                (bool isAuthenticated, string jwtToken, string userRole, string authenticationErrorMessage) =
                    await userService.AuthenticateUserAsync(loginRequest.Username, loginRequest.Password);

                if (!isAuthenticated)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = authenticationErrorMessage,
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        Message = "Login successful",
                        Token = jwtToken,
                        Role = userRole
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Login error: {ex.Message}");

                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred during login.",
                    Item = null as object
                });
            }
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            try
            {
                Response.Cookies.Append("AuthToken", "", new CookieOptions
                {
                    Expires = DateTime.UtcNow.AddDays(-1),
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict
                });

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "Logged out successfully"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Logout error: {ex.Message}");

                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred during logout.",
                    Item = null as object
                });
            }
        }

        [HttpGet("token/validate")]
        [AllowAnonymous]
        public IActionResult ValidateTokenEndpoint()
        {
            try
            {
                string? authorizationHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
                if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Bearer token not found",
                        Item = null as object
                    });
                }

                string jwtToken = authorizationHeader["Bearer ".Length..];
                JwtSecurityTokenHandler tokenHandler = new();

                if (tokenHandler.ReadToken(jwtToken) is not JwtSecurityToken parsedToken)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Invalid token format",
                        Item = null as object
                    });
                }

                List<JwtClaim> tokenClaims = [.. parsedToken.Claims.Select(claim => new JwtClaim { Type = claim.Type, Value = claim.Value })];

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        Valid = true,
                        Claims = tokenClaims,
                        parsedToken.Issuer,
                        Audience = parsedToken.Audiences,
                        Expires = parsedToken.ValidTo
                    }
                });
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"Token validation error: {ex.Message}");

                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = $"Error validating token: {ex.Message}",
                    Item = null as object
                });
            }
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("validate")]
        public IActionResult ValidateToken()
        {
            try
            {
                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "Token is valid"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token validation error: {ex.Message}");

                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred during token validation.",
                    Item = null as object
                });
            }
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("protected")]
        public IActionResult ProtectedRoute()
        {
            try
            {
                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "You are authenticated!"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Protected route error: {ex.Message}");

                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred.",
                    Item = null as object
                });
            }
        }

        public class LoginRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}