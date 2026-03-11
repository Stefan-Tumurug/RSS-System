using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using RssPlayer.Components.Utilities;
using static RssPlayer.Components.Services.ApiService;

namespace RssPlayer.Components.Services
{
    public class ConfigMonitorService : IDisposable
    {
        private readonly ApiService _apiService;
        private readonly LoggingService _logger;
        private readonly NetworkService _networkService;
        private readonly PlayerForm _playerForm;
        private readonly AppConfiguration _config;
        private readonly MaintenanceService _maintenanceService;

        private DeviceConfig _lastCheckedConfig;

        private System.Timers.Timer _configCheckTimer;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isStarted = false;
        private bool _isOfflinePollingActive = false;

        private int _checkIntervalSeconds = 5;

        public ConfigMonitorService(
            ApiService apiService,
            LoggingService logger,
            NetworkService networkService,
            PlayerForm playerForm,
            MaintenanceService maintenanceService)
        {
            _apiService = apiService;
            _logger = logger;
            _networkService = networkService;
            _playerForm = playerForm;
            _config = AppConfiguration.Instance;
            _maintenanceService = maintenanceService;
        }

        public void StartMonitoring(int checkIntervalSeconds = 0)
        {
            try
            {
                if (_isStarted)
                {
                    StopMonitoring();
                }

                if (checkIntervalSeconds > 0)
                {
                    _checkIntervalSeconds = checkIntervalSeconds;
                }

                _cancellationTokenSource = new CancellationTokenSource();

                _configCheckTimer = new System.Timers.Timer
                {
                    Interval = _checkIntervalSeconds * 1000,
                    AutoReset = true
                };

                _configCheckTimer.Elapsed += async (sender, e) => await CheckConfigurationChangesAsync();
                _configCheckTimer.Start();

                _isStarted = true;
                _logger.Log($"Config monitoring service started. Check interval: {_checkIntervalSeconds} seconds");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to start configuration monitoring service", ex);
            }
        }

        public void StopMonitoring()
        {
            try
            {
                if (_configCheckTimer != null)
                {
                    _configCheckTimer.Stop();
                    _configCheckTimer.Dispose();
                    _configCheckTimer = null;
                }

                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }

                _isStarted = false;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error stopping configuration monitoring service", ex);
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        private async Task CheckConfigurationChangesAsync()
        {
            try
            {
                if (_isOfflinePollingActive)
                {
                    return;
                }

                string macAddress = _networkService.GetMacAddress();
                if (string.IsNullOrEmpty(macAddress))
                {
                    return;
                }

                bool isApiHealthy = await _apiService.CheckApiHealthAsync();
                if (!isApiHealthy)
                {
                    _logger.Log("❌ API health check failed. Maintaining offline mode.");
                    _playerForm?.Invoke(new Action(() =>
                    {
                        _playerForm.EnterOfflineMode();
                    }));
                    return;
                }

                bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(macAddress);
                if (!isRegistered)
                {
                    _logger.LogWarning("❌ Device not registered. Maintaining offline mode.");
                    return;
                }

                DeviceConfig newConfig = await _apiService.GetScreenConfigAsync(macAddress);
                if (newConfig == null)
                {
                    _logger.LogWarning("API returned null configuration. Maintaining offline mode.");
                    bool confirmApiUnhealthy = await _apiService.CheckApiHealthAsync();
                    if (!confirmApiUnhealthy)
                    {
                        _logger.LogWarning("API health confirmed offline. Maintaining offline mode.");
                        _playerForm?.TriggerOfflineModeIfNotAlready();
                    }
                    return;
                }

                if (_playerForm?.PageManager?.IsApiOffline == true)
                {
                    _playerForm?.PageManager?.HideOfflineIndicator();
                    await _playerForm?.PageManager?.LoadConfiguredUrl();
                }
                if (HasConfigurationChanged(newConfig))
                {
                    _logger.Log("Configuration change detected. Applying updates immediately.");
                    await ApplyConfigurationChanges(newConfig);
                }
                _lastCheckedConfig = newConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception during config monitoring.", ex);

                try
                {
                    bool isHealthy = await _apiService.CheckApiHealthAsync();
                    if (!isHealthy)
                    {
                        _logger.Log("API confirmed offline during exception handling. Entering offline mode.");
                        _playerForm?.Invoke(new Action(() =>
                        {
                            _playerForm.EnterOfflineMode();
                        }));
                    }

                    if (isHealthy)
                    {
                        _logger.LogWarning("Exception occurred but API is healthy. Not entering offline mode.");
                    }
                }
                catch (Exception healthEx)
                {
                    _logger.LogError("Failed to check API health during exception handling. Assuming offline.", healthEx);
                    _playerForm?.Invoke(new Action(() =>
                    {
                        _playerForm.EnterOfflineMode();
                    }));
                }
            }
        }
        private bool HasConfigurationChanged(DeviceConfig newConfig)
        {
            if (_lastCheckedConfig == null)
            {
                return true; 
            }

            bool urlChanged = !string.Equals(_lastCheckedConfig.Url, newConfig.Url, StringComparison.OrdinalIgnoreCase);
            bool idleScreenUrlChanged = !string.Equals(_lastCheckedConfig.IdleScreenUrl, newConfig.IdleScreenUrl, StringComparison.OrdinalIgnoreCase);
            bool refreshIntervalChanged = _lastCheckedConfig.RefreshInterval != newConfig.RefreshInterval;
            bool autoRestartChanged = _lastCheckedConfig.AutoRestart != newConfig.AutoRestart;
            bool startupEnabledChanged = _lastCheckedConfig.StartupEnabled != newConfig.StartupEnabled;

            if (urlChanged || idleScreenUrlChanged || refreshIntervalChanged || autoRestartChanged || startupEnabledChanged)
            {
                _logger.Log($"Config changes detected: URL={urlChanged}, IdleURL={idleScreenUrlChanged}, Refresh={refreshIntervalChanged}, Restart={autoRestartChanged}, Startup={startupEnabledChanged}");
            }

            return urlChanged || idleScreenUrlChanged || refreshIntervalChanged ||
                   autoRestartChanged || startupEnabledChanged;
        }
        private async Task ApplyConfigurationChanges(DeviceConfig newConfig)
        {
            try
            {
                if (_lastCheckedConfig != null)
                {
                    LogConfigurationChanges(_lastCheckedConfig, newConfig);
                }

                if (!string.IsNullOrEmpty(newConfig.Url))
                {
                    _config.SetMainUrl(newConfig.Url);
                }

                bool idleScreenUrlChanged = false;
                if (!string.IsNullOrEmpty(newConfig.IdleScreenUrl) &&
                    !string.Equals(_lastCheckedConfig?.IdleScreenUrl, newConfig.IdleScreenUrl, StringComparison.OrdinalIgnoreCase))
                {
                    idleScreenUrlChanged = true;
                    _config.SetIdleScreenUrl(newConfig.IdleScreenUrl);
                    _playerForm.PageManager.UpdateIdleScreenUrl(newConfig.IdleScreenUrl);
                }

                if (idleScreenUrlChanged)
                {
                    if (_playerForm.IdleService.IsIdle &&
                        _playerForm.CurrentUrl == AppConfiguration.Instance.MainUrl)
                    {
                        _logger.Log("Forcing re-entry to idle mode due to idle screen URL change");
                        _playerForm.IdleService.ForceIdleStateReset();
                    }
                }
                if (newConfig.StartupEnabled.HasValue)
                {
                    bool enableStartup = newConfig.StartupEnabled.Value;
                    StartupHandler.ApplyStartupSetting(enableStartup, _logger);

                    if (enableStartup)
                    {
                        _config.StartupConfiguration = AppConfiguration.StartupConfigState.Enabled;
                    }

                    if (!enableStartup)
                    {
                        _config.StartupConfiguration = AppConfiguration.StartupConfigState.Disabled;
                    }

                    _config.SaveSettings();
                }


                _apiService.SetLastRetrievedConfig(newConfig);


                if (_playerForm.IdleService.IsIdle)
                {
                    if (idleScreenUrlChanged)
                    {
                        _logger.Log("Updating idle screen while in idle mode");
                        _playerForm.PageManager.NavigateToUrl(newConfig.IdleScreenUrl);
                    }

                    if (!idleScreenUrlChanged)
                    {
                        _logger.Log("Configuration updated while in idle mode. Changes will apply when exiting idle.");
                    }
                }

                if (!_playerForm.IdleService.IsIdle && !_playerForm.PageManager.IsNavigating)
                {
                    _playerForm.PageManager.NavigateToUrl(newConfig.Url);
                }
                if (idleScreenUrlChanged && _playerForm.IdleService.IsIdle)
                {
                    _logger.Log("Idle screen changed while in idle mode. Re-entering idle mode to apply changes.");
                    await _playerForm.IdleService.ExitIdleModeAsync();
                    await _playerForm.IdleService.EnterIdleModeAsync();

                }
                if (newConfig.RefreshInterval > 0)
                {
                    await _playerForm.PageManager.UpdateRefreshIntervalAsync(newConfig.RefreshInterval);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error applying configuration changes", ex);
            }
        }

        private void LogConfigurationChanges(DeviceConfig oldConfig, DeviceConfig newConfig)
        {
            if (!string.Equals(oldConfig.Url, newConfig.Url, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Log($"Main URL changed: {oldConfig.Url} -> {newConfig.Url}");
            }

            if (!string.Equals(oldConfig.IdleScreenUrl, newConfig.IdleScreenUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Log($"Idle Screen URL changed: {oldConfig.IdleScreenUrl} -> {newConfig.IdleScreenUrl}");
            }

            if (oldConfig.RefreshInterval != newConfig.RefreshInterval)
            {
                _logger.Log($"Refresh Interval changed: {oldConfig.RefreshInterval} -> {newConfig.RefreshInterval}");
            }

            if (oldConfig.AutoRestart != newConfig.AutoRestart)
            {
                _logger.Log($"Auto Restart changed: {oldConfig.AutoRestart} -> {newConfig.AutoRestart}");
            }

            if (oldConfig.StartupEnabled != newConfig.StartupEnabled)
            {
                _logger.Log($"Startup Setting changed: {oldConfig.StartupEnabled} -> {newConfig.StartupEnabled}");
            }
        }
        public void SetOfflinePolling(bool isPolling)
        {
            _isOfflinePollingActive = isPolling;
        }

        public void Dispose()
        {
            StopMonitoring();

            _cancellationTokenSource?.Dispose();
        }
    }
}