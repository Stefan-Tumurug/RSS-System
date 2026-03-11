using Microsoft.JSInterop;

namespace RssSite.Components.Storage
{
    public class SecureStorage(IJSRuntime jsRuntime, ILogger<SecureStorage> logger)
    {
        private readonly IJSRuntime _jsRuntime = jsRuntime;
        private readonly ILogger<SecureStorage> _logger = logger;

        public async Task<string> GetAuthTokenAsync()
        {
            try
            {
                _logger.LogInformation("[SECURE STORAGE] Retrieving auth token...");
                string? token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
                if (string.IsNullOrEmpty(token))
                {
                    token = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "authToken");
                }
                if (!string.IsNullOrEmpty(token))
                {
                    _logger.LogInformation("[SECURE STORAGE] Auth token retrieved successfully.");
                }
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("[SECURE STORAGE] No auth token found.");
                }
                return token ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SECURE STORAGE] Error retrieving auth token.");
                return string.Empty;
            }
        }

        public async Task<bool> StoreAuthTokenAsync(string token, bool persistent)
        {
            try
            {
                string storageMethod = persistent ? "localStorage" : "sessionStorage";
                _logger.LogInformation("[SECURE STORAGE] Storing auth token in {StorageMethod}.", storageMethod);
                await _jsRuntime.InvokeVoidAsync($"{storageMethod}.setItem", "authToken", token);
                string? storedToken = await _jsRuntime.InvokeAsync<string>($"{storageMethod}.getItem", "authToken");
                bool success = !string.IsNullOrEmpty(storedToken);
                if (success)
                {
                    _logger.LogInformation("[SECURE STORAGE] Auth token stored successfully.");
                }
                if (!success)
                {
                    _logger.LogWarning("[SECURE STORAGE] Failed to store auth token.");
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SECURE STORAGE] Error storing auth token.");
                return false;
            }
        }

        public async Task ClearAuthTokenAsync()
        {
            try
            {
                _logger.LogInformation("[SECURE STORAGE] Clearing auth tokens...");
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
                await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "authToken");
                _logger.LogInformation("[SECURE STORAGE] Auth tokens cleared successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SECURE STORAGE] Error clearing auth tokens.");
            }
        }

        public static async Task<string?> GetAuthToken(IJSRuntime jsRuntime)
        {
            try
            {
                string? token = await jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
                if (string.IsNullOrEmpty(token))
                {
                    token = await jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "authToken");
                }
                return token;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<bool> StoreAuthToken(IJSRuntime jsRuntime, string token, bool persistent)
        {
            try
            {
                string storageMethod = persistent ? "localStorage" : "sessionStorage";
                await jsRuntime.InvokeVoidAsync($"{storageMethod}.setItem", "authToken", token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task ClearAuthToken(IJSRuntime jsRuntime)
        {
            try
            {
                await jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
                await jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "authToken");
            }
            catch (Exception)
            {
            }
        }
    }
}