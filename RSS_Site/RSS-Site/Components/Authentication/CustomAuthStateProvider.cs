using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RssSite.Components.Storage;

namespace RssSite.Components.Authentication
{
    public class CustomAuthStateProvider(IJSRuntime jsRuntime, ILogger<CustomAuthStateProvider> logger) : AuthenticationStateProvider
    {
        private readonly IJSRuntime _jsRuntime = jsRuntime;
        private readonly ILogger<CustomAuthStateProvider> _logger = logger;
        private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                string? storedAuthToken = await SecureStorage.GetAuthToken(_jsRuntime);

                if (string.IsNullOrEmpty(storedAuthToken))
                {
                    _logger.LogDebug("[AUTH STATE] No token found, returning unauthenticated state");
                    _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
                    return new AuthenticationState(_currentUser);
                }

                List<Claim> parsedClaims = [.. ParseJwtToken(storedAuthToken)];
                _currentUser = new ClaimsPrincipal(new ClaimsIdentity(parsedClaims, "jwt"));
                _logger.LogDebug("[AUTH STATE] Token found with {ClaimCount} claims", parsedClaims.Count);

                return new AuthenticationState(_currentUser);
            }
            catch (Exception authenticationException)
            {
                _logger.LogError(authenticationException, "[AUTH STATE] Error getting authentication state");
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }

        public void NotifyUserAuthentication(string jwtToken)
        {
            try
            {
                if (string.IsNullOrEmpty(jwtToken))
                {
                    _logger.LogWarning("[AUTH STATE] Cannot authenticate with empty token");
                    return;
                }

                List<Claim> parsedClaims = [.. ParseJwtToken(jwtToken)];

                if (parsedClaims.Count == 0)
                {
                    _logger.LogWarning("[AUTH STATE] Token contained no claims");
                    return;
                }

                foreach (Claim claim in parsedClaims)
                {
                    _logger.LogInformation("[AUTH STATE] Claim found - Type: {Type}, Value: {Value}", claim.Type, claim.Value);
                }

                Claim? nameIDClaim = parsedClaims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);

                if (nameIDClaim == null)
                {
                    Claim? altIDClaim = parsedClaims.FirstOrDefault(c =>
                        c.Type == "sub" ||
                        c.Type == "nameid" ||
                        c.Type == "userid" ||
                        c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");

                    if (altIDClaim != null)
                    {
                        parsedClaims.Add(new Claim(ClaimTypes.NameIdentifier, altIDClaim.Value));
                        _logger.LogInformation("[AUTH STATE] Added standard NameIDentifier claim from {OriginalType}", altIDClaim.Type);
                    }

                    if (altIDClaim == null)
                    {
                        _logger.LogWarning("[AUTH STATE] Could not find a user ID claim in the token");
                    }
                }

                Claim? nameClaim = parsedClaims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

                if (nameClaim == null)
                {
                    Claim? altNameClaim = parsedClaims.FirstOrDefault(c =>
                        c.Type == "name" ||
                        c.Type == "username" ||
                        c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name");

                    if (altNameClaim != null)
                    {
                        parsedClaims.Add(new Claim(ClaimTypes.Name, altNameClaim.Value));
                        _logger.LogInformation("[AUTH STATE] Added standard Name claim from {OriginalType}", altNameClaim.Type);
                    }

                    if (altNameClaim == null && nameIDClaim != null)
                    {
                        parsedClaims.Add(new Claim(ClaimTypes.Name, nameIDClaim.Value));
                        _logger.LogInformation("[AUTH STATE] Added Name claim using NameIDentifier");
                    }
                }

                Claim? roleClaim = parsedClaims.FirstOrDefault(c => c.Type == ClaimTypes.Role);

                if (roleClaim == null)
                {
                    Claim? altRoleClaim = parsedClaims.FirstOrDefault(c =>
                        c.Type == "role" ||
                        c.Type == "roles" ||
                        c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");

                    if (altRoleClaim != null)
                    {
                        parsedClaims.Add(new Claim(ClaimTypes.Role, altRoleClaim.Value));
                        _logger.LogInformation("[AUTH STATE] Added standard Role claim from {OriginalType}", altRoleClaim.Type);
                    }
                }

                _currentUser = new ClaimsPrincipal(new ClaimsIdentity(parsedClaims, "jwt"));

                string? userID = parsedClaims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value;
                IEnumerable<string> userRoles = parsedClaims
                    .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
                    .Select(claim => claim.Value);

                _logger.LogInformation("[AUTH STATE] Authentication state updated: UserID={UserID}, Roles={Roles}",
                    userID, string.Join(", ", userRoles));

                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
            }
            catch (Exception authenticationException)
            {
                _logger.LogError(authenticationException, "[AUTH STATE] Error processing authentication token");
            }
        }

        public void NotifyUserLogout()
        {
            _logger.LogInformation("[AUTH STATE] User logged out");
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        }

        private IEnumerable<Claim> ParseJwtToken(string jwtToken)
        {
            try
            {
                System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler tokenHandler = new();
                System.IdentityModel.Tokens.Jwt.JwtSecurityToken parsedJwtToken = tokenHandler.ReadJwtToken(jwtToken);
                IEnumerable<Claim> claims = parsedJwtToken.Claims;
                _logger.LogDebug("[AUTH STATE] JWT Token contains {ClaimCount} claims", claims.Count());

                foreach (Claim claim in claims)
                {
                    _logger.LogDebug("[AUTH STATE] JWT Token claim: {Type} = {Value}", claim.Type, claim.Value);
                }

                return claims;
            }
            catch (Exception tokenParsingException)
            {
                _logger.LogError(tokenParsingException, "[AUTH STATE] Error parsing JWT token");
                return [];
            }
        }
    }
}