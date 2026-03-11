using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using Serilog.Core;

namespace RssPlayer.Components.Utilities
{
    public class PageGenerator
    {
        private readonly StyleProvider _styleProvider = new StyleProvider();
        private readonly AppConfiguration _config = AppConfiguration.Instance;
        private readonly string _apiBaseUrl;
        private readonly string _outputDirectory;
        private readonly LoggingService _logger;

		public PageGenerator(string apiBaseUrl = null, string outputDirectory = null)
		{
			try
			{
				this._apiBaseUrl = apiBaseUrl ?? _config.ApiBaseUrl;
				this._outputDirectory = outputDirectory ?? _config.ResourcesFolder;
			}
			catch (Exception ex)
			{
				_logger?.LogError($"Error initializing PageGenerator: {ex.Message}", ex);
				this._apiBaseUrl = _config.ApiBaseUrl;
				this._outputDirectory = _config.ResourcesFolder;
			}
		}
		public PageGenerator(LoggingService logger)
		{
			try
			{
				this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
				this._apiBaseUrl = _config.ApiBaseUrl;
				this._outputDirectory = _config.ResourcesFolder;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error initializing PageGenerator: {ex.Message}");
				this._apiBaseUrl = _config.ApiBaseUrl;
				this._outputDirectory = _config.ResourcesFolder;
				this._logger = new LoggingService();
			}
		}
		public string GetOutputDirectory()
        {
            return _outputDirectory;
        }

        private static readonly object _pageGenerationLock = new object();

        public string GenerateRegistrationPage(string macAddress, string outputDirectory)
        {
            lock (_pageGenerationLock)
            {
                try
                {
                    _logger?.Log($"🏁 GenerateRegistrationPage STARTED");
                    _logger?.Log($"📍 MAC Address: {macAddress}");
                    _logger?.Log($"📂 Output Directory: {outputDirectory}");

                    Directory.CreateDirectory(outputDirectory);

                    string htmlContent = BuildRegistrationPage(macAddress);

                    string filePath = Path.Combine(outputDirectory, $"DeviceRegistration_{macAddress.Replace(":", "-")}.html");

                    _logger?.Log($"📄 File Path: {filePath}");

                    File.WriteAllText(filePath, htmlContent, Encoding.UTF8);

                    _logger?.Log($"✅ GenerateRegistrationPage COMPLETED");

                    return filePath;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"❌ Error in GenerateRegistrationPage: {ex.Message}", ex);
                    Console.Error.WriteLine($"Error generating registration page: {ex.Message}");
                    return null;
                }
            }
        }
        private string BuildRegistrationPage(string macAddress)
        {
            try
            {
                string apiBaseUrl = AppConfiguration.Instance.ApiBaseUrl;
                string registerEndpoint = AppConfiguration.Instance.ScreenRegistrationEndpoint;
                string configEndpoint = $"{apiBaseUrl}/api/screens/player/";

                _logger?.Log($"📋 BuildRegistrationPage STARTED");
                _logger?.Log($"📍 MAC Address: {macAddress}");
                _logger?.Log($"🌐 API Base URL: {apiBaseUrl}");

                string htmlContent = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>RSS Player - Device Registration</title>
    " + _styleProvider.GetCommonStyles() + @"
    " + _styleProvider.GetRegistrationStyles() + @"
</head>
<body>
    <div class=""container"">
        <h1>RSS Player Registration</h1>
        <div class=""mac-address"">
            <strong>Device MAC Address:</strong>
            <span id=""macAddress"">" + macAddress + @"</span>
        </div>
        <form id=""registrationForm"">
            <div class=""form-group"">
                <label for=""name"">Screen Name *</label>
                <input type=""text"" id=""name"" name=""name"" required placeholder=""Enter a name for this screen"">
            </div>
            <div class=""form-group"">
                <label for=""address"">Address</label>
                <input type=""text"" id=""address"" name=""address"" placeholder=""Physical location of this screen"" 
                    oninvalid=""this.setCustomValidity('Please provide a descriptive location or room name')"" 
                    oninput=""this.setCustomValidity('')"">
            </div>
            <div class=""form-group"">
                <label for=""url"">Display URL *</label>
                <input type=""url"" id=""url"" name=""url"" required placeholder=""https://example.com"">
            </div>
            <div class=""form-group"">
                <label for=""idleScreenUrl"">Idle Screen URL</label>
                <input type=""url"" id=""idleScreenUrl"" name=""idleScreenUrl"" placeholder=""https://example.com/idle"">
            </div>
            <div class=""form-group"">
                <label for=""refreshInterval"">Refresh Interval (minutes)</label>
                <input type=""number"" id=""refreshInterval"" name=""refreshInterval"" value=""60"" min=""1"" max=""1440"">
            </div>
            <div class=""form-group"">
                <label for=""screenResolution"">Screen Resolution</label>
                <select id=""screenResolution"" name=""screenResolution"">
                    <option value=""1920x1080"" selected>1920x1080</option>
                    <option value=""1280x720"">1280x720</option>
                    <option value=""2560x1440"">2560x1440</option>
                </select>
            </div>
            <div class=""form-group"">
                <label for=""autoRestart"">Auto Restart - Ctrl + Shift + X to exit Application</label>
                <select id=""autoRestart"" name=""autoRestart"">
                    <option value=""false"" selected>No</option>
                    <option value=""true"">Yes</option>
                </select>
            </div>
            <div class=""form-group"">
                <label for=""startupEnabled"">Startup on Boot</label>
                <select id=""startupEnabled"" name=""startupEnabled"">
                    <option value=""true"" selected>Yes</option>
                    <option value=""false"">No</option>
                </select>
            </div>
            <btn type=""submit"" class=""btn"">Register Device</btn>
        </form>
        <div id=""status-message""></div>
        <div id=""loading""><p>Fetching configuration...</p></div>
    </div>
    <script>
        window.registerEndpoint = '" + registerEndpoint + @"';
        window.configEndpoint = '" + configEndpoint + @"';
    </script>
    <script>
        " + GetRegistrationScript(apiBaseUrl) + @"
    </script>
    <script>
        // DIAGNOSTIC LOGGING
        console.log('🔍 Registration Page Generated');
        console.log('🖥️ MAC Address: " + macAddress + @"');
        console.log('🌐 API Base URL: " + apiBaseUrl + @"');
    </script>
</body>
</html>";

                _logger?.Log($"📋 BuildRegistrationPage COMPLETED");
                return htmlContent;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"❌ Error in BuildRegistrationPage: {ex.Message}", ex);
                return $"Error generating registration page: {ex.Message}";
            }
        }
        private string GetRegistrationScript(string apiBaseUrl)
        {
            _ = $"{apiBaseUrl}/api/screens/player/register";
            _ = $"{apiBaseUrl}/api/screens/player/";

            return @"
// Advanced Logging Utility
const Logger = {
    // Logging levels
    LEVELS: {
        ERROR: 'error',
        WARN: 'warn',
        INFO: 'info',
        DEBUG: 'debug'
    },

    // Log message formatter
    formatMessage: function(message, level = this.LEVELS.INFO) {
        const timestamp = new Date().toISOString();
        return `[${level.toUpperCase()}] ${timestamp} - ${message}`;
    },

    // Core logging method
    log: function(message, level = this.LEVELS.INFO) {
        const logMessage = this.formatMessage(message, level);
        
        // Console logging based on level
        switch(level) {
            case this.LEVELS.ERROR:
                console.error(logMessage);
                break;
            case this.LEVELS.WARN:
                console.warn(logMessage);
                break;
            case this.LEVELS.DEBUG:
                console.debug(logMessage);
                break;
            default:
                console.log(logMessage);
        }

        // Optional WebView logging
        this.sendToHost(message, level);
    },

    // Send log to host application
    sendToHost: function(message, level) {
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ 
                    action: 'log', 
                    level: level, 
                    message: message 
                });
            }
        } catch (e) {
            console.error('Failed to send log to host:', e);
        }
    },

    // Shorthand logging methods
    error: function(message) { this.log(message, this.LEVELS.ERROR); },
    warn: function(message) { this.log(message, this.LEVELS.WARN); },
    debug: function(message) { this.log(message, this.LEVELS.DEBUG); }
};

// Device Registration Module with Configuration Fetching
const RegistrationModule = {
    elements: {
        form: null,
        statusMessage: null,
        macAddressElement: null,
        submitButton: null
    },

    init: function() {
        this.cacheElements();
        this.preventDefaultSubmission();
        this.addEventListeners();
        this.setupValidation();
    },

    cacheElements: function() {
        this.elements.form = document.getElementById('registrationForm');
        this.elements.statusMessage = document.getElementById('status-message');
        this.elements.macAddressElement = document.getElementById('macAddress');
        this.elements.submitButton = this.elements.form.querySelector('btn[type=""submit""]');
    },

    preventDefaultSubmission: function() {
        if (this.elements.form) {
            this.elements.form.addEventListener('submit', (event) => {
                event.preventDefault();
            });
        }
    },

    setupValidation: function() {
        const requiredFields = ['name', 'url'];
        requiredFields.forEach(fieldId => {
            const field = document.getElementById(fieldId);
            if (field) {
                field.addEventListener('invalid', (event) => {
                    event.preventDefault();
                    this.showError(`Please fill out the ${field.labels[0].textContent}`);
                });
            }
        });
    },

    addEventListeners: function() {
        if (this.elements.form) {
            this.elements.submitButton.addEventListener('click', (event) => {
                event.preventDefault();
                this.handleSubmit();
            });
        }
    },

    showError: function(message) {
        if (this.elements.statusMessage) {
            this.elements.statusMessage.textContent = message;
            this.elements.statusMessage.className = 'error';
        }
        Logger.error(message);
    },

    clearStatus: function() {
        if (this.elements.statusMessage) {
            this.elements.statusMessage.textContent = '';
            this.elements.statusMessage.className = '';
        }
    },

    validateForm: function() {
        const nameField = document.getElementById('name');
        const urlField = document.getElementById('url');

        if (!nameField.value.trim()) {
            this.showError('Screen Name is required');
            nameField.focus();
            return false;
        }

        if (!urlField.value.trim()) {
            this.showError('Display URL is required');
            urlField.focus();
            return false;
        }

        try {
            new URL(urlField.value);
        } catch {
            this.showError('Invalid Display URL format');
            urlField.focus();
            return false;
        }

        return true;
    },

    disableForm: function() {
        if (this.elements.form) {
            this.elements.form.querySelectorAll('input, select, btn').forEach(el => {
                el.disabled = true;
            });
            this.elements.form.style.opacity = '0.5';
        }
    },

    enableForm: function() {
        if (this.elements.form) {
            this.elements.form.querySelectorAll('input, select, btn').forEach(el => {
                el.disabled = false;
            });
            this.elements.form.style.opacity = '1';
        }
    },

    collectRegistrationData: function() {
        const macAddress = this.elements.macAddressElement.textContent.trim();
        const name = document.getElementById('name').value.trim();
        const url = document.getElementById('url').value.trim();
        const idleScreenUrl = document.getElementById('idleScreenUrl').value.trim() || null;
        const address = document.getElementById('address').value.trim() || 'Not Specified';

        return {
            macAddress,
            name,
            url,
            idleScreenUrl,
            address,
            autoRestart: document.getElementById('autoRestart').value === 'true',
        startupEnabled: document.getElementById('startupEnabled').value === 'true',
            refreshInterval: parseInt(document.getElementById('refreshInterval').value),
            screenResolution: document.getElementById('screenResolution').value,
            operatingSystem: navigator.userAgent
        };
    },

    handleSubmit: async function() {
        this.clearStatus();

        if (!this.validateForm()) {
            return;
        }

        this.disableForm();

        try {
            const registrationData = this.collectRegistrationData();
            Logger.log('Registration Data: ' + JSON.stringify(registrationData));

            // Send registration request
            const registrationResponse = await this.registerDevice();
            _pendingStartupRegistration = true;

            if (registrationResponse) {
                // Fetch configuration after successful registration
                const config = await this.fetchConfiguration();
                _pendingStartupRegistration = true;

                // Navigate to URL
                if (config && config.url) {
                    this.navigateToUrl(config.url);
                } else if (config && config.idleScreenUrl) {
                    this.navigateToUrl(config.idleScreenUrl);
                } else {
                    throw new Error('No valid URL found in configuration');
                }
            }
        } catch (error) {
            Logger.error('Registration Error: ' + error.message);
            this.showError(error.message);
            this.enableForm();
        }
    },

    registerDevice: async function() {
        try {
            const response = await fetch(window.registerEndpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(this.collectRegistrationData())
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`Registration failed: ${response.status} - ${errorText}`);
            }

            Logger.log('Device registered successfully');
            return true;
        } catch (error) {
            Logger.error('Registration request failed: ' + error.message);
            throw error;
        }
    },

    fetchConfiguration: async function() {
        try {
            const macAddress = this.elements.macAddressElement.textContent.trim();
            const response = await fetch(`${window.configEndpoint}${macAddress}/config`);
            
            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`Configuration fetch failed: ${response.status} - ${errorText}`);
            }

            const configData = await response.json();
            
            Logger.log('Configuration fetched: ' + JSON.stringify(configData));
            
            // Extract the configuration from the API response structure
            const config = configData.item || configData;

            if (config && typeof config.startupEnabled !== ""undefined"") {
                Logger.log(""StartupEnabled from config: "" + config.startupEnabled);

                // Send message to host app (WinForms) to enable/disable startup
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage({
                        action: ""setStartupConfig"",
                        enabled: !!config.startupEnabled
                    });
                }
            }

            return config;
        } catch (error) {
            Logger.error('Configuration fetch failed: ' + error.message);
            throw error;
        }
    },

    navigateToUrl: function(url) {
        Logger.log('Attempting to navigate to: ' + url);
        
        // Validate URL
        try {
            new URL(url);
        } catch (error) {
            Logger.error('Invalid URL: ' + url);
            return;
        }

        // Send explicit WebView messages, multiple ways
        console.log('WebView Navigation Attempt:', url);
        
        // Wrapper function to ensure multiple attempts at navigation
        const attemptNavigation = () => {
            if (window.chrome && window.chrome.webview) {
                // Method 1: Direct message
                window.chrome.webview.postMessage({
                    action: 'navigateToUrl', 
                    url: url
                });

                // Method 2: Invoke host objects if available
                try {
                    if (window.chrome.webview.hostObjects && 
                        window.chrome.webview.hostObjects.sync && 
                        window.chrome.webview.hostObjects.sync.bridge) {
                        window.chrome.webview.hostObjects.sync.bridge.NavigateToUrl(url);
                    }
                } catch (hostObjError) {
                    console.error('Host object navigation failed:', hostObjError);
                }

                // Fallback method
                window.location.href = url;
            } else {
                // Standard browser navigation
                window.location.href = url;
            }
        };

        // Multiple attempts with a slight delay
        setTimeout(attemptNavigation, 0);
        setTimeout(attemptNavigation, 100);
        setTimeout(attemptNavigation, 300);
    }
};

// Initialize registration module when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    RegistrationModule.init();
});";
        }
		public string GenerateRegistrationPage(string macAddress)
        {
            return GenerateRegistrationPage(macAddress, _outputDirectory);
        }
        public string GenerateOfflineScreen(string macAddress)
        {
            lock (_pageGenerationLock)
            {
                try
                {
                    _logger?.Log($"🏁 GenerateOfflineScreen STARTED");
                    _logger?.Log($"📍 MAC Address: {macAddress}");

                    Directory.CreateDirectory(_outputDirectory);
                    string htmlContent = BuildOfflineScreen(macAddress);

                    string filePath = Path.Combine(_outputDirectory, $"OfflineScreen_{macAddress.Replace(":", "-")}.html");
                    _logger?.Log($"📄 File Path: {filePath}");

                    File.WriteAllText(filePath, htmlContent, Encoding.UTF8);
                    _logger?.Log($"✅ GenerateOfflineScreen COMPLETED");

                    return filePath;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"❌ Error in GenerateOfflineScreen: {ex.Message}", ex);
                    return null;
                }
            }
        }

        private string BuildOfflineScreen(string macAddress)
        {
            try
            {
                _logger?.Log($"📋 BuildOfflineScreen STARTED");
                _logger?.Log($"📍 MAC Address: {macAddress}");

                string htmlContent = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>RSS Player - Offline Mode</title>
    " + _styleProvider.GetCommonStyles() + @"
    " + _styleProvider.GetOfflineStyles() + @"
</head>
<body>
    <div class=""container"">
        <div class=""icon"">⚠️</div>
        <h1>Connection Lost</h1>
        <p>We're having trouble connecting to the server. The application is now in offline mode.</p>
        <div class=""spinner""></div>
        <p class=""status"">Attempting to reconnect automatically...</p>
        <div class=""attempt-count"">Reconnection attempt: <span id=""attempts"">1</span></div>
        <button class=""retry-button"" onclick=""manualRetry()"">Retry Connection Now</button>
    </div>

    <script>
        " + GetOfflineScreenScript() + @"
    </script>
    <script>
        // DIAGNOSTIC LOGGING
        console.log('🔍 Offline Screen Generated');
        console.log('🖥️ MAC Address: " + macAddress + @"');
    </script>
</body>
</html>";

                _logger?.Log($"📋 BuildOfflineScreen COMPLETED");
                return htmlContent;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"❌ Error in BuildOfflineScreen: {ex.Message}", ex);
                return GenerateSimpleOfflineScreen();
            }
        }

        private string GenerateSimpleOfflineScreen()
        {
            return @"<!DOCTYPE html>
<html>
<head><title>Offline Mode</title></head>
<body style='font-family:sans-serif;text-align:center;padding-top:100px;'>
<h1>Offline Mode</h1>
<p>Connection lost. Attempting to reconnect...</p>
</body>
</html>";
        }

        private string GetOfflineScreenScript()
        {
            return @"
        let attemptCount = 1;
        const attemptElement = document.getElementById('attempts');
        const statusElement = document.querySelector('.status');
        const MAX_ATTEMPTS = 100; // Effectively unlimited
        const RETRY_INTERVAL = 60000; // 60 seconds
        
        // Update attempt counter
        function updateAttempt() {
            attemptCount++;
            attemptElement.textContent = attemptCount;
        }
        
        // Check connection status
        async function checkConnection() {
            try {
                statusElement.textContent = 'Testing connection...';
                
                // Post a message to the C# application to check API health
                window.chrome.webview.postMessage({ 
                    action: 'retryConnection'
                });
                
                // The actual connection check happens in C#
                // We'll just update the UI and schedule the next attempt
                setTimeout(() => {
                    statusElement.textContent = 'Attempting to reconnect automatically...';
                    updateAttempt();
                    
                    if (attemptCount <= MAX_ATTEMPTS) {
                        setTimeout(checkConnection, RETRY_INTERVAL);
                    } else {
                        statusElement.textContent = 'Maximum reconnection attempts reached.';
                    }
                }, 3000);
                
            } catch (error) {
                statusElement.textContent = `Error checking connection: ${error.message}`;
                setTimeout(checkConnection, RETRY_INTERVAL);
            }
        }
        
        // Manual retry button handler
        function manualRetry() {
            const button = document.querySelector('.retry-button');
            button.disabled = true;
            button.textContent = 'Checking Connection...';
            
            window.chrome.webview.postMessage({ 
                action: 'retryConnection'
            });
            
            setTimeout(() => {
                button.disabled = false;
                button.textContent = 'Retry Connection Now';
            }, 3000);
        }
        
        // Start checking for connection
        setTimeout(checkConnection, RETRY_INTERVAL);
        
        // Log important events
        window.chrome.webview.postMessage({ 
            action: 'log',
            level: 'info',
            message: 'Offline loading screen initialized'
        });
    ";
        }
    }
}