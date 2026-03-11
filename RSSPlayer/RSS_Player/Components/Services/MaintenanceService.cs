using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using RssPlayer.Components.Utilities;
using static RssPlayer.Components.Services.ApiService;

namespace RssPlayer.Components.Services
{
    public class MaintenanceService : IDisposable
    {
        private readonly ApiService _apiService;
        private readonly LoggingService _logger;
        private readonly NetworkService _networkService;
        private readonly HttpClient _httpClient;
        private readonly Action<string> _navigateAction;
        private readonly PlayerForm _playerForm;
        private readonly AppConfiguration _config;
        private readonly IdleService _idleService;

        private readonly object _registrationLock = new object();

        private readonly bool _isIdle = false;

        private System.Timers.Timer _maintenanceTimer;

        private CancellationTokenSource _cancellationTokenSource;

        private string _lastSuccessfulUrl = "";

        private DeviceConfig _lastRetrievedConfig;


        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private DateTime _registrationTime = DateTime.MinValue;


        private bool _isDeviceKnownToBeRegistered;
        private bool _isFirstCheckAfterRegistration = true;
        private bool _isWaitingForRegistration;
        private bool _pendingStartupRegistration = false;
        private bool _justRegistered = false;

        private int _refreshInterval = 60;
        private const int REGISTRATION_GRACE_PERIOD_MINUTES = 2;
        public Action<string> NavigateWebView { get; }

        public MaintenanceService(
            ApiService apiService,
            LoggingService logger,
            NetworkService networkService,
            HttpClient httpClient,
            PlayerForm playerForm,
            IdleService idleService,
            Action<string> navigateAction)
        {
            _playerForm = playerForm;
            _apiService = apiService;
            _logger = logger;
            _networkService = networkService;
            _httpClient = httpClient;
            _idleService = idleService;
            _navigateAction = navigateAction;
            _config = AppConfiguration.Instance;
        }

        public MaintenanceService(ApiService apiService, LoggingService logger, NetworkService networkService, HttpClient httpClient, PlayerForm playerForm, IdleService idleService, AppConfiguration appConfiguration, Action<string> navigateWebView)
        {
            _apiService = apiService;
            _logger = logger;
            _networkService = networkService;
            _httpClient = httpClient;
            _playerForm = playerForm;
            _idleService = idleService;
            _config = appConfiguration;
            NavigateWebView = navigateWebView;
        }

        public void StartMonitoring(int intervalMinutes = 0)
        {
            try
            {
                if (intervalMinutes <= 0)
                {
                    intervalMinutes = _config.MaintenanceIntervalMinutes;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _refreshInterval = intervalMinutes * 60;

                _maintenanceTimer = new System.Timers.Timer
                {
                    Interval = intervalMinutes * 60 * 1000,
                    AutoReset = true
                };

                _maintenanceTimer.Elapsed += async (sender, e) => await HandleMaintenanceTimerElapsed();

                _maintenanceTimer.Start();

                _logger.Log($"🛠 MaintenanceService started. Interval: {intervalMinutes} minutes");
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Failed to start MaintenanceService", ex);
            }
        }

        public void StopMonitoring()
        {
            try
            {
                _logger.Log("Stopping monitoring services...");

                _maintenanceTimer?.Stop();
                _maintenanceTimer?.Dispose();
                _maintenanceTimer = null;

                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                    _logger.Log("Background tasks canceled.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error stopping monitoring service", ex);
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _logger.Log("Monitoring service fully stopped.");
            }
        }

        private async Task HandleMaintenanceTimerElapsed()
        {
            try
            {
                await SendPingToApiAsync();
                if (!(_playerForm?.IdleService?.IsIdleServicePaused ?? false))
                {
                    return;
                }

                if (_playerForm?.PageManager?.IsNavigating ?? false)
                {
                    _logger.Log("🚧 Navigation in progress. Delaying maintenance.");
                    await Task.Delay(2000);
                    return;
                }

                await RunMaintenanceChecks();
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error during maintenance timer elapsed", ex);
            }
        }

        private async Task RunMaintenanceChecks()
        {
            try
            {
                if (!_playerForm.IdleService.IsIdleServicePaused)
                {
                    return;
                }

                _playerForm.IdleService.SetMaintenanceRunning(true);

                if (_playerForm.IdleService.IsProgrammaticRefresh)
                {
                    return;
                }

                if (_playerForm.PageManager.IsNavigating)
                {
                    _logger.Log("⏳ Navigation in progress, delaying maintenance by 2s...");
                    await Task.Delay(2000);
                }

                _logger.Log("🔄 Performing maintenance checks...");

                await CheckDeviceRegistrationStatusAsync();

                if (_isDeviceKnownToBeRegistered)
                {
                    await HandleDeviceConfiguration();
                }

                await CheckApiHealth();

                if (ShouldUpdateStatus())
                {
                    await UpdatePlayerStatus();
                    _lastStatusUpdate = DateTime.UtcNow;
                }

                await TriggerMaintenanceRefresh();
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error during maintenance checks", ex);
            }
            finally
            {
                _playerForm.IdleService.SetMaintenanceRunning(false);
            }
        }

        private async Task TriggerMaintenanceRefresh()
        {
            try
            {
                if (_playerForm.IdleService.IsIdle)
                {
                    _logger.Log("⏳ User is idle. Forcing exit and re-enter of idle mode to refresh state.");
                    try
                    {
                        await _playerForm.IdleService.ExitIdleModeAsync();
                        await _playerForm.IdleService.EnterIdleModeAsync();

                        _playerForm.IdleService.ForceIdleStateReset();
                        _logger.Log("✅ Successfully refreshed idle state.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("🚨 Error refreshing idle state", ex);
                        _playerForm.IdleService.ResumeIdleTimerAfterRefresh();
                    }
                    return;
                }

                if (_playerForm.PageManager.IsApiOffline)
                {
                    _logger.Log("⚠️ App is in offline mode. Skipping content refresh.");
                    return;
                }

                bool isApiHealthy = await _apiService.CheckApiHealthAsync();
                if (!isApiHealthy)
                {
                    _logger.Log("⚠️ API health check failed. Skipping content refresh.");
                    await _playerForm.PageManager.HandleApiOffline();
                    return;
                }

                string currentUrl = _playerForm?.PageManager?.GetCurrentPageUrl();
                string idleScreenUrl = AppConfiguration.Instance.IdleScreenUrl;

                if (!string.IsNullOrEmpty(currentUrl) && currentUrl.Equals(idleScreenUrl, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Log("🚨 On idle screen but not in idle state. Forcing idle exit.");
                    await _playerForm.IdleService.ExitIdleModeAsync();
                    _playerForm.IdleService.ForceIdleStateReset();
                    return;
                }

                await _playerForm.PageManager.LoadConfiguredUrl();

                _playerForm.IdleService.ResumeAfterMaintenance();
                _playerForm.IdleService.ForceIdleStateReset();

                _logger.Log("✅ Maintenance refresh completed, idle monitoring reset");
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error during maintenance refresh", ex);
                _playerForm.IdleService.ResumeIdleTimerAfterRefresh();
            }
        }
        public async Task PollUntilApiIsAvailable()
        {
            while (_playerForm?.PageManager?.IsApiOffline == true)
            {
                bool isHealthy = await _apiService.CheckApiHealthAsync();
                if (isHealthy)
                {
                    _logger.Log("✅ API is back online.");
                    _playerForm?.PageManager?.HideOfflineIndicator();

                    _playerForm?.ConfigMonitorService?.SetOfflinePolling(false);

                    await _playerForm?.PageManager?.LoadConfiguredUrl();
                    break;

                }
                if (_playerForm != null)
                {
                    _logger.Log("✅ API is back. Resetting offline popup flag.");
                    _playerForm.ResetOfflinePopupFlag();
                }


                _logger.Log("API still offline. Will retry in 60 seconds.");
                await Task.Delay(60000);
            }
        }

        private async Task HandleDeviceConfiguration()
        {
            try
            {
                DeviceConfig screenConfig = await FetchConfigIfNotIdle();
                if (_playerForm?.IdleService?.IsIdle ?? false)
                {
                    _logger.Log("🛑 Skipping refresh config update during idle.");
                    return;
                }

                if (screenConfig != null)
                {
                    if (ShouldUpdateConfiguration(screenConfig))
                    {
                        await ApplyNewConfiguration(screenConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in HandleDeviceConfiguration", ex);
            }
        }

        private bool ShouldUpdateConfiguration(DeviceConfig newConfig)
        {
            return _lastRetrievedConfig == null ||
                   (!string.Equals(_lastRetrievedConfig.Url, newConfig.Url, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_lastRetrievedConfig.IdleScreenUrl, newConfig.IdleScreenUrl, StringComparison.OrdinalIgnoreCase) ||
                    _lastRetrievedConfig.RefreshInterval != newConfig.RefreshInterval);
        }

        private async Task ApplyNewConfiguration(DeviceConfig newConfig)
        {
            try
            {
                _logger.Log("🔄 Configuration change detected. Applying updates...");

                if (!string.IsNullOrEmpty(newConfig.Url))
                {
                    AppConfiguration.Instance.SetMainUrl(newConfig.Url);
                }

                if (!string.IsNullOrEmpty(newConfig.IdleScreenUrl))
                {
                    AppConfiguration.Instance.SetIdleScreenUrl(newConfig.IdleScreenUrl);
                }

                try
                {
                    bool? startupEnabled = newConfig.StartupEnabled;

                    if (startupEnabled.HasValue && (AppConfiguration.Instance.StartupConfiguration == AppConfiguration.StartupConfigState.Unconfigured || _pendingStartupRegistration))
                    {
                        try
                        {
                            bool enableStartup = startupEnabled.Value;
                            _logger.Log($"🖥 StartupEnabled = {enableStartup}. Applying startup setting...");

                            StartupHandler.ApplyStartupSetting(enableStartup, _logger);
                            AppConfiguration.Instance.StartupConfiguration = enableStartup
                                ? AppConfiguration.StartupConfigState.Enabled
                                : AppConfiguration.StartupConfigState.Disabled;
                            AppConfiguration.Instance.SaveSettings();

                            _pendingStartupRegistration = false;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("🚨 Failed to apply startup setting", ex);
                        }
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError("⚠️ Failed to apply startup configuration", ex);
                }

                if (newConfig.RefreshInterval > 0 &&
                    newConfig.RefreshInterval != (_refreshInterval / 60))
                {
                    await UpdateRefreshInterval(newConfig.RefreshInterval);
                }

                NavigateToNewUrlIfNeeded(newConfig);

                _lastRetrievedConfig = newConfig;
                _apiService.SetLastRetrievedConfig(newConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error applying new configuration", ex);
            }
        }



        private async Task UpdateRefreshInterval(int newIntervalMinutes)
        {
            try
            {
                _logger.Log($"🕒 Updating refresh interval to {newIntervalMinutes} minutes");

                _refreshInterval = newIntervalMinutes * 60;
                _maintenanceTimer.Interval = newIntervalMinutes * 60 * 1000;

                await _playerForm.PageManager.UpdateRefreshIntervalAsync(newIntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error updating refresh interval", ex);
            }
        }

        private void NavigateToNewUrlIfNeeded(DeviceConfig newConfig)
        {
            try
            {
                string currentUrl = _playerForm.PageManager.GetCurrentPageUrl();

                if (!_playerForm.PageManager.IsIdle &&
                    !currentUrl.Contains("DeviceRegistration_") &&
                    !string.Equals(currentUrl, newConfig.Url, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Log($"🌐 Navigating to new configured URL: {newConfig.Url}");
                    _navigateAction(newConfig.Url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error navigating to new URL", ex);
            }
        }
        public async Task CheckDeviceRegistrationStatusAsync()
        {
            try
            {

                bool isApiHealthy = await _apiService.CheckApiHealthAsync();
                if (!isApiHealthy)
                {
                    _logger.Log("Skipping device registration check - API is offline");
                    return; 
                }

                string macAddress = _networkService.GetMacAddress();

                if (_justRegistered && (DateTime.UtcNow - _registrationTime).TotalMinutes < REGISTRATION_GRACE_PERIOD_MINUTES)
                {
                    _logger.Log($"⏳ In post-registration grace period ({REGISTRATION_GRACE_PERIOD_MINUTES} min). Skipping device existence check.");
                    _isDeviceKnownToBeRegistered = true;
                    await HandleDeviceRegistrationNavigation(macAddress, true);
                    return;
                }

                bool wasRegistered = _isDeviceKnownToBeRegistered;
                bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(macAddress);

                _isDeviceKnownToBeRegistered = isRegistered;
                bool isNewlyRegistered = isRegistered && !wasRegistered;

                if (isNewlyRegistered)
                {
                    _isFirstCheckAfterRegistration = true;

                    NotifyRegistrationCompleted();

                    await _apiService.SendStatusUpdateAsync(macAddress, "Online", _playerForm.PageManager.UpdateIdleScreenUrl);
                }

                await HandleDeviceRegistrationNavigation(macAddress, isRegistered);
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error checking device registration", ex);
            }
        }

        private async Task HandleDeviceRegistrationNavigation(string macAddress, bool isRegistered)
        {
            try
            {
                if (!isRegistered)
                {
                    await HandleUnregisteredDevice(macAddress);
                }
                if (_isFirstCheckAfterRegistration && !_playerForm.PageManager.IsIdle)
                {
                    _isFirstCheckAfterRegistration = false;

                    try
                    {
                        await FetchConfigAndUpdatePlayer();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error fetching config and updating player", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in HandleDeviceRegistrationNavigation", ex);
            }
        }
        public void NotifyRegistrationCompleted()
        {
            _justRegistered = true;
            _registrationTime = DateTime.UtcNow;
        }

        private async Task CheckApiHealth()
        {
            try
            {
                bool isHealthy = await _apiService.CheckApiHealthAsync();

                if (!isHealthy)
                {
                    _logger.Log("🧪 API health is bad. Maintaining offline mode.");
                    await _playerForm.PageManager.HandleApiOffline();
                    _playerForm?.TriggerOfflineModeIfNotAlready();
                    return;
                }

                if (_playerForm.PageManager.IsApiOffline)
                {
                    isHealthy = await _apiService.CheckApiHealthAsync();
                    if (!isHealthy)
                    {
                        _logger.Log("🧪 Second API health check failed. Maintaining offline mode.");
                        return;
                    }

                    try
                    {
                        string macAddress = _networkService.GetMacAddress();
                        bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(macAddress);

                        if (!isRegistered)
                        {
                            _logger.Log("Device not registered. Maintaining offline mode.");
                            return;
                        }

                        DeviceConfig config = await _apiService.GetScreenConfigAsync(macAddress);
                        if (config == null)
                        {
                            _logger.Log("Could not retrieve device configuration. Maintaining offline mode.");
                            return;
                        }

                        _logger.Log("✅ API confirmed healthy after multiple checks. Exiting offline mode.");
                        _playerForm.PageManager.HideOfflineIndicator();
                        await _playerForm.PageManager.LoadConfiguredUrl();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error during offline mode exit checks", ex);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 API Health Check Exception", ex);
                await _playerForm.PageManager.HandleApiOffline();
                _playerForm?.TriggerOfflineModeIfNotAlready();
            }
        }
        private bool ShouldUpdateStatus()
        {
            try
            {
                return (DateTime.UtcNow - _lastStatusUpdate).TotalMinutes >= _config.StatusUpdateIntervalMinutes;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking status update condition", ex);
                return false;
            }
        }

        private async Task UpdatePlayerStatus()
        {
            try
            {
                string macAddress = _networkService.GetMacAddress();
                await _apiService.SendStatusUpdateAsync(macAddress, "Online", _playerForm.PageManager.UpdateIdleScreenUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error updating player status", ex);
            }
        }
        private async Task SendPingToApiAsync()
        {
            try
            {
                string macAddress = _networkService.GetMacAddress();
                if (string.IsNullOrEmpty(macAddress))
                {
                    _logger.LogWarning("Cannot send ping: MAC address is null or empty");
                    return;
                }

                string status = _playerForm?.IdleService?.IsIdle ?? false ? "Idle" : "Online";
                bool success = await _apiService.SendStatusUpdateAsync(macAddress, status, null);

                if (!success)
                {
                    _logger.LogWarning("📡 Failed to send ping to API");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Error sending ping to API", ex);
            }
        }
        public void Dispose()
        {
            StopMonitoring();
            _cancellationTokenSource?.Dispose();
        }

        public async Task<DeviceConfig> FetchConfigAndUpdatePlayer()
        {
            string macAddress = _networkService.GetMacAddress();

            bool isApiHealthy = await _apiService.CheckApiHealthAsync();
            if (!isApiHealthy)
            {
                _logger.LogError("API is NOT healthy, using Cached URLs.");
                return null;
            }

            bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(macAddress);
            _isDeviceKnownToBeRegistered = isRegistered;

            if (!isRegistered)
            {
                await HandleUnregisteredDeviceForConfig(macAddress);
                return null;

            }

            return await ProcessRegisteredDevice(macAddress);
        }

        private async Task HandleUnregisteredDeviceForConfig(string macAddress)
        {
            try
            {
                lock (_registrationLock)
                {
                    if (_isWaitingForRegistration)
                    {
                        _logger.Log("Already awaiting registration. Skipping further checks.");
                        return;
                    }
                    _isWaitingForRegistration = true;
                }

                if (_playerForm.PageManager.IsApiOffline)
                {
                    _logger.Log("⚠️ API is offline. Skipping navigation to registration page.");
                    return;
                }

                if (!_playerForm.PageManager.IsIdle)
                {
                    string registrationPagePath = await Task.Run(() =>
                    {
                        try
                        {
                            return HtmlRenderer.GenerateAndSaveRegistrationPage(
                                macAddress,
                                _config.ResourcesFolder,
                                _config.ApiBaseUrl,
                                _logger
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Failed to generate registration page", ex);
                            return null;
                        }
                    });

                    if (string.IsNullOrEmpty(registrationPagePath))
                    {
                        _logger.LogError("Could not generate registration page");
                        return;
                    }

                    string registrationPageUrl = $"file:///{registrationPagePath.Replace('\\', '/')}";

                    try
                    {
                        await Task.Run(() => _navigateAction(registrationPageUrl));
                        _logger.Log("Registration page navigation invoked successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Navigation to registration failed, fallback triggered explicitly.", ex);
                        await Task.Run(() => _playerForm.PageManager.NavigateToUrl(registrationPageUrl));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error in HandleUnregisteredDeviceForConfig", ex);
            }
        }


        private async Task<DeviceConfig> ProcessRegisteredDevice(string macAddress)
        {
            DeviceConfig screenConfig = await _apiService.GetScreenConfigAsync(macAddress);

            if (screenConfig == null || string.IsNullOrWhiteSpace(screenConfig.Url))
            {
                _logger.LogWarning("Device registered but configuration is invalid/missing.");
                return null;
            }

            lock (_registrationLock)
            {
                _isWaitingForRegistration = false;
            }

            _lastSuccessfulUrl = screenConfig.Url;

            _apiService.SetLastRetrievedConfig(screenConfig);

            await _apiService.SendStatusUpdateAsync(macAddress, "Online", _playerForm.PageManager.UpdateIdleScreenUrl);

            return await HandleScreenConfigNavigation(screenConfig);
        }

        private async Task<DeviceConfig> HandleScreenConfigNavigation(DeviceConfig screenConfig)
        {
            try
            {
                if (_playerForm.PageManager.IsIdle)
                {
                    return await HandleIdleScreenConfig(screenConfig);
                }
                try
                {
                    _navigateAction(screenConfig.Url);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to navigate to URL: {screenConfig.Url}", ex);
                }
                return screenConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error in HandleScreenConfigNavigation", ex);
                return null;
            }
        }

        private async Task<DeviceConfig> HandleIdleScreenConfig(DeviceConfig screenConfig)
        {
            _logger.Log("User is idle, not navigating to the configured URL.");

            if (screenConfig.RefreshInterval > 0 && screenConfig.RefreshInterval != (_refreshInterval / 60))
            {
                await UpdateRefreshIntervalIfNeeded(screenConfig);
            }

            UpdateIdleScreenUrlIfNeeded(screenConfig);

            return screenConfig;
        }

        private async Task UpdateRefreshIntervalIfNeeded(DeviceConfig screenConfig)
        {
            int newIntervalMinutes = screenConfig.RefreshInterval;
            _refreshInterval = newIntervalMinutes * 60;

            _logger.Log($"Refresh interval updated from API: {newIntervalMinutes} minutes");

            if (_maintenanceTimer != null)
            {
                _maintenanceTimer.Interval = newIntervalMinutes * 60 * 1000;
                _logger.Log($"MaintenanceService timer updated to {newIntervalMinutes} minutes");
            }

            await _playerForm.PageManager.UpdateRefreshIntervalAsync(newIntervalMinutes);
        }

        private void UpdateIdleScreenUrlIfNeeded(DeviceConfig screenConfig)
        {
            if (!string.IsNullOrEmpty(screenConfig.IdleScreenUrl))
            {
                _playerForm.PageManager.UpdateIdleScreenUrl(screenConfig.IdleScreenUrl);
            }
        }

        private async Task HandleUnregisteredDevice(string macAddress)
        {
            try
            {
                if (!_playerForm.PageManager.IsIdle && !_isWaitingForRegistration)
                {
                    try
                    {
                        string registrationPagePath = await Task.Run(() => HtmlRenderer.GenerateAndSaveRegistrationPage(
                            macAddress,
                            _config.ResourcesFolder,
                            _config.ApiBaseUrl,
                            _logger
                        ));

                        string registrationPageUrl = $"file:///{registrationPagePath.Replace('\\', '/')}";

                        try
                        {
                            lock (_registrationLock)
                            {
                                _isWaitingForRegistration = true;
                            }

                            await Task.Run(() => _navigateAction(registrationPageUrl));
                            _logger.Log("Registration page navigation invoked successfully.");
                        }
                        catch (Exception navEx)
                        {
                            _logger.LogError("Navigation to registration failed, fallback triggered explicitly.", navEx);
                            await Task.Run(() => _playerForm.PageManager.NavigateToUrl(registrationPageUrl));
                        }
                    }
                    catch (Exception configEx)
                    {
                        _logger.LogError("Error generating registration page", configEx);
                    }
                }
                if (_playerForm.PageManager.IsIdle)
                {
                    _logger.Log("Device registration checked but staying on idle screen due to inactivity");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error in HandleUnregisteredDevice", ex);
            }
        }

        private async Task<DeviceConfig> FetchConfigIfNotIdle()
        {
            try
            {
                string macAddress = _networkService.GetMacAddress();
                DeviceConfig screenConfig = await _apiService.GetScreenConfigAsync(macAddress);

                if (screenConfig == null || string.IsNullOrWhiteSpace(screenConfig.Url))
                {
                    return null;
                }

                if (_playerForm.PageManager.IsIdle)
                {
                    _logger.Log("User is idle, not navigating to the configured URL.");

                    if (!string.IsNullOrEmpty(screenConfig.IdleScreenUrl))
                    {
                        _playerForm.PageManager.UpdateIdleScreenUrl(screenConfig.IdleScreenUrl);
                    }
                }
                if (!_isIdle)
                {
                    _lastSuccessfulUrl = screenConfig.Url;
                    _navigateAction(screenConfig.Url);
                }

                return screenConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching config", ex);
                return null;
            }
        }
    }
}