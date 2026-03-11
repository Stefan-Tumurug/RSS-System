using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Utilities;
using RssPlayer.Components.Services;
using static RssPlayer.Components.Services.ApiService;
using Timer = System.Timers.Timer;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;
using System.Drawing;
using System.Reflection;
using System.Xml.Linq;
using System.Net.Mail;

namespace RssPlayer.Components.Services
{
    public class PageManager
    {
        private readonly NetworkService _networkService;
        private readonly LoggingService _logger;
        private readonly ApiService _apiService;
        private readonly WebView2 _webView;
        private readonly HttpClient _httpClient;
        private readonly AppConfiguration _config;
        private readonly Dictionary<string, string> _pageCache = new Dictionary<string, string>();
        private readonly PlayerForm _playerForm;

        private IdleService _idleService;
        private MaintenanceService _maintenanceService;

        private string _lastSuccessfulUrl = "";
        private string _idleScreenUrl = null;


        private bool _isHandlingApiCheck = false;
        private bool _isApiOffline = false;
        private bool isNavigating;

        private bool GetisNavigating()
        {
            return isNavigating;
        }

        private void SetisNavigating(bool value)
        {
            isNavigating = value;
        }

        public bool IsNavigating
        {
            get { return GetisNavigating(); }
            private set { SetisNavigating(value); }
        }
        public bool IsApiOffline
        {
            get { return _isApiOffline; }
        }
        public bool IsSystemOperationInProgress { get; private set; }
        public PageManager(
            NetworkService networkService,
            LoggingService logger,
            ApiService apiService,
            WebView2 webView,
            HttpClient httpClient,
            PlayerForm playerForm,
            MaintenanceService maintenanceService = null)
        {
            _playerForm = playerForm;
            _networkService = networkService;
            _logger = logger;
            _apiService = apiService;
            _webView = webView;
            _httpClient = httpClient;
            _maintenanceService = maintenanceService;
            _config = AppConfiguration.Instance;

            _config.ForceOffline(false);
            LoadCachedUrl();

            _webView.NavigationCompleted += WebView_NavigationCompleted;
        }

        public void SetIdleService(IdleService idleService)
        {
            _idleService = idleService;
        }
        public void SetMaintenanceService(MaintenanceService maintenanceService)
        {
            if (maintenanceService != null && _maintenanceService == null)
            {
                _maintenanceService = maintenanceService;
            }
        }
        public bool IsIdle
        {
            get { return _idleService?.IsIdle ?? false; }
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            string url = _webView.Source?.ToString();

            if (_idleService != null)
            {
                _idleService.HandleNavigationEvent(url);
                SetisNavigating(false);
            }
            if (_idleService == null)
            {
                SetisNavigating(false);
                _logger.LogWarning("⚠️ IdleService is null, cannot handle navigation event");
            }
        }

        public string GetMacAddress()
        {
            try
            {
                return _networkService.GetMacAddress();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error getting MAC address", ex);
                return "00-00-00-00-00-00";
            }
        }

        public void StartMonitoringServices()
        {
            try
            {
                _logger.Log("Starting monitoring services...");

                try
                {
                    if (_idleService == null)
                    {
                        _logger.LogWarning("IdleService is not initialized. Skipping idle monitoring start.");
                    }

                    _idleService.StartMonitoring();
                    _logger.Log("IdleService started monitoring.");
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error starting IdleService monitoring", ex);
                }

                try
                {
                    if (_maintenanceService == null)
                    {
                        _logger.LogWarning("MaintenanceService is not initialized. Skipping maintenance monitoring start.");
                    }

                    _logger.Log("Starting MaintenanceService with interval: " + _config.MaintenanceIntervalMinutes + " minutes");
                    _maintenanceService.StartMonitoring();
                    _logger.Log("MaintenanceService started monitoring.");
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error starting MaintenanceService monitoring", ex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error starting monitoring services in PageManager", ex);
            }
        }

        public async Task InitializeNavigation()
        {
            try
            {
                if (_config.ForceOfflineMode)
                {
                    _logger.Log("🚫 ForceOfflineMode is ON — skipping all API checks and using offline content.");

                    try
                    {
                        if (!File.Exists(_config.UrlCachePath) && !string.IsNullOrWhiteSpace(_config.MainUrl))
                        {
                            SaveUrlToCache(_config.MainUrl);
                            _logger.Log($"📂 Wrote default MainUrl to cache for offline fallback: {_config.MainUrl}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error saving URL to cache", ex);
                    }

                    await HandleApiOffline();
                    return;
                }

                if (_webView.CoreWebView2 == null)
                {
                    _logger.LogWarning("CoreWebView2 not initialized, cannot navigate");
                    return;
                }

                try
                {
                    bool apiAvailable = await _apiService.CheckApiHealthAsync();

                    if (apiAvailable)
                    {
                        try
                        {
                            _idleScreenUrl = await _apiService.GetIdleScreenUrlAsync(_networkService.GetMacAddress());

                            if (string.IsNullOrEmpty(_idleScreenUrl) && File.Exists(_config.UrlCachePath + "_idle"))
                            {
                                _idleScreenUrl = File.ReadAllText(_config.UrlCachePath + "_idle").Trim();
                                _logger.Log($"📂 Loaded Idle Screen URL from cache: {_idleScreenUrl}");
                            }

                            _logger.Log($"Idle Screen URL set to: {_idleScreenUrl}");

                            try
                            {
                                _idleService?.UpdateIdleScreenUrl(_idleScreenUrl);

                                if (_idleService == null)
                                {
                                    _logger.LogWarning("⚠️ IdleService is null, cannot update idle screen URL");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("Error updating idle service URL", ex);
                            }

                            await LoadConfiguredUrl();

                            try
                            {
                                string macAddress = _networkService.GetMacAddress();
                                bool success = await _apiService.SendStatusUpdateAsync(macAddress, "Online", UpdateIdleScreenUrl);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("Error sending online status update", ex);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Error processing idle screen URL", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error checking API health", ex);
                }

                try
                {
                    StartMonitoringServices();

                    if (_idleService == null || _maintenanceService == null)
                    {
                        _logger.LogWarning("One or more services not yet initialized in PageManager.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error starting monitoring services", ex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in InitializeNavigation", ex);
            }
        }
        public async Task LoadConfiguredUrl()
        {
            string macAddress = _networkService.GetMacAddress();
            DeviceConfig config = await _apiService.GetScreenConfigAsync(macAddress);
            try
            {
                if (IsIdle && !_idleService.IsIdleScreenUrl(config.Url))
                {
                    _logger.Log("⚠️ Navigation blocked: System is idle.");
                    return;
                }

                bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(macAddress);

                if (!isRegistered)
                {
                    await ShowRegistrationPage();
                    return;
                }

                bool isApiHealthy = await _apiService.CheckApiHealthAsync();
                if (!isApiHealthy)
                {
                    await HandleApiOffline();
                    return;
                }
                if (_isApiOffline)
                {
                    HideOfflineIndicator();
                }

                if (config == null)
                {
                    _logger.LogError("Failed to retrieve device configuration");
                    await HandleApiOffline();
                    return;
                }

                string idleUrl = config.IdleScreenUrl;
                if (string.IsNullOrWhiteSpace(idleUrl) && !string.IsNullOrWhiteSpace(config.Url))
                {
                    idleUrl = config.Url;
                    _logger.LogWarning($"⚠️ No Idle Screen URL provided by API. Using Display URL instead: {idleUrl}");
                }

                if (!string.IsNullOrWhiteSpace(idleUrl) && Uri.IsWellFormedUriString(idleUrl, UriKind.Absolute))
                {
                    UpdateIdleScreenUrl(idleUrl);
                    AppConfiguration.Instance.SetIdleScreenUrl(idleUrl);
                }

                string url = config.Url;
                if (!string.IsNullOrEmpty(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    await NavigateToUrlAndSetOnline(url, macAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Comprehensive error in LoadConfiguredUrl", ex);
            }
        }

        private async Task NavigateToUrlAndSetOnline(string url, string macAddress)
        {
            try
            {
                NavigateToUrl(url);
                SaveUrlToCache(url);
                _lastSuccessfulUrl = url;

                bool statusUpdateSuccess = false;

                try
                {
                    await Task.Delay(500);
                    statusUpdateSuccess = await _apiService.SendStatusUpdateAsync(macAddress, "Online", UpdateIdleScreenUrl);
                }

                catch (Exception immediateEx)
                {
                    _logger.LogError($"Initial status update attempt failed: {immediateEx.Message}");
                }

                if (!statusUpdateSuccess)
                {
                    int[] retryDelays = new[] { 1000, 3000, 5000 };

                    foreach (int delay in retryDelays)
                    {
                        try
                        {
                            await Task.Delay(delay);

                            statusUpdateSuccess = await _apiService.SendStatusUpdateAsync(macAddress, "Online", UpdateIdleScreenUrl);

                            if (statusUpdateSuccess)
                            {
                                break;
                            }
                        }
                        catch (Exception retryEx)
                        {
                            _logger.LogError($"Status update retry failed after {delay}ms: {retryEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Comprehensive navigation and status update error: {ex.Message}", ex);
            }
        }
        public string GetIdleScreenUrl()
        {
            try
            {
                _logger.Log($"Idle Screen URL from API: {_idleScreenUrl}");
                return _idleScreenUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error getting idle screen URL", ex);
                return string.Empty;
            }
        }
        public string GetLastSuccessfulUrl()
        {
            try
            {
                return _lastSuccessfulUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error getting last successful URL", ex);
                return string.Empty;
            }
        }
        private void LoadCachedUrl()
        {
            try
            {
                string cachePath = _config.UrlCachePath;
                if (File.Exists(cachePath))
                {
                    _lastSuccessfulUrl = File.ReadAllText(cachePath).Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading cached URL", ex);
            }
        }

        public void SaveUrlToCache(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                File.WriteAllText(_config.UrlCachePath, url);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error saving URL to cache", ex);
            }
        }
        public void NavigateToUrl(string url)
        {
            try
            {
                if (_idleService != null && IsIdle && !_idleService.IsIdleScreenUrl(url))
                {
                    _logger.Log("⚠️ Navigation blocked: System is idle.");
                    return;
                }
                Task.Delay(100);
                SetisNavigating(true);
                SetSystemOperationInProgress(true);

                try
                {
                    if (_webView.InvokeRequired)
                    {
                        _webView.Invoke(new Action(() => NavigateToUrl(url)));
                        return;
                    }

                    if (_webView.CoreWebView2 == null)
                    {
                        _logger.LogWarning("CoreWebView2 not initialized, cannot navigate");
                        return;
                    }
                    if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        url = url.Replace("\\", "/");
                    }

                    if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    {
                        _logger.LogWarning($"Invalid URL format: {url}");
                        return;
                    }


                    if (_webView.Source != null && _webView.Source.ToString() == url)
                    {
                        _logger.Log("⚠️ Prevented duplicate navigation to the same URL.");
                        SetisNavigating(false);
                        return;
                    }

                    if (url.Contains("IdleScreen_") && url.StartsWith("file:///") && !string.IsNullOrEmpty(_idleScreenUrl))
                    {
                        if (Uri.IsWellFormedUriString(_idleScreenUrl, UriKind.Absolute) &&
                            (_idleScreenUrl.StartsWith("http://") || _idleScreenUrl.StartsWith("https://")))
                        {
                            _logger.Log($"🧭 Redirecting to original idle screen URL instead of local HTML: {_idleScreenUrl}");
                            url = _idleScreenUrl;
                        }
                    }

                    _webView.CoreWebView2.Navigate(url);
                    _playerForm?.UpdateCurrentUrl(url);
                }
                catch (Exception navEx)
                {
                    _logger.LogError($"Navigation internal error for URL '{url}'", navEx);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Comprehensive navigation error for URL '{url}'", ex);
            }
            finally
            {
                SetisNavigating(false);
                SetSystemOperationInProgress(false);
            }
        }

        public async Task HandleWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string rawMessage = args.WebMessageAsJson;
                _logger.Log($"📥 Raw web message received: {rawMessage}");

                if (string.IsNullOrWhiteSpace(rawMessage))
                {
                    _logger.LogWarning("Received empty or invalid web message.");
                    return;
                }

                JsonSerializerOptions options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                WebMessage message = JsonSerializer.Deserialize<WebMessage>(rawMessage, options);

                if (message == null || string.IsNullOrEmpty(message.Action))
                {
                    _logger.LogWarning("Received null or malformed web message.");
                    return;
                }

                _logger.Log($"✅ WebView2 message action: {message.Action}");

                switch (message.Action)
                {
                    case "navigateToUrl":
                        if (!string.IsNullOrEmpty(message.Url) && Uri.IsWellFormedUriString(message.Url, UriKind.Absolute))
                        {
                            string macAddress = _networkService.GetMacAddress();

                            _lastSuccessfulUrl = message.Url;
                            SaveUrlToCache(message.Url);
                            _playerForm?.UpdateCurrentUrl(message.Url);
                            try
                            {
                                DeviceConfig screenConfig = await _apiService.GetScreenConfigAsync(macAddress);
                                if (screenConfig != null)
                                {
                                    _logger.Log($"Retrieved latest config before navigation: URL={screenConfig.Url}");
                                    _apiService.SetLastRetrievedConfig(screenConfig);
                                    _idleService.SetLastSuccessfulUrl(message.Url);
                                }
                            }
                            catch (Exception configEx)
                            {
                                _logger.LogError($"Error updating config: {configEx.Message}");
                            }

                            await Task.Delay(500);
                            NavigateToUrl(message.Url);
                        }
                        break;
                    case "retryConnection":
                        _logger.Log("🔄 Retry connection requested from web interface");
                        await RetryApiConnection();
                        break;

                    case "log":
                        string level = message.Level ?? "info";
                        string logMessage = message.Message ?? "";
                        if (!string.IsNullOrEmpty(logMessage))
                        {
                            _logger.Log($"🌐 WebView Log [{level}]: {logMessage}");
                        }
                        if (logMessage.Contains("Device registered successfully"))
                        {
                            try
                            {
                                if (_maintenanceService != null)
                                {
                                    (_maintenanceService as MaintenanceService)?.NotifyRegistrationCompleted();
                                    _logger.Log("✅ Notified maintenance service of successful registration");
                                }
                            }
                            catch (Exception maintenanceEx)
                            {
                                _logger.LogError("Error notifying maintenance service of registration", maintenanceEx);
                            }
                        }
                        break;
                    case "setStartupConfig":
                        bool enableStartup = message.Enabled == true;
                        _logger.Log($"[WebView2] Applying startup config: {enableStartup}");
                        StartupHandler.ApplyStartupSetting(enableStartup, _logger);
                        AppConfiguration.Instance.StartupConfiguration = enableStartup
                            ? AppConfiguration.StartupConfigState.Enabled
                            : AppConfiguration.StartupConfigState.Disabled;
                        AppConfiguration.Instance.SaveSettings();
                        break;


                    default:
                        _logger.Log($"⚠️ Unknown WebView message action received: {message.Action}");
                        break;
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError($"JSON parsing error: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Comprehensive error handling web message", ex);
            }
        }
        [System.Runtime.InteropServices.ComVisible(true)]
        public class PageBridge
        {
            private readonly PageManager _pageManager;

            public PageBridge(PageManager pageManager)
            {
                this._pageManager = pageManager;
            }
        }

        public IdleService GetIdleService()
        {
            return _idleService;
        }

        public Task UpdateRefreshIntervalAsync(int refreshIntervalMinutes)
        {
            try
            {
                IdleService idleService = GetIdleService();
                if (idleService == null)
                {
                    _logger.LogWarning("⚠️ IdleService not available, cannot update refresh interval");
                    return Task.CompletedTask;
                }
                idleService.UpdateRefreshInterval(refreshIntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error updating refresh interval", ex);
            }
            return Task.CompletedTask;
        }
        public string GetCurrentPageUrl()
        {
            try
            {
                if (_webView.InvokeRequired)
                {
                    string result = "";
                    _webView.Invoke(new Action(() =>
                    {
                        try
                        {
                            if (_webView.CoreWebView2 != null)
                            {
                                result = _webView.Source?.ToString() ?? string.Empty;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error accessing CoreWebView2 on UI thread: {ex.Message}");
                        }
                    }));
                    return result;
                }
                if (_webView.CoreWebView2 == null)
                {
                    _logger.LogWarning("⚠️ WebView2 is not initialized, returning empty URL.");
                    return string.Empty;
                }

                string currentUrl = _webView.Source?.ToString();
                _logger.Log($"📄 Current page URL: {currentUrl}");
                return currentUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving current page URL: {ex.Message}", ex);
                return string.Empty;
            }
        }
        public void UpdateIdleScreenUrl(string newIdleScreenUrl)
        {
            if (string.IsNullOrEmpty(newIdleScreenUrl) || newIdleScreenUrl == _idleScreenUrl)
                return;

            try
            {
                _idleScreenUrl = newIdleScreenUrl;

                try
                {
                    File.WriteAllText(_config.UrlCachePath + "_idle", newIdleScreenUrl);
                }
                catch (IOException ioEx)
                {
                    _logger.LogError("IO error saving Idle Screen URL to cache", ioEx);
                }

                IdleService idleService = GetIdleService();
                if (idleService == null)
                {
                    _logger.LogWarning("⚠️ IdleService is null, cannot update idle screen URL");
                    return;
                }

                idleService.UpdateIdleScreenUrl(newIdleScreenUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError("Comprehensive error in UpdateIdleScreenUrl", ex);
            }
        }
        public async Task ShowRegistrationPage()
        {
            _logger.Log("📝 Showing registration page");

            try
            {
                AppConfiguration.Instance.LoadSettings();

                string macAddress = _networkService.GetMacAddress();

                string registrationPagePath = await Task.Run(() => HtmlRenderer.GenerateAndSaveRegistrationPage(
                    macAddress,
                    AppConfiguration.Instance.ResourcesFolder,
                    AppConfiguration.Instance.ApiBaseUrl));

                if (string.IsNullOrEmpty(registrationPagePath) || !File.Exists(registrationPagePath))
                {
                    _logger.LogError($"❌ Registration page could not be generated for {macAddress}");
                    return;
                }

                string registrationUrl = new Uri(registrationPagePath).AbsoluteUri;
                _logger.Log($"📄 Navigating to registration page: {registrationUrl}");

                NavigateToUrl(registrationUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error showing registration page", ex);
            }
        }
        public async Task NavigateToOfflineScreen()
        {
            try
            {
                _logger.Log("🌐 Generating and navigating to offline screen");

                string macAddress = _networkService.GetMacAddress();
                string offlineScreenPath = await Task.Run(() => HtmlRenderer.GenerateAndSaveOfflineScreen(
                    macAddress,
                    _config.ResourcesFolder,
                    _config.ApiBaseUrl,
                    _logger
                ));

                if (string.IsNullOrEmpty(offlineScreenPath) || !File.Exists(offlineScreenPath))
                {
                    _logger.LogError("❌ Failed to generate offline screen");
                    string cachedUrl = _config.MainUrl;
                    if (!string.IsNullOrEmpty(cachedUrl))
                    {
                        NavigateToUrl(cachedUrl);
                    }
                    return;
                }

                string offlineUrl = $"file:///{offlineScreenPath.Replace('\\', '/')}";
                _logger.Log($"📄 Navigating to offline screen: {offlineUrl}");

                NavigateToUrl(offlineUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error navigating to offline screen", ex);
            }
        }
        public void SetSystemOperationInProgress(bool value)
        {
            IsSystemOperationInProgress = value;
        }
        public async Task<bool> TestStatusUpdate(string status = "Online")
        {
            try
            {
                string macAddress = GetMacAddress();
                _logger.Log($"🧪 TEST: Manually triggering status update to '{status}' for device {macAddress}");

                bool result = await _apiService.SendStatusUpdateAsync(macAddress, status, UpdateIdleScreenUrl);

                if (result)
                    _logger.LogSuccess($"✅ TEST: Status update to '{status}' was successful");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ TEST: Status update to '{status}' failed due to {ex}");
                return false;
            }
        }
        public async Task RetryApiConnection()
        {
            if (_isHandlingApiCheck) return;

            try
            {
                _isHandlingApiCheck = true;
                _logger.Log("🔄 Retrying API connection");

                bool apiAvailable = await _apiService.CheckApiHealthAsync();

                if (apiAvailable)
                {
                    _logger.LogSuccess("API connection restored");
                    await LoadConfiguredUrl();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrying API connection", ex);
            }
            finally
            {
                _isHandlingApiCheck = false;
            }
        }
        public async Task HandleApiOffline()
        {
            _logger.Log("API appears offline. Switching to offline mode.");
            await Task.Delay(10);
            _isApiOffline = true;

            try
            {
                _logger.Log("API appears offline. Switching to offline mode.");
                _logger.Log("🧪 PageManager is about to call EnterOfflineMode()");
                _playerForm?.TriggerOfflineModeIfNotAlready();



            }
            catch (Exception ex)
            {
                _logger.LogError("Error while handling API offline fallback", ex);
            }
        }


        public void HideOfflineIndicator()
        {
            _isApiOffline = false;
            _logger.Log("✅ Offline state cleared (no visual indicator to hide)");
        }

        public void Dispose()
        {
            try
            {
                _webView.NavigationCompleted -= WebView_NavigationCompleted;

            }
            catch (Exception ex)
            {
                _logger.LogError("Error disposing PageManager", ex);
            }
        }

        private class WebMessage
        {
            public string Action { get; set; }
            public string Url { get; set; }
            public string Message { get; set; }
            public string MacAddress { get; set; }
            public string Status { get; set; }
            public string Level { get; set; }
            public bool? Enabled { get; set; }
        }
    }
}