using Microsoft.Extensions.Logging;
using IWshRuntimeLibrary;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using File = System.IO.File;
using RssPlayer.Components.Utilities;

namespace RssPlayer.Components.Configuration
{
    public sealed class AppConfiguration
    {
        private static AppConfiguration _instance;
        private static readonly object _lockObject = new object();
        private readonly ILogger<AppConfiguration> _logger;
        [ComImport]
        [Guid("F935DC22-1CF0-11D3-9A73-0000F8045797")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IWshShortcut
        {
            string FullName { get; }
            string TargetPath { get; set; }
        }
        public static AppConfiguration Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lockObject)
                    {
                        _instance ??= new AppConfiguration();
                    }
                }
                return _instance;
            }
        }
        public enum StartupConfigState
        {
            Unconfigured,
            Enabled,
            Disabled
        }

        public string ApiBaseUrl { get; private set; } = "https://remotescreenapi.diviso.dev";
        public string MainUrl { get; private set; }
        public string IdleScreenUrl { get; private set; }
        public string CurrentVersion { get; private set; }
        public string ApiHealthEndpoint => $"{ApiBaseUrl}/api/screens/health";
        public string ScreenRegistrationEndpoint => $"{ApiBaseUrl}/api/screens/player/register";
        public string GetScreenConfigEndpoint(string macAddress) => $"{ApiBaseUrl}/api/screens/player/{macAddress}/config";
        public string GetScreenStatusEndpoint(string macAddress) => $"{ApiBaseUrl}/api/screens/player/{macAddress}/status";

        public int ApiTimeoutSeconds { get; private set; } = 5;
        public int StatusUpdateIntervalMinutes { get; private set; } = 15;
        public int MaintenanceIntervalMinutes { get; private set; } = 1;

        public bool ForceOfflineMode { get; private set; } = false;
        public bool IsFreshInstall { get; private set; } = false;
        public bool ShouldShowStartupConfigDialog()
        {
            return StartupConfiguration == StartupConfigState.Unconfigured;
        }
        public string AppDataFolder { get; }
        public string ResourcesFolder { get; }
        public string UrlCachePath { get; private set; }
        public string LogFilePath { get; }

        public StartupConfigState StartupConfiguration { get; internal set; } = StartupConfigState.Unconfigured;


        public event Action OnConfigUpdated;

        private AppConfiguration()
        {
            AppDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RssPlayer"
            );

            ResourcesFolder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources"
            );

            UrlCachePath = Path.Combine(AppDataFolder, "LastUrl.txt");
            LogFilePath = Path.Combine(AppDataFolder, "AppLogs.txt");

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<AppConfiguration>();

            EnsureDirectoriesCreated();
            LoadSettings();
        }
        private void EnsureDirectoriesCreated()
        {
            try
            {
                if (string.IsNullOrEmpty(AppDataFolder))
                {
                    throw new InvalidOperationException("AppDataFolder is not initialized.");
                }

                EnsureDirectoryExists(AppDataFolder);
                EnsureDirectoryExists(ResourcesFolder);
                EnsureLogFileExists();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create required directories and log file");
            }
        }

        private void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                _logger.LogInformation($"Created directory: {directoryPath}");
            }
        }

        private void EnsureLogFileExists()
        {
            if (!System.IO.File.Exists(LogFilePath))
            {
                System.IO.File.Create(LogFilePath).Dispose();
                _logger.LogInformation($"Created log file: {LogFilePath}");
            }
        }

        public void SetMainUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                MainUrl = url;
                SaveSettings();
                _logger.LogInformation($"Main URL updated: {MainUrl}");
            }
        }

        public void SetIdleScreenUrl(string newIdleScreenUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newIdleScreenUrl))
                    throw new ArgumentException("Idle screen URL cannot be empty");

                _ = new Uri(newIdleScreenUrl);

                IdleScreenUrl = newIdleScreenUrl;
                SaveSettings();
                _logger.LogInformation($"Idle Screen URL updated: {IdleScreenUrl}");
            }
            catch (UriFormatException)
            {
                _logger.LogWarning($"Attempted to set an invalid Idle Screen URL: {newIdleScreenUrl}");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting idle screen URL: {ex.Message}");
            }
        }
        public void SetStartupConfiguration(bool enableStartup)
        {
            StartupConfiguration = enableStartup ? StartupConfigState.Enabled : StartupConfigState.Disabled;
            SaveSettings();
        }

        public void ForceOffline(bool enable)
        {
            ForceOfflineMode = false;
        }

        public string GetRegistrationPagePath(string macAddress)
        {
            return Path.Combine(
                ResourcesFolder,
                $"DeviceRegistration_{macAddress.Replace(":", "-")}.html"
            );
        }

        public void LoadSettings()
        {
            string configFilePath = Path.Combine(AppDataFolder, "Settings.json");

            try
            {
                string jsonContent = File.ReadAllText(configFilePath);
                object settings = new
                {
                    ApiBaseUrl = string.Empty,
                    ApiTimeoutSeconds = 0,
                    StatusUpdateIntervalMinutes = 0,
                    MaintenanceIntervalMinutes = 0,
                    IdleScreenUrl = string.Empty,
                    MainUrl = string.Empty,
                    ForceOfflineMode = false,
                    CurrentVersion = string.Empty,
                    StartupConfiguration = string.Empty
                };

                object configData = JsonSerializer.Deserialize(jsonContent, settings.GetType());

                UpdateSettingsFromConfig(configData);
                OnConfigUpdated?.Invoke();
            }
            catch (FileNotFoundException)
            {
                IsFreshInstall = true;
                _logger.LogWarning("No configuration file found. Using default values.");
            }

            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse configuration JSON");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration settings");
            }
        }

        public void SaveSettings()
        {
            string configFilePath = Path.Combine(AppDataFolder, "Settings.json");

            try
            {
                object settingsToSave = new
                {
                    ApiBaseUrl,
                    ApiTimeoutSeconds,
                    StatusUpdateIntervalMinutes,
                    MaintenanceIntervalMinutes,
                    ForceOfflineMode,
                    IdleScreenUrl,
                    CurrentVersion,
                    StartupConfiguration = StartupConfiguration.ToString()
                };

                JsonSerializerOptions serializerOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string jsonContent = JsonSerializer.Serialize(settingsToSave, serializerOptions);
                File.WriteAllText(configFilePath, jsonContent);
                _logger.LogInformation("Settings saved successfully.");

                OnConfigUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration settings");
            }
        }
        public void SetCurrentVersion(string version)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(version))
                {
                    CurrentVersion = version;
                    SaveSettings();
                    _logger.LogInformation($"Current version updated: {CurrentVersion}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting current version: {ex.Message}");
            }
        }

        private void UpdateSettingsFromConfig(object configData)
        {
            Type type = configData.GetType();

            UpdateStringProperty(type, configData, "ApiBaseUrl", value => ApiBaseUrl = value);
            UpdateIntProperty(type, configData, "ApiTimeoutSeconds", value => ApiTimeoutSeconds = value);
            UpdateIntProperty(type, configData, "StatusUpdateIntervalMinutes", value => StatusUpdateIntervalMinutes = value);
            UpdateStringProperty(type, configData, "MainUrl", value => MainUrl = value);
            UpdateIntProperty(type, configData, "MaintenanceIntervalMinutes", value => MaintenanceIntervalMinutes = value);
            UpdateStringProperty(type, configData, "IdleScreenUrl", value => IdleScreenUrl = value);
            UpdateBoolProperty(type, configData, "ForceOfflineMode", value => ForceOfflineMode = value);
            UpdateStringProperty(type, configData, "CurrentVersion", value => CurrentVersion = value);
            UpdateStartupConfigProperty(type, configData, "StartupConfiguration", value =>
            {
                if (Enum.TryParse(value, out StartupConfigState startupState))
                {
                    StartupConfiguration = startupState;
                }
            });
        }

        private void UpdateStringProperty(Type type, object configData, string propertyName, Action<string> updateAction)
        {
            try
            {
                System.Reflection.PropertyInfo property = type.GetProperty(propertyName);
                string value = property?.GetValue(configData) as string;

                if (!string.IsNullOrEmpty(value))
                {
                    updateAction(value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to update string property {propertyName}: {ex.Message}");
            }
        }
        private void UpdateBoolProperty(Type type, object configData, string propertyName, Action<bool> updateAction)
        {
            try
            {
                if (propertyName == "ForceOfflineMode" && ForceOfflineMode)
                {
                    return;
                }

                System.Reflection.PropertyInfo property = type.GetProperty(propertyName);
                object value = property?.GetValue(configData);

                if (value != null)
                {
                    updateAction(Convert.ToBoolean(value));
                }
            }
            catch (FormatException ex)
            {
                _logger.LogWarning($"Failed to convert property {propertyName} to bool: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to update bool property {propertyName}: {ex.Message}");
            }
        }

        private void UpdateIntProperty(Type type, object configData, string propertyName, Action<int> updateAction)
        {
            try
            {
                System.Reflection.PropertyInfo property = type.GetProperty(propertyName);
                object value = property?.GetValue(configData);

                if (value != null)
                {
                    updateAction(Convert.ToInt32(value));
                }
            }
            catch (FormatException ex)
            {
                _logger.LogWarning($"Failed to convert property {propertyName} to int: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to update int property {propertyName}: {ex.Message}");
            }
        }
        private void UpdateStartupConfigProperty(Type type, object configData, string propertyName, Action<string> updateAction)
        {
            try
            {
                System.Reflection.PropertyInfo property = type.GetProperty(propertyName);
                string value = property?.GetValue(configData) as string;

                if (!string.IsNullOrEmpty(value))
                {
                    updateAction(value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to update startup config property {propertyName}: {ex.Message}");
            }
        }

        public async Task<bool> FetchAndUpdateConfigAsync(string macAddress)
        {
            try
            {
                using HttpClient httpClient = new HttpClient();
                string apiUrl = GetScreenConfigEndpoint(macAddress);

                HttpResponseMessage response = await httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to fetch config from API. Status Code: {response.StatusCode}");
                    return false;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                JsonElement resultData = JsonSerializer.Deserialize<JsonElement>(jsonContent);

                if (!ValidateApiResponse(resultData))
                {
                    return false;
                }

                UpdateConfigFromApiResponse(resultData);
                SaveSettings();
                _logger.LogInformation("Configuration updated successfully.");
                OnConfigUpdated?.Invoke();
                return true;
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                _logger.LogError("API base URL unreachable. Forcing offline mode.");
                ForceOfflineMode = false;
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching and updating config");
                return false;
            }
        }

        private bool ValidateApiResponse(JsonElement resultData)
        {
            try
            {
                bool success = resultData.GetProperty("success").GetBoolean();
                if (!success) throw new InvalidOperationException("API returned unsuccessful response");

                JsonElement itemElement = resultData.GetProperty("item");
                if (itemElement.ValueKind == JsonValueKind.Null)
                    throw new InvalidOperationException("API response missing 'item' property");

                string url = itemElement.GetProperty("url").GetString();
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException("API returned empty configuration. No 'url' found");

                return true;
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning($"API response missing required property: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error validating API response: {ex.Message}");
                return false;
            }
        }

        private void UpdateConfigFromApiResponse(JsonElement resultData)
        {
            try
            {
                JsonElement itemElement = resultData.GetProperty("item");

                try
                {
                    JsonElement idleScreenUrlElement = itemElement.GetProperty("idleScreenUrl");
                    string idleUrl = idleScreenUrlElement.GetString();

                    if (!string.IsNullOrWhiteSpace(idleUrl))
                    {
                        IdleScreenUrl = idleUrl;
                        _logger.LogInformation($"Idle screen URL updated from API: {IdleScreenUrl}");
                    }
                }
                catch (KeyNotFoundException)
                {
                    try
                    {
                        JsonElement idleScreenUrlElement = itemElement.GetProperty("iDleScreenUrl");
                        string idleUrl = idleScreenUrlElement.GetString();

                        if (!string.IsNullOrWhiteSpace(idleUrl))
                        {
                            IdleScreenUrl = idleUrl;
                            _logger.LogInformation($"Idle screen URL (capitalized) updated from API: {IdleScreenUrl}");
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    string mainUrl = itemElement.GetProperty("url").GetString();
                    if (!string.IsNullOrWhiteSpace(mainUrl))
                    {
                        MainUrl = mainUrl;
                        _logger.LogInformation($"Main URL updated from API: {MainUrl}");
                    }
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating configuration from API response: {ex.Message}");
            }
        }
    }
}