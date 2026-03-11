using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RssSite.Components.Models;
using RssSite.Components.Storage;
namespace RssSite.Components.Authentication
{
    public class AuthService(
        HttpClient httpClient,
        IJSRuntime jsRuntime,
        NavigationManager navigationManager,
        AuthenticationStateProvider authenticationStateProvider,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly IJSRuntime _jsRuntime = jsRuntime;
        private readonly ILogger<AuthService> _logger = logger;
        private readonly NavigationManager _navigationManager = navigationManager;
        private readonly AuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider;
        private readonly IConfiguration _configuration = configuration;
        private readonly SemaphoreSlim _loginSemaphore = new(1, 1);

        public async Task<(bool Success, string? ErrorMessage)> ProcessLoginAsync(
            string username,
            string password,
            bool rememberMe)
        {
            await _loginSemaphore.WaitAsync();

            try
            {
                _logger.LogInformation("[AUTH] Processing login for user: {Username}", username);
                _httpClient.DefaultRequestHeaders.Authorization = null;

                LoginRequest loginRequest = new() { Username = username, Password = password };
                string jsonPayload = JsonSerializer.Serialize(loginRequest);
                StringContent requestContent = new(jsonPayload, Encoding.UTF8, "application/json");
                string loginUrl = $"{_configuration["ApiSettings:BaseUrl"]}/api/auth/login";

                HttpResponseMessage loginResponse;
                try
                {
                    loginResponse = await _httpClient.PostAsync(loginUrl, requestContent);
                }
                catch (Exception exception)
                {
                    _logger.LogError("[AUTH] Exception during login request: {Message}", exception.Message);
                    return (false, $"Connection error: {exception.Message}");
                }

                string loginResponseContent = await loginResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("[AUTH] Login response status: {Status}, content: {Content}",
                    loginResponse.StatusCode, loginResponseContent);

                return loginResponse.IsSuccessStatusCode
                    ? await ProcessSuccessfulLogin(loginResponseContent, rememberMe)
                    : (false, $"Server error: {loginResponse.StatusCode}");
            }
            catch (Exception loginException)
            {
                _logger.LogError(loginException, "[AUTH] Unhandled exception during login process");
                return (false, $"Login error: {loginException.Message}");
            }
            finally
            {
                _loginSemaphore.Release();
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> ProcessSuccessfulLogin(
            string loginResponseContent,
            bool rememberMe)
        {
            JsonSerializerOptions jsonOptions = new()
            {
                PropertyNameCaseInsensitive = true
            };

            ApiResponse<AuthResponseItem>? responseWrapper = JsonSerializer.Deserialize<ApiResponse<AuthResponseItem>>(
                loginResponseContent,
                jsonOptions
            );

            if (responseWrapper == null)
            {
                _logger.LogWarning("[AUTH] Deserialization returned null response wrapper");
                return (false, "Invalid login response");
            }

            if (!responseWrapper.Success)
            {
                _logger.LogWarning("[AUTH] Invalid auth response: {Response}", loginResponseContent);
                return (false, responseWrapper.ErrorMessage ?? "Invalid login response");
            }

            AuthResponseItem? authItem = responseWrapper.Item;

            if (authItem == null)
            {
                _logger.LogWarning("[AUTH] No auth item in response");
                return (false, "No authentication details received");
            }

            if (string.IsNullOrEmpty(authItem.Token))
            {
                return (false, "Invalid token received");
            }

            bool isTokenStored = await SecureStorage.StoreAuthToken(_jsRuntime, authItem.Token, rememberMe);

            if (!isTokenStored)
            {
                _logger.LogError("[AUTH] Failed to store authentication token");
                return (false, "Failed to store authentication token");
            }

            string storageType = rememberMe ? "localStorage" : "sessionStorage";
            await _jsRuntime.InvokeVoidAsync($"{storageType}.setItem", "userRole", authItem.Role);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authItem.Token);

            ((CustomAuthStateProvider)_authenticationStateProvider).NotifyUserAuthentication(authItem.Token);

            await Task.Delay(100);

            AuthenticationState authenticationState = await _authenticationStateProvider.GetAuthenticationStateAsync();

            if (authenticationState.User.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning("[AUTH] Authentication state not updated properly after login");
                return (false, "Authentication state update failed");
            }

            _logger.LogInformation("[AUTH] User successfully authenticated with {ClaimCount} claims",
                authenticationState.User.Claims.Count());

            return (true, null);
        }
        public async Task<bool> IsAuthenticatedAsync()
        {
            string? storedToken = await SecureStorage.GetAuthToken(_jsRuntime);

            if (string.IsNullOrEmpty(storedToken))
            {
                _logger.LogInformation("[AUTH] No token found, user is not authenticated");
                return false;
            }

            bool isValidToken = await IsTokenValid(storedToken);
            if (!isValidToken)
            {
                _logger.LogInformation("[AUTH] Token is expired or invalid");
                await LogoutAsync();
                return false;
            }

            AuthenticationState authenticationState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            bool isUserAuthenticated = authenticationState.User.Identity?.IsAuthenticated ?? false;

            if (!isUserAuthenticated)
            {
                _logger.LogWarning("[AUTH] Token exists but auth state shows unauthenticated - refreshing state");
                ((CustomAuthStateProvider)_authenticationStateProvider).NotifyUserAuthentication(storedToken);
                await Task.Delay(100);
            }

            return true;
        }

        public async Task LogoutAsync()
        {
            _logger.LogInformation("[AUTH] Logging out user");

            await SecureStorage.ClearAuthToken(_jsRuntime);
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "userRole");
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "userRole");

            _httpClient.DefaultRequestHeaders.Authorization = null;

            ((CustomAuthStateProvider)_authenticationStateProvider).NotifyUserLogout();

            _navigationManager.NavigateTo("/login", forceLoad: true);
        }

        public Task<bool> IsTokenValid(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Task.FromResult(false);
            }

            try
            {
                JwtSecurityTokenHandler tokenHandler = new();
                JwtSecurityToken jwtToken = tokenHandler.ReadJwtToken(token);

                _logger.LogDebug("[AUTH] Token validity: Expires {ExpireTime}, Now {CurrentTime}, Valid: {IsValid}",
                    jwtToken.ValidTo, DateTime.UtcNow, jwtToken.ValidTo > DateTime.UtcNow);

                return Task.FromResult(jwtToken.ValidTo > DateTime.UtcNow);
            }
            catch (Exception validationException)
            {
                _logger.LogError(validationException, "[AUTH] Error validating token");
                return Task.FromResult(false);
            }
        }

        public async Task<string?> GetUserRoleAsync()
        {
            return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "userRole")
                   ?? await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "userRole");
        }

        public void NavigateToHomepage()
        {
            _navigationManager.NavigateTo("/", forceLoad: true);
        }

        public void NavigateAfterLogin(string? returnUrl)
        {
            string destinationUrl = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            _navigationManager.NavigateTo(destinationUrl, forceLoad: true);
        }
    }

    public class AuthResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public AuthResponseItem? Item { get; set; }
    }

    public class AuthResponseItem
    {
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}