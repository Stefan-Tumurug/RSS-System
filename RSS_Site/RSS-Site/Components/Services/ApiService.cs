using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using RssSite.Components.Models;
using RssSite.Components.Storage;

namespace RssSite.Components.Services
{
    public class ApiService
    {
        internal readonly HttpClient _httpClient;
        internal readonly string _apiBaseUrl;
        internal readonly ILogger<ApiService> _logger;
        private readonly IJSRuntime _jsRuntime;
        private readonly IConfiguration _configuration;
        public const string ErrorLogPrefix = "[API ERROR]";

        internal readonly JsonSerializerOptions JsonSerializerSettings = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public ApiService(HttpClient httpClient, IConfiguration configuration, ILogger<ApiService> logger, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _apiBaseUrl = $"{_configuration["ApiSettings:BaseUrl"]}/api/screens";
            _logger = logger;
            _jsRuntime = jsRuntime;
        }

        public async Task<List<ScreenModel>> GetAllScreensAsync()
        {
            await AddAuthorizationHeader();
            try
            {
                string requestUrl = $"{_apiBaseUrl}/screens";
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("{Prefix} Failed to fetch screens. Status: {StatusCode} - {Response}",
                        ErrorLogPrefix, response.StatusCode, jsonResponse);
                    return [];
                }

                JsonSerializerOptions options = new(JsonSerializerSettings)
                {
                    PropertyNameCaseInsensitive = true
                };

                using JsonDocument jsonDoc = JsonDocument.Parse(jsonResponse);
                JsonElement root = jsonDoc.RootElement;

                if (root.TryGetProperty("success", out JsonElement successElement) &&
                    successElement.GetBoolean())
                {
                    if (root.TryGetProperty("items", out JsonElement itemsElement))
                    {
                        List<ScreenModel>? deserializedItems = JsonSerializer.Deserialize<List<ScreenModel>>(
                            itemsElement.GetRawText(),
                            options
                        );

                        return deserializedItems ?? [];
                    }

                    if (root.TryGetProperty("item", out JsonElement itemElement))
                    {
                        List<ScreenModel>? deserializedItem = JsonSerializer.Deserialize<List<ScreenModel>>(
                            itemElement.GetRawText(),
                            options
                        );

                        return deserializedItem ?? [];
                    }
                }

                string errorMessage = root.TryGetProperty("errorMessage", out JsonElement errorElement)
                    ? errorElement.GetString() ?? "Unknown error"
                    : "Unknown error";

                _logger.LogWarning("[API] Failed to get screens: {ErrorMessage}", errorMessage);
                return [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Exception fetching screens", ErrorLogPrefix);
                return [];
            }
        }
        public async Task<bool> SaveScreenConfigAsync(ScreenModel updatedScreen)
        {
            try
            {
                string requestUrl = $"{_apiBaseUrl}/player/{updatedScreen.MacAddress}/config";
                string payload = JsonSerializer.Serialize(updatedScreen, JsonSerializerSettings);

                _logger.LogInformation("[API] Sending config to {Url}", requestUrl);
                _logger.LogInformation("[API] Payload: {Payload}", payload);

                StringContent content = new(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(requestUrl, content);
                string responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("[API] Response from server: {StatusCode} - {Response}",
                    response.StatusCode, responseContent);

                if (response.IsSuccessStatusCode)
                {
                    ApiResponse<string>? responseWrapper = JsonSerializer.Deserialize<ApiResponse<string>>(
                        responseContent,
                        JsonSerializerSettings
                    );

                    return responseWrapper?.Success ?? false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API] Error saving config");
                return false;
            }
        }
        public async Task<bool> DeleteScreenAsync(string macAddress)
        {
            try
            {
                string requestUrl = $"{_apiBaseUrl}/player/{macAddress}";
                _logger.LogInformation("[API] Deleting screen at {Url}", requestUrl);

                HttpResponseMessage response = await _httpClient.DeleteAsync(requestUrl);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[API] Screen {MacAddress} deleted successfully", macAddress);
                    return true;
                }

                _logger.LogError("[API] Failed to delete screen. Status: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API] Error deleting screen");
                return false;
            }
        }

        public async Task<bool> CheckApiHealthAsync()
        {
            try
            {
                _logger.LogInformation("[API] Checking API health");
                HttpResponseMessage response = await _httpClient.GetAsync($"{_apiBaseUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API] Error checking API health");
                return false;
            }
        }
        public async Task<ScreenModel?> GetScreenConfigAsync(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogError("[API] MacAddress cannot be empty!");
                return null;
            }

            string requestUrl = $"{_apiBaseUrl}/player/{macAddress}/config";
            _logger.LogInformation("[API] Fetching config from {Url}", requestUrl);

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[API ERROR] Failed to fetch screen config. Status: {StatusCode} - {Content}",
                        response.StatusCode, responseContent);
                    return null;
                }

                _logger.LogInformation("[API] Response received: {Response}", responseContent);

                ApiResponse<ScreenModel>? responseWrapper = JsonSerializer.Deserialize<ApiResponse<ScreenModel>>(
                    responseContent,
                    JsonSerializerSettings
                );

                if (responseWrapper == null)
                {
                    _logger.LogWarning("[API] Deserialization returned null response wrapper");
                    return null;
                }

                if (!responseWrapper.Success)
                {
                    _logger.LogWarning("[API] Failed to get screen config: {ErrorMessage}",
                        responseWrapper.ErrorMessage ?? "Unknown error");
                    return null;
                }

                ScreenModel? screen = responseWrapper.Item;
                if (screen == null)
                {
                    _logger.LogWarning("[API] Response wrapper success, but item is null");
                    return null;
                }

                screen.MacAddress = macAddress;
                screen.Status ??= "Offline";
                screen.LastUpdated = (screen.LastUpdated == default)
                    ? DateTime.UtcNow
                    : screen.LastUpdated;
                screen.ScreenResolution = string.IsNullOrEmpty(screen.ScreenResolution)
                    ? "1920x1080"
                    : screen.ScreenResolution;
                screen.RefreshInterval = (screen.RefreshInterval <= 0)
                    ? 60
                    : screen.RefreshInterval;

                return screen;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[API] JSON deserialization error fetching screen config");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API] Unexpected exception fetching screen config");
                return null;
            }
        }

        public async Task AddAuthorizationHeader()
        {
            try
            {
                string? token = await SecureStorage.GetAuthToken(_jsRuntime);

                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("[AUTH] No token available for Authorization header.");
                    return;
                }

                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                string tokenStart = token.Length > 10
                    ? string.Concat(token.AsSpan(0, 10), "...")
                    : token;

                _logger.LogInformation("[AUTH] Authorization header set: {TokenStart}", tokenStart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUTH] Error setting authorization header.");
            }
        }

        public async Task<HttpResponseMessage> PostAsync<T>(string url, T data)
        {
            await AddAuthorizationHeader();

            try
            {
                string jsonPayload = JsonSerializer.Serialize(data, JsonSerializerSettings);
                StringContent content = new(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogInformation("[API CLIENT] Sending POST request to {Url}", url);
                _logger.LogInformation("[API CLIENT] Payload: {Payload}", jsonPayload);

                return await _httpClient.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API CLIENT] Error during POST request");
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            }
        }

        public async Task<HttpResponseMessage> PutAsync<T>(string url, T data)
        {
            await AddAuthorizationHeader();

            try
            {
                string jsonPayload = JsonSerializer.Serialize(data, JsonSerializerSettings);
                StringContent content = new(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogInformation("[API CLIENT] Sending PUT request to {Url}", url);
                _logger.LogInformation("[API CLIENT] Payload: {Payload}", jsonPayload);

                return await _httpClient.PutAsync(url, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API CLIENT] Error during PUT request");
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            }
        }

        public async Task<HttpResponseMessage> DeleteAsync(string url)
        {
            await AddAuthorizationHeader();

            try
            {
                _logger.LogInformation("[API CLIENT] Sending DELETE request to {Url}", url);
                return await _httpClient.DeleteAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API CLIENT] Error during DELETE request");
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            }
        }
        public async Task<bool> LoginAsync(string username, string password, bool rememberMe)
        {
            _logger.LogInformation("[AUTH] Sending login request for user: {Username}", username);

            try
            {
                object loginRequest = new { Username = username, Password = password };
                string jsonPayload = JsonSerializer.Serialize(loginRequest, JsonSerializerSettings);
                StringContent requestContent = new(jsonPayload, Encoding.UTF8, "application/json");
                string apiUrl = $"{_configuration["ApiSettings:BaseUrl"]}/api/auth/login";

                HttpResponseMessage apiResponse = await _httpClient.PostAsync(apiUrl, requestContent);
                string responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("[AUTH] API Response Status: {StatusCode}, Content: {Response}",
                    apiResponse.StatusCode, responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[AUTH] Login failed for user: {Username}", username);
                    return false;
                }

                AuthResponse? authenticationResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, JsonSerializerSettings);

                if (authenticationResponse == null || !authenticationResponse.Success || string.IsNullOrEmpty(authenticationResponse.Token))
                {
                    _logger.LogWarning("[AUTH] Invalid authentication response for user: {Username}", username);
                    return false;
                }

                bool isTokenStored = await SecureStorage.StoreAuthToken(_jsRuntime, authenticationResponse.Token, rememberMe);

                if (!isTokenStored)
                {
                    _logger.LogError("[AUTH] Failed to store token for user: {Username}", username);
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authenticationResponse.Token);
                _logger.LogInformation("[AUTH] User {Username} successfully logged in.", username);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUTH] Exception during login for user: {Username}", username);
                return false;
            }
        }

        public async Task<bool> LogoutAsync(string username)
        {
            try
            {
                string requestUrl = $"{_configuration["ApiSettings:BaseUrl"]}/api/auth/logout";
                _logger.LogInformation("[AUTH] Logging out user: {Username}", username);

                object logoutRequest = new { Username = username };
                string payload = JsonSerializer.Serialize(logoutRequest, JsonSerializerSettings);
                StringContent content = new(payload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(requestUrl, content);
                string responseContent = await response.Content.ReadAsStringAsync();

                ApiResponse? result = JsonSerializer.Deserialize<ApiResponse>(responseContent, JsonSerializerSettings);

                if (result != null && result.Success)
                {
                    _logger.LogInformation("[AUTH] Logout successful for user: {Username}", username);
                    return true;
                }

                _logger.LogWarning("[AUTH] Logout failed for user: {Username}", username);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUTH] Exception during logout for user: {Username}", username);
                return false;
            }
        }

        public async Task<bool> ValidateTokenAsync(string username, string token)
        {
            try
            {
                string requestUrl = $"{_configuration["ApiSettings:BaseUrl"]}/api/auth/validate?username={username}&token={token}";
                _logger.LogInformation("[AUTH] Validating token for user: {Username}", username);

                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                ApiResponse? result = JsonSerializer.Deserialize<ApiResponse>(responseContent, JsonSerializerSettings);

                return result != null && result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUTH] Exception validating token for user: {Username}", username);
                return false;
            }
        }

        public class LogEntry
        {
            public int ID { get; set; }
            public string MacAddress { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        public class LogResponse
        {
            public List<LogEntry> Logs { get; set; } = [];
            public int TotalLogs { get; set; }
            public int TotalPages { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
        }

        public class JsonStringDateTimeConverter : JsonConverter<DateTime?>
        {
            public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;

                string? dateString = reader.GetString();
                if (string.IsNullOrWhiteSpace(dateString) || dateString == "Not Available")
                    return null;

                string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd"];

                if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime result))
                {
                    return result;
                }

                return null;
            }

            public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
            }
        }

        public class AuthResponse
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
        }
    }
}