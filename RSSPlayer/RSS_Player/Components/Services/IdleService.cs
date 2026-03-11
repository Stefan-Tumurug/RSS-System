using System;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using RssPlayer.Components.Utilities;
using static RssPlayer.Components.Services.ApiService;
using Timer = System.Timers.Timer;

namespace RssPlayer.Components.Services
{
    public class IdleService
    {
        private enum IdleServiceState
        {
            Active,
            Idle,
            ProgrammaticRefresh,
            Maintenance,
            Suspended
        }

        private IdleServiceState idleState = IdleServiceState.Active;

        private PageManager pageManager;
        private readonly WebView2 webView;
        private readonly LoggingService logger;
        private readonly NetworkService networkService;
        private readonly ApiService apiService;
        private readonly ActivityHandler activityHandler;

        private Timer idleTimer;
        private DateTime lastActivityTime;
        private double idleTimeoutMilliseconds;
        private string idleScreenUrl = string.Empty;
        private string lastSuccessfulUrl = string.Empty;
        private bool activityDuringMaintenance = false;

        public bool IsIdle => idleState == IdleServiceState.Idle;
        public bool IsIdleServicePaused => idleState == IdleServiceState.Suspended;
        public bool IsProgrammaticRefresh => idleState == IdleServiceState.ProgrammaticRefresh;
        public DateTime LastActivityTime => lastActivityTime;
        public double IdleTimeoutMilliseconds => idleTimeoutMilliseconds;

        public IdleService(PageManager pageManagerInstance, WebView2 webViewInstance, LoggingService loggerInstance, NetworkService networkServiceInstance, ApiService apiServiceInstance)
        {
            pageManager = pageManagerInstance;
            webView = webViewInstance;
            logger = loggerInstance;
            networkService = networkServiceInstance;
            apiService = apiServiceInstance;

            idleScreenUrl = AppConfiguration.Instance.IdleScreenUrl;
            lastActivityTime = DateTime.Now;
            UpdateRefreshInterval(AppConfiguration.Instance.MaintenanceIntervalMinutes);

            Form parentForm = webView.FindForm();
            activityHandler = new ActivityHandler(parentForm, webView, OnUserActivity);
        }

        public void SetPageManager(PageManager manager)
        {
            pageManager = manager;
        }

        public PageManager GetPageManager()
        {
            return pageManager;
        }

        public void UpdateRefreshInterval(int refreshIntervalMinutes)
        {
            idleTimeoutMilliseconds = refreshIntervalMinutes * 60 * 1000 - 2000;

            idleTimer?.Stop();

            idleTimer = new Timer(idleTimeoutMilliseconds);
            idleTimer.Elapsed += (sender, args) => CheckForActivity();
            idleTimer.AutoReset = false;
        }

        public void UpdateIdleScreenUrl(string newIdleScreenUrl)
        {
            if (string.IsNullOrEmpty(newIdleScreenUrl) == false)
            {
                idleScreenUrl = newIdleScreenUrl;
            }
        }

        public void SetLastSuccessfulUrl(string url)
        {
            if (string.IsNullOrEmpty(url) == false && url.Contains("IdleScreen_") == false)
            {
                lastSuccessfulUrl = url;
            }
        }

        public bool IsIdleScreenUrl(string url)
        {
            return string.Equals(url, idleScreenUrl, StringComparison.OrdinalIgnoreCase);
        }

        public void StartMonitoring()
        {
            activityHandler.Register();
            idleTimer?.Start();
        }

        public async void CheckForActivity()
        {
            if (idleState == IdleServiceState.ProgrammaticRefresh)
            {
                return;
            }

            if (idleState == IdleServiceState.Maintenance)
            {
                return;
            }

            TimeSpan inactivityDuration = DateTime.Now - lastActivityTime;

            if (inactivityDuration.TotalMilliseconds >= idleTimeoutMilliseconds)
            {
                await EnterIdleModeAsync();
                return;
            }

            logger.Log("[IdleService] Activity detected. Pausing Idle Timer until next refresh.");
            idleState = IdleServiceState.Suspended;
            idleTimer?.Stop();
        }

        private void OnUserActivity()
        {
            try
            {
                lastActivityTime = DateTime.Now;

                if (idleState == IdleServiceState.Maintenance)
                {
                    activityDuringMaintenance = true;
                }

                if (idleState == IdleServiceState.Idle)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ExitIdleModeAsync();
                            idleState = IdleServiceState.Suspended;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError("[IdleService] Error while exiting idle mode", ex);
                            idleState = IdleServiceState.Active;
                            activityHandler.Register();
                        }
                    });
                    return;
                }

                idleState = IdleServiceState.Suspended;
            }
            catch (Exception ex)
            {
                logger.LogError("[IdleService] Error in OnUserActivity", ex);
                idleState = IdleServiceState.Active;
                activityHandler.Register();
            }
        }
        public void HandleNavigationEvent(string url)
        {
            activityHandler.Register();

            if (string.IsNullOrEmpty(url) == false)
            {
                idleTimer?.Stop();
                idleTimer?.Start();
            }
        }

        public async Task EnterIdleModeAsync()
        {
            if (idleState == IdleServiceState.Idle)
            {
                logger.Log("[IdleService] Already in idle mode, re-registering activity handler.");
                activityHandler.Register();
                return;
            }

            bool apiHealthy = await apiService.CheckApiHealthAsync();

            if (apiHealthy == false)
            {
                logger.Log("[IdleService] API health check failed - skipping idle screen navigation");
                idleState = IdleServiceState.Idle;
                return;
            }

            activityHandler.Register();
            idleState = IdleServiceState.ProgrammaticRefresh;
            logger.Log("[IdleService] Entering idle mode...");

            string macAddress = networkService.GetMacAddress();
            await apiService.SendStatusUpdateAsync(macAddress, "Idle", null);

            if (webView.IsHandleCreated && webView.IsDisposed == false)
            {
                webView.Invoke((MethodInvoker)(() =>
                {
                    pageManager.SetSystemOperationInProgress(true);
                    pageManager.NavigateToUrl(idleScreenUrl);
                    pageManager.SetSystemOperationInProgress(false);
                    idleState = IdleServiceState.Idle;
                }));
            }
        }
        public async Task ExitIdleModeAsync()
        {
            if (idleState != IdleServiceState.Idle)
            {
                return;
            }

            logger.Log("[IdleService] Exiting idle mode");
            idleState = IdleServiceState.Suspended;

            try
            {
                bool apiHealthy = await apiService.CheckApiHealthAsync();
                if (apiHealthy == false)
                {
                    logger.Log("[IdleService] API health check failed - skipping exit actions");
                    return;
                }

                string macAddress = networkService.GetMacAddress();
                DeviceConfig config = await apiService.GetScreenConfigAsync(macAddress);

                if (config != null && string.IsNullOrWhiteSpace(config.Url) == false)
                {
                    string currentUrl = pageManager.GetCurrentPageUrl();
                    if (string.Equals(currentUrl, config.Url, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        pageManager.NavigateToUrl(config.Url);
                    }

                    lastSuccessfulUrl = config.Url;
                    bool statusUpdateSuccess = await apiService.SendStatusUpdateAsync(macAddress, "Online", null);

                    if (statusUpdateSuccess)
                    {
                        logger.Log("[IdleService] Online status update successful after exiting idle.");
                    }

                    if (!statusUpdateSuccess)
                    {
                        logger.LogWarning("[IdleService] Failed to update status to Online after exiting idle.");
                    }
                }

                activityHandler.Register();
            }
            catch (Exception ex)
            {
                logger.LogError($"[IdleService] Error while exiting idle mode: {ex.Message}");
            }
        }

        public void SetMaintenanceRunning(bool isRunning)
        {
            if (isRunning)
            {
                logger.Log("IdleService: Maintenance started");
                idleState = IdleServiceState.Maintenance;
                idleTimer?.Stop();
                return;
            }
            idleState = IdleServiceState.Suspended;
            ResumeIdleTimerAfterRefresh();

            if (activityDuringMaintenance)
            {
                activityDuringMaintenance = false;
                ExitIdleModeAsync().ConfigureAwait(false);
            }
        }

        public void ForceIdleStateReset()
        {
            try
            {
                logger.Log("[IdleService] Forcing idle state reset");

                idleState = IdleServiceState.Active;

                lastActivityTime = DateTime.Now;

                activityHandler.Register();

                idleTimer?.Stop();
                idleTimer?.Start();

                string currentUrl = pageManager.GetCurrentPageUrl();
                if (currentUrl.Contains("IdleScreen_") || IsIdleScreenUrl(currentUrl))
                {
                    logger.Log("[IdleService] Detected idle screen during force reset - scheduling URL reload");
                    Task.Run(async () => {
                        try
                        {
                            await Task.Delay(500);
                            await pageManager.LoadConfiguredUrl();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError("[IdleService] Error loading configured URL during force reset", ex);
                        }
                    });
                }

                logger.Log("[IdleService] Idle state reset completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError("[IdleService] Error during idle state reset", ex);

                idleState = IdleServiceState.Active;
                activityHandler.Register();
                idleTimer?.Start();
            }
        }

        public void ResumeIdleTimerAfterRefresh()
        {
            idleState = IdleServiceState.Active;
            activityHandler.Register();
            idleTimer?.Stop();
            idleTimer?.Start();
        }

        public void ResumeAfterMaintenance()
        {
            try
            {
                logger.Log("[IdleService] Resuming after maintenance");

                idleState = IdleServiceState.Active;

                lastActivityTime = DateTime.Now;

                activityHandler.Register();

                idleTimer?.Stop();
                idleTimer?.Start();

                string currentUrl = pageManager.GetCurrentPageUrl();
                if ((idleState != IdleServiceState.Idle) &&
                    (currentUrl.Contains("IdleScreen_") || IsIdleScreenUrl(currentUrl)))
                {
                    logger.Log("[IdleService] Detected we're on idle screen but not in idle state - correcting");
                    Task.Run(async () => {
                        try
                        {
                            await Task.Delay(500); 
                            await pageManager.LoadConfiguredUrl();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError("[IdleService] Error loading URL during maintenance resume", ex);
                        }
                    });
                }

                logger.Log("[IdleService] Successfully resumed after maintenance");
            }
            catch (Exception ex)
            {
                logger.LogError("[IdleService] Error resuming after maintenance", ex);

                idleState = IdleServiceState.Active;
                activityHandler.Register();
            }
        }
    }
}
