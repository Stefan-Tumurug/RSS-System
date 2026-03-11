using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RSSAPI.Models;
using System.Diagnostics;

namespace RSSAPI.Controllers
{
    [ApiController]
    [Route("api/debug")]
    public class DebugController(IConfiguration configuration) : ControllerBase
    {
        private readonly IConfiguration configuration = configuration;

        [HttpGet("generate-and-validate")]
        [AllowAnonymous]
        public IActionResult GenerateAndValidateToken()
        {
            try
            {
                string jwtSecretKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key is missing in configuration");
                string jwtIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT issuer is missing in configuration");
                string jwtAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT audience is missing in configuration");

                List<Claim> jwtClaims =
                [
                    new Claim(JwtRegisteredClaimNames.Sub, "testuser"),
                    new Claim("role", "Admin"),
                    new Claim(ClaimTypes.Role, "Admin")
                ];

                SymmetricSecurityKey securityKey = new(Encoding.UTF8.GetBytes(jwtSecretKey));
                SigningCredentials signingCredentials = new(securityKey, SecurityAlgorithms.HmacSha256);

                JwtSecurityToken jwtToken = new(
                    issuer: jwtIssuer,
                    audience: jwtAudience,
                    claims: jwtClaims,
                    expires: DateTime.UtcNow.AddMinutes(30),
                    signingCredentials: signingCredentials
                );

                string generatedToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

                try
                {
                    JwtSecurityTokenHandler tokenHandler = new();
                    TokenValidationParameters validationParameters = new()
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                        ValidateIssuer = true,
                        ValidIssuer = jwtIssuer,
                        ValidateAudience = true,
                        ValidAudience = jwtAudience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };

                    ClaimsPrincipal validatedTokenPrincipal = tokenHandler.ValidateToken(generatedToken, validationParameters, out _);

                    List<JwtClaim> validatedClaims = [.. validatedTokenPrincipal.Claims.Select(claim => new JwtClaim { Type = claim.Type, Value = claim.Value })];

                    return Ok(new
                    {
                        Success = true,
                        ErrorMessage = string.Empty,
                        Item = new
                        {
                            Token = generatedToken,
                            IsValid = true,
                            Claims = validatedClaims
                        }
                    });
                }
                catch (Exception validationException)
                {
                    Debug.WriteLine($"Token validation error: {validationException.Message}");
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = validationException.Message,
                        Item = new
                        {
                            Token = generatedToken,
                            IsValid = false
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token generation error: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = $"An error occurred: {ex.Message}",
                    Item = null as object
                });
            }
        }

        [HttpGet("debugtoken")]
        [AllowAnonymous]
        public IActionResult DebugToken()
        {
            try
            {
                string? authorizationHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();

                if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                {
                    return Ok(new
                    {
                        Success = true,
                        ErrorMessage = string.Empty,
                        Item = new
                        {
                            Authenticated = false,
                            Message = "No Bearer token found in request"
                        }
                    });
                }

                string jwtToken = authorizationHeader["Bearer ".Length..];

                JwtSecurityTokenHandler tokenHandler = new();

                if (tokenHandler.ReadToken(jwtToken) is not JwtSecurityToken parsedToken)
                {
                    return Ok(new
                    {
                        Success = true,
                        ErrorMessage = string.Empty,
                        Item = new
                        {
                            Authenticated = false,
                            Message = "Invalid token format",
                            Token = jwtToken
                        }
                    });
                }

                List<JwtClaim> parsedClaims = [.. parsedToken.Claims.Select(claim => new JwtClaim { Type = claim.Type, Value = claim.Value })];

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        Authenticated = true,
                        Claims = parsedClaims,
                        parsedToken.Issuer,
                        Audience = parsedToken.Audiences,
                        Expires = parsedToken.ValidTo,
                        IsExpired = parsedToken.ValidTo < DateTime.UtcNow
                    }
                });
            }
            catch (Exception validationException)
            {
                Debug.WriteLine($"Token debugging error: {validationException.Message}");
                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        Authenticated = false,
                        Message = $"Error validating token: {validationException.Message}",
                        Token = validationException.Message
                    }
                });
            }
        }
    }
}