using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using RssPlayer.Components.Utilities;
using static RssPlayer.Components.Services.ApiService;
using MessageBoxBtns = System.Windows.Forms.MessageBoxButtons;
namespace RssPlayer
{
    public partial class PlayerForm : Form
    {
        private readonly ApiService _apiService;
        private readonly NetworkService _networkService;
        private readonly LoggingService _logger;
        private readonly MaintenanceService _maintenanceService;
        private readonly HttpClient _httpClient;
        private readonly AppConfiguration _config;
        private readonly ConfigMonitorService _configMonitorService;
        public PageManager PageManager { get; private set; }
        public string CurrentUrl { get; private set; }

        private bool _isShuttingDown = false;
        private const int HOTKEY_ID = 1;
        private const int TEST_HOTKEY_ID = 2;
        private const int MOD_CTRL = 0x2;
        private const int MOD_SHIFT = 0x4;
        private const int VK_X = 0x58;
        public IdleService IdleService { get; private set; }
        public ConfigMonitorService ConfigMonitorService { get; private set; }

        private bool _offlinePopupShown = false;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public PlayerForm(
            ApiService apiService,
            NetworkService networkService,
            LoggingService logger)
        {
            InitializeComponent();

            this._apiService = apiService;
            this._networkService = networkService;
            this._logger = logger;
            _httpClient = new HttpClient();
            _config = AppConfiguration.Instance;

            this.Text = "Remote Screen";
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;

            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CTRL | MOD_SHIFT, VK_X);
            RegisterHotKey(this.Handle, TEST_HOTKEY_ID, MOD_CTRL | MOD_SHIFT, (int)'T');

            InitializeWebViewEnvironment();

            IdleService = new IdleService(null, webView, logger, networkService, apiService);


            PageManager = new PageManager(
                _networkService,
                _logger,
                _apiService,
                webView,
                _httpClient,
                this
            );


            IdleService.SetPageManager(PageManager);
            PageManager.SetIdleService(IdleService);

            _maintenanceService = new MaintenanceService(
                _apiService,
                _logger,
                _networkService,
                _httpClient,
                this,
                IdleService,
                NavigateWebView
            );
            PageManager.SetMaintenanceService(_maintenanceService);
            FormClosing += OnFormClosing;

            _configMonitorService = new ConfigMonitorService(
                _apiService,
                _logger,
                _networkService,
                this,
                _maintenanceService
            );

            _configMonitorService.StartMonitoring();

        }
        protected override void WndProc(ref Message m)
        {
            try
            {
                const int WM_HOTKEY = 0x0312;
                const int HOTKEY_ID = 1;
                const int TEST_HOTKEY_ID = 2;

                if (m.Msg == WM_HOTKEY)
                {
                    try
                    {
                        if (m.WParam.ToInt32() == HOTKEY_ID)
                        {
                            _logger.Log("🚨 Killswitch activated! Exiting fullscreen or shutting down.");
                            ShowKillswitchDialog();
                            return;
                        }
                        if (m.WParam.ToInt32() == TEST_HOTKEY_ID)
                        {
                            _logger.Log("🧪 Test hotkey activated! Running status update test.");
                            RunStatusUpdateTest();
                            return;
                        }
                    }
                    catch (Exception hotKeyEx)
                    {
                        _logger.LogError("Error processing hotkey", hotKeyEx);
                    }
                }

                base.WndProc(ref m);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error in WndProc", ex);
                base.WndProc(ref m);
            }
        }

        private async void RunStatusUpdateTest()
        {
            try
            {
                bool result = await PageManager.TestStatusUpdate("Online");
                MessageBox.Show(result ? "Status update test successful!" : "Status update test failed!",
                              "Test Result", MessageBoxBtns.OK,
                              result ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error running status update test", ex);
                MessageBox.Show($"Test error: {ex.Message}", "Test Error", MessageBoxBtns.OK, MessageBoxIcon.Error);
            }
        }
        private void ShowKillswitchDialog()
        {
            try
            {
                Form dialogForm = new Form
                {
                    Text = "Emergency Exit",
                    Size = new Size(400, 250),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    Owner = this,
                    TopMost = true,
                    ShowInTaskbar = true
                };

                Label messageLabel = new Label
                {
                    Text = "Options:\n\n" +
                           "Exit Fullscreen: Return to windowed mode\n" +
                           "Close Application: Completely exit the program\n" +
                           "Cancel: Do nothing",
                    Dock = DockStyle.Top,
                    Padding = new Padding(10),
                    AutoSize = true,

                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10)
                };

                FlowLayoutPanel panel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    FlowDirection = FlowDirection.TopDown
                };
                panel.Controls.Add(messageLabel);

                Button exitFullscreenButton = new Button
                {
                    Text = "Exit Fullscreen",
                    DialogResult = DialogResult.Yes,
                    Dock = DockStyle.Bottom,
                    Width = 200,
                    Margin = new Padding(10)
                };

                Button closeApplicationButton = new Button
                {
                    Text = "Close Application",
                    DialogResult = DialogResult.No,
                    Dock = DockStyle.Bottom,
                    Width = 200,
                    Margin = new Padding(10)
                };

                Button cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Dock = DockStyle.Bottom,
                    Width = 200,
                    Margin = new Padding(10)
                };

                dialogForm.Controls.Add(exitFullscreenButton);
                dialogForm.Controls.Add(closeApplicationButton);
                dialogForm.Controls.Add(cancelButton);
                dialogForm.Controls.Add(panel);

                dialogForm.Shown += (s, e) =>
                {
                    dialogForm.Activate();
                    dialogForm.BringToFront();
                };

                DialogResult result = dialogForm.ShowDialog();
                HandleKillswitchDialogResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in killswitch dialog", ex);
                try
                {
                    ShutdownApplication();
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError("Error during fallback shutdown", fallbackEx);
                }
            }
        }

        private async void HandleKillswitchDialogResult(DialogResult result)
        {
            try
            {
                switch (result)
                {
                    case DialogResult.Yes:
                        ExitFullscreen();
                        break;
                    case DialogResult.No:
                        await SendOfflineStatus();
                        ShutdownApplication();
                        break;
                    case DialogResult.Cancel:
                        _logger.Log("Emergency exit cancelled. No action taken.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling killswitch dialog result", ex);
                await SendOfflineStatus();
                ShutdownApplication();
            }
        }

        private void ExitFullscreen()
        {
            _logger.Log("🔲 Exiting fullscreen mode.");
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = false;
        }

        private async void ShutdownApplication()
        {
            await SendOfflineStatus();
            _logger.Log("❌ Emergency shutdown initiated.");

            _maintenanceService.StopMonitoring();

            this.FormClosing -= OnFormClosing;
            this.Close();
            Application.Exit();
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            UnregisterHotKey(this.Handle, TEST_HOTKEY_ID);
            base.OnFormClosed(e);
        }

        private async Task RestartApplicationAsync()
        {
            try
            {
                string apprefPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Diviso\\Remote Screen Setup\\Remote Screen Player.appref-ms"
                );

                if (!File.Exists(apprefPath))
                {
                    _logger.LogError("❌ .appref-ms shortcut not found. Cannot restart the application.");
                    return;
                }

                _logger.Log("🔁 Restarting application using ClickOnce shortcut: " + apprefPath);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = apprefPath,
                    UseShellExecute = true
                };

                Process.Start(psi);
                await Task.Delay(1000);
                Application.Exit();
            }
            catch (Exception ex)
            {
                _logger.LogError("🚨 Failed to restart application", ex);
            }
        }


        private async void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (_isShuttingDown)
                {
                    return;
                }

                _isShuttingDown = true;
                _logger.Log("🔻 Application is closing — initiating graceful shutdown.");

                bool autoRestart = _apiService.GetLastRetrievedConfig()?.AutoRestart ?? false;
                _logger.Log($"AutoRestart is: {autoRestart}");

                if (autoRestart)
                {
                    e.Cancel = true;
                    await RestartApplicationAsync();
                    return;
                }

                StopBackgroundServices();

                if (webView?.CoreWebView2 != null)
                {
                    _logger.Log("🧹 Disposing WebView2 before shutdown.");
                    webView.CoreWebView2.Stop();
                    webView.Dispose();
                }

                await SendOfflineStatus();

                _logger.Log("✅ Shutdown completed. Letting the form close.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during form closing", ex);
                try
                {
                    await SendOfflineStatus();
                }
                catch (Exception innerEx)
                {
                    _logger.LogError("Error sending offline status during error recovery", innerEx);
                }
            }
        }
        private void StopBackgroundServices()
        {
            try
            {
                _maintenanceService.StopMonitoring();
                _configMonitorService.StopMonitoring();

                string lastUrl = PageManager.GetLastSuccessfulUrl();
                if (!string.IsNullOrEmpty(lastUrl))
                {
                    PageManager.SaveUrlToCache(lastUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error stopping background services", ex);
            }
        }

        private async Task SendOnlineStatus()
        {
            try
            {
                string macAddress = _networkService.GetMacAddress();

                bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(macAddress);
                if (!isRegistered)
                {
                    _logger.Log($"📌 Device {macAddress} not found in system. Waiting for registration.");
                    return;
                }

                bool success = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        success = await _apiService.SendStatusUpdateAsync(macAddress, "Online", PageManager.UpdateIdleScreenUrl);
                        if (success) return;
                    }
                    catch (Exception attemptEx)
                    {
                        _logger.LogWarning($"🌐 Online status failed on attempt {attempt + 1}. {attemptEx.Message}");
                    }

                    await Task.Delay(2000);
                }

                _logger.LogError("❌ Online status update failed after multiple attempts.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Comprehensive error sending online status", ex);
            }
        }
        private async Task SendOfflineStatus()
        {
            try
            {
                string macAddress = _networkService.GetMacAddress();
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool success = await _apiService.SendStatusUpdateAsync(macAddress, "Offline", PageManager.UpdateIdleScreenUrl);
                    if (success) return;

                    _logger.LogWarning($"🌐 Offline status failed. Retrying... ({attempt + 1}/3)");
                    await Task.Delay(2000);
                }
                _logger.LogError("❌ Offline status update failed after multiple attempts.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error sending offline status", ex);
            }
        }
        public void EnterOfflineMode()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(EnterOfflineMode));
                return;
            }

            if (_offlinePopupShown)
            {
                _logger.Log("🔁 Offline mode already active. Skipping duplicate.");
                return;
            }

            _offlinePopupShown = true;

            try
            {
                _logger.Log("🛑 Entering offline mode. API is not reachable.");

                this.TopMost = false;
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;

                PageManager.NavigateToOfflineScreen().ConfigureAwait(false);

                Task.Run(async () =>
                {
                    while (_offlinePopupShown)
                    {
                        try
                        {
                            bool isHealthy = await _apiService.CheckApiHealthAsync();
                            bool isRegistered = await _apiService.CheckIfDeviceExistsAsync(_networkService.GetMacAddress());

                            if (isHealthy && isRegistered)
                            {
                                if (IdleService != null && IdleService.IsIdle)
                                {
                                    _logger.Log("✅ API is back online. Attempting to exit idle before loading main page.");
                                    await IdleService.ExitIdleModeAsync();
                                }

                                ResetOfflinePopupFlag();

                                await PageManager.LoadConfiguredUrl();
                                break;
                            }

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Error checking API health during offline mode", ex);
                        }

                        await Task.Delay(60000);
                    }
                });

                _logger.Log("✅ Entered offline mode with visual indicator in WebView");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while entering offline mode", ex);
            }
        }
        public void TriggerOfflineModeIfNotAlready()
        {
            if (_offlinePopupShown)
            {
                _logger.Log("🔁 Offline popup already shown. Skipping duplicate trigger.");
                return;
            }

            EnterOfflineMode();
        }
        public void ResetOfflinePopupFlag()
        {
            _offlinePopupShown = false;
        }


        private async void InitializeWebViewEnvironment()
        {
            try
            {
                if (webView.CoreWebView2 != null)
                {
                    _logger.Log("WebView2 is already initialized. Skipping reinitialization.");
                    return;
                }

                string userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RemoteScreenPlayerWebView2"
                );
                _logger.Log($"Using WebView2 User Data Folder: {userDataFolder}");

                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--allow-file-access-from-files --disable-web-security --allow-file-access --allow-running-insecure-content"
                };

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.WebMessageReceived += (sender, args) =>
                {
                    PageManager?.HandleWebMessage(sender, args);
                };

                CoreWebView2Settings settings = webView.CoreWebView2.Settings;
                settings.IsWebMessageEnabled = true;
                settings.AreHostObjectsAllowed = true;
                settings.IsScriptEnabled = true;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.IsStatusBarEnabled = false;

                webView.CoreWebView2.WebMessageReceived += WebView2MessageHandler;

                await webView.CoreWebView2.ExecuteScriptAsync(@"
            (function() {
                const originalLog = console.log;
                const originalError = console.error;
                const originalWarn = console.warn;
                const originalInfo = console.info;
                
                const postMessageToHost = function(action, message) {
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage({ action: action, message: message });
                    }
                };

                console.log = function() {
                    const message = Array.from(arguments).map(String).join(' ');
                    postMessageToHost('log', message);
                    originalLog.apply(console, arguments);
                };

                console.error = function() {
                    const message = Array.from(arguments).map(String).join(' ');
                    postMessageToHost('error', message);
                    originalError.apply(console, arguments);
                };
                
                console.warn = function() {
                    const message = Array.from(arguments).map(String).join(' ');
                    postMessageToHost('warn', message);
                    originalWarn.apply(console, arguments);
                };
                
                console.info = function() {
                    const message = Array.from(arguments).map(String).join(' ');
                    postMessageToHost('info', message);
                    originalInfo.apply(console, arguments);
                };
            })();
        ");
                await SendOnlineStatus();

                if (IdleService != null && PageManager != null)
                {
                    if (IdleService.GetPageManager() == null)
                        IdleService.SetPageManager(PageManager);

                    if (PageManager.GetIdleService() == null)
                        PageManager.SetIdleService(IdleService);
                }

                if (_maintenanceService != null && PageManager != null)
                {
                    PageManager.SetMaintenanceService(_maintenanceService);
                }

                await PageManager.InitializeNavigation();
                DeviceConfig config = _apiService.GetLastRetrievedConfig();
                if (config != null)
                {
                    bool startupEnabled = config.StartupEnabled ?? false;
                    _config.StartupConfiguration = startupEnabled
                        ? AppConfiguration.StartupConfigState.Enabled
                        : AppConfiguration.StartupConfigState.Disabled;
                    StartupHandler.ApplyStartupSetting(startupEnabled, _logger);

                    _config.SaveSettings();
                }


            }
            catch (Exception ex)
            {
                _logger.LogError("WebView2 Initialization Error", ex);
                MessageBox.Show($"Failed to initialize WebView2.\n\nError: {ex.Message}", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void WebView2MessageHandler(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string message = args.WebMessageAsJson;
                _logger.Log($"🌐 WebView2 received message: {message}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling WebView2 message", ex);
            }
        }

        private void NavigateWebView(string url)
        {
            CurrentUrl = url;
            PageManager.NavigateToUrl(url);
        }
        public void UpdateCurrentUrl(string url)
        {
            CurrentUrl = url;
        }
        public WebView2 GetWebView()
        {
            return webView;
        }
    }
}