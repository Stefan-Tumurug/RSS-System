using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Utilities;
using RssPlayer.Components.Services;
using System.Collections.Generic;

namespace RssPlayer.Components.Services
{
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly LoggingService _logger;
        private readonly AppConfiguration _config;
        private DeviceConfig _lastRetrievedConfig;
        private bool? _lastKnownDeviceRegistrationState = null;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public ApiService(HttpClient httpClient, LoggingService logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = AppConfiguration.Instance;
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.ApiTimeoutSeconds);
        }
        public async Task<DeviceConfig> GetScreenConfigAsync(string macAddress)
        {
            try
            {
                string apiUrl = _config.GetScreenConfigEndpoint(macAddress);

                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"API Call Failed: {response.ReasonPhrase}");
                    return null;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();

                JsonElement resultData = JsonSerializer.Deserialize<JsonElement>(jsonContent);

                if (!ValidateApiResponse(resultData))
                {
                    return null;
                }

                JsonElement itemElement = resultData.GetProperty("item");
                _lastRetrievedConfig = MapDeviceConfig(macAddress, itemElement);
                return _lastRetrievedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError("API Exception", ex);
                return null;
            }
        }
        private DeviceConfig MapDeviceConfig(string macAddress, JsonElement itemElement)
        {
            string idleScreenUrl = null;

            try
            {
                JsonElement idleElement = itemElement.GetProperty("idleScreenUrl");
                idleScreenUrl = idleElement.GetString();
            }
            catch (KeyNotFoundException)
            {
                try
                {
                    JsonElement capElement = itemElement.GetProperty("iDleScreenUrl");
                    idleScreenUrl = capElement.GetString();
                }
                catch
                {
                }
            }

            return new DeviceConfig
            {
                MacAddress = macAddress,
                Name = GetJsonPropertyStringFromItem(itemElement, "name"),
                Url = GetJsonPropertyStringFromItem(itemElement, "url"),
                IdleScreenUrl = idleScreenUrl,
                Address = GetJsonPropertyStringFromItem(itemElement, "address"),
                OperatingSystem = GetJsonPropertyStringFromItem(itemElement, "operatingSystem"),
                RefreshInterval = GetJsonPropertyIntFromItem(itemElement, "refreshInterval", 60),
                ScreenResolution = GetJsonPropertyStringFromItem(itemElement, "screenResolution") ?? "1920x1080",
                AutoRestart = GetJsonPropertyBoolFromItem(itemElement, "autoRestart"),
                StartupEnabled = GetJsonPropertyBoolFromItem(itemElement, "startupEnabled")

            };
        }
        public DeviceConfig GetLastRetrievedConfig()
        {
            return _lastRetrievedConfig;
        }
        public void SetLastRetrievedConfig(DeviceConfig config)
        {
            _lastRetrievedConfig = config;
        }
        private bool ValidateApiResponse(JsonElement resultData)
        {
            try
            {
                JsonElement successElement = resultData.GetProperty("success");
                bool isSuccess = successElement.GetBoolean();
                if (!isSuccess)
                {
                    throw new InvalidOperationException("API returned unsuccessful response");
                }

                JsonElement itemElement = resultData.GetProperty("item");
                if (itemElement.ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidOperationException("API response missing 'item' property");
                }

                try
                {
                    JsonElement idleScreenUrlElement = itemElement.GetProperty("idleScreenUrl");
                    string idleScreenUrl = idleScreenUrlElement.GetString();
                }
                catch (KeyNotFoundException)
                {
                    try
                    {
                        JsonElement capitalized = itemElement.GetProperty("iDleScreenUrl");
                        string idleScreenUrl = capitalized.GetString();
                    }
                    catch (KeyNotFoundException)
                    {
                        _logger.LogWarning("[WARNING] API did not return an 'idleScreenUrl' property.");
                    }
                }

                return true;
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning($"[WARNING] Required API response property missing: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning($"[WARNING] {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[WARNING] Unexpected error validating API response: {ex.Message}");
                return false;
            }
        }
    
        private string GetJsonPropertyStringFromItem(JsonElement element, string propertyName)
        {
            try
            {
                JsonElement property = element.GetProperty(propertyName);
                if (property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
                return null;
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting string property {propertyName}: {ex.Message}");
                return null;
            }
        }
        private int GetJsonPropertyIntFromItem(JsonElement element, string propertyName, int defaultValue)
        {
            try
            {
                JsonElement property = element.GetProperty(propertyName);
                if (property.ValueKind == JsonValueKind.Number)
                {
                    return property.GetInt32();
                }
                return defaultValue;
            }
            catch (KeyNotFoundException)
            {
                return defaultValue;
            }
            catch (InvalidOperationException)
            {
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting int property {propertyName}: {ex.Message}");
                return defaultValue;
            }
        }

        private bool GetJsonPropertyBoolFromItem(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            return false;
        }
        private async Task<bool> SendStatusUpdateWithRetry(string macAddress, string status, string jsonContent, Action<string> updateIdleScreenUrl)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(1000 * attempt);
                    }
                    string apiUrl = _config.GetScreenStatusEndpoint(macAddress);

                    using HttpContent content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await _httpClient.PostAsync(apiUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        await HandleSuccessfulStatusUpdate(response, updateIdleScreenUrl);
                        return true;
                    }

                    await HandleFailedStatusUpdate(response, status);
                }
                catch (Exception innerEx)
                {
                    _logger.LogError($"Error during status update attempt {attempt + 1}: {innerEx.Message}");
                }
            }

            _logger.LogError($"Failed to update device {macAddress} status to {status} after multiple attempts");
            return false;
        }
        private async Task HandleSuccessfulStatusUpdate(HttpResponseMessage response, Action<string> updateIdleScreenUrl)
        {
            try
            {
                string responseBody = await response.Content.ReadAsStringAsync();
            }
            catch { }

            try
            {
                AppConfiguration.Instance.LoadSettings();
                updateIdleScreenUrl?.Invoke(AppConfiguration.Instance.IdleScreenUrl);
            }
            catch (Exception urlEx)
            {
                _logger.LogWarning($"Error updating idle screen URL: {urlEx.Message}");
            }
        }

        private async Task HandleFailedStatusUpdate(HttpResponseMessage response, string status)
        {
            try
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Failed to send {status} status: {response.StatusCode}. Response: {errorBody}");
            }
            catch
            {
                _logger.LogWarning($"Failed to send {status} status: {response.StatusCode}");
            }
        }
        public async Task<bool> SendStatusUpdateAsync(string macAddress, string status, Action<string> updateIdleScreenUrl)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogError("Cannot send status update: MAC address is null or empty");
                return false;
            }

            status = status?.ToLower() switch
            {
                "online" => "Online",
                "idle" => "Idle",
                _ => "Offline"
            };

            try
            {

                DeviceStatusUpdate statusUpdate = CreateDeviceStatusUpdate(status);
                string jsonContent = SerializeStatusUpdate(statusUpdate);

                return await SendStatusUpdateWithRetry(macAddress, status, jsonContent, updateIdleScreenUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error sending {status} status", ex);
                return false;
            }
        }
        private DeviceStatusUpdate CreateDeviceStatusUpdate(string status)
        {
            return new DeviceStatusUpdate
            {
                Status = status,
                LastUpdated = DateTime.UtcNow,
                LastSeenOnline = status == "Online" ? DateTime.UtcNow : (DateTime?)null
            };
        }

        private string SerializeStatusUpdate(DeviceStatusUpdate statusUpdate)
        {
            return JsonSerializer.Serialize(statusUpdate, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }
       
        public async Task<bool> CheckApiHealthAsync()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_config.ApiBaseUrl}/api/screens/health");
                bool isHealthy = response.IsSuccessStatusCode;

                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError("API Health Check Error", ex);
                return false;
            }
        }

        public async Task<string> GetIdleScreenUrlAsync(string macAddress)
        {
            try
            {
                DeviceConfig config = await GetScreenConfigAsync(macAddress);
                return config?.IdleScreenUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching Idle Screen URL", ex);
            }

            return null;
        }

        public async Task<bool> RegisterDeviceAsync(
            string macAddress, string name, string url, bool autoRestart,
            string idleScreenUrl, string screenResolution, int refreshInterval,
            string address, string operatingSystem)
        {
            try
            {
                _logger.Log($"Registering MAC: {macAddress}");

                if (!ValidateRegistrationInput(macAddress, name, url))
                {
                    return false;
                }

                DeviceRegistration registrationData = CreateDeviceRegistration(
                    macAddress, name, url, autoRestart, idleScreenUrl,
                    screenResolution, refreshInterval, address, operatingSystem);

                return await SendRegistrationRequest(registrationData, macAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error during registration", ex);
                return false;
            }
        }

        private bool ValidateRegistrationInput(string macAddress, string name, string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(macAddress))
                    throw new ArgumentException("MAC address is required");

                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Name is required");

                if (string.IsNullOrWhiteSpace(url))
                    throw new ArgumentException("URL is required");

                return true;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError($"Registration validation error: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error validating registration input: {ex.Message}");
                return false;
            }
        }
        private DeviceRegistration CreateDeviceRegistration(
            string macAddress, string name, string url, bool autoRestart,
            string idleScreenUrl, string screenResolution, int refreshInterval,
            string address, string operatingSystem)
        {
            return new DeviceRegistration
            {
                MacAddress = macAddress,
                Name = name,
                Url = url,
                AutoRestart = autoRestart,
                IdleScreenUrl = string.IsNullOrWhiteSpace(idleScreenUrl) ? null : idleScreenUrl.Trim(),
                ScreenResolution = screenResolution.Trim(),
                RefreshInterval = refreshInterval,
                Address = address.Trim(),
                OperatingSystem = operatingSystem.Trim()
            };
        }
        private async Task<bool> SendRegistrationRequest(DeviceRegistration registrationData, string macAddress)
        {
            try
            {
                string jsonContent = JsonSerializer.Serialize(registrationData);
                HttpContent content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(_config.ScreenRegistrationEndpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogSuccess("Device registered successfully!");
                    return await HandleSuccessfulRegistration(macAddress);
                }

                _logger.LogError($"Registration failed with status code: {response.StatusCode}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Registration failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending registration request: {ex.Message}");
                return false;
            }
        }
        private async Task<bool> HandleSuccessfulRegistration(string macAddress)
        {
            bool success = await _config.FetchAndUpdateConfigAsync(macAddress);
            if (success)
            {
                _logger.LogSuccess("Configuration fetched after registration.");
            }
            return true;
        }

        public async Task<bool> CheckIfDeviceExistsAsync(string macAddress)
        {
            try
            {
                bool isApiHealthy = await CheckApiHealthAsync();
                if (!isApiHealthy)
                {
                    _logger.Log($"API is offline. Skipping device existence check for {macAddress}.");

                    return _lastKnownDeviceRegistrationState ?? true;
                }

                string apiUrl = $"{_config.ApiBaseUrl}/api/screens/player/{macAddress}/config";

                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);

                bool isRegistered = EvaluateDeviceExistenceResponse(response, macAddress);

                _lastKnownDeviceRegistrationState = isRegistered;

                return isRegistered;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking if device {macAddress} exists", ex);

                return _lastKnownDeviceRegistrationState ?? true;
            }
        }

        private bool EvaluateDeviceExistenceResponse(HttpResponseMessage response, string macAddress)
        {
            try
            {
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.Log($"Device {macAddress} is NOT registered in the API");
                    return false;
                }

                _logger.LogWarning($"Unexpected status code: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Unexpected response while checking device registration: {ex.Message}");
                return false;
            }
        }
        public class DeviceConfig
        {
            public string MacAddress { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }
            public bool? AutoRestart { get; set; }
            public bool? StartupEnabled { get; set; }
            public string IdleScreenUrl { get; set; }
            public string ScreenResolution { get; set; } = "1920x1080";
            public int RefreshInterval { get; set; } = 60;
            public string Address { get; set; }
            public string OperatingSystem { get; set; }
        }

        public class DeviceStatusUpdate
        {
            public string Status { get; set; }
            public DateTime LastUpdated { get; set; }
            public DateTime? LastSeenOnline { get; set; }
        }

        public class DeviceRegistration
        {
            public string MacAddress { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }
            public bool AutoRestart { get; set; }
            public bool StartupEnabled { get; set; }
            public string IdleScreenUrl { get; set; }
            public string ScreenResolution { get; set; }
            public int RefreshInterval { get; set; }
            public string Address { get; set; }
            public string OperatingSystem { get; set; }
        }
        public class VersionCheckResult
        {
            public bool IsSuccess { get; set; }
            public string CurrentVersion { get; set; }
            public string LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string FileName { get; set; }
            public DateTime? LastCheckedDate { get; set; }

            public VersionCheckResult() { } 

            public VersionCheckResult(
                bool isSuccess,
                string currentVersion = null,
                string latestVersion = null,
                string downloadUrl = null,
                string fileName = null,
                DateTime? lastCheckedDate = null)
            {
                IsSuccess = isSuccess;
                CurrentVersion = currentVersion;
                LatestVersion = latestVersion;
                DownloadUrl = downloadUrl;
                FileName = fileName;
                LastCheckedDate = lastCheckedDate;
            }
        }
        public class VersionResponse
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public VersionInfo Item { get; set; }

            public class VersionInfo
            {
                public string Version { get; set; }
                public string FileName { get; set; }
                public string DownloadUrl { get; set; }
            }
        }
    }
}