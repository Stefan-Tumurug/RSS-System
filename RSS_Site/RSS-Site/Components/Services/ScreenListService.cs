using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using RssSite.Components.Models;
using RssSite.Components.Services.Extensions;
using static RssSite.Components.Services.ApiService;

namespace RssSite.Components.Services
{
    public class ScreenListService(
        HttpClient httpClient,
        ApiService apiService,
        NavigationManager navigationManager,
        ILogger<ScreenListService> logger,
        AuthenticationStateProvider authenticationStateProvider,
        TimeService timeService)
    {
        private readonly ApiService _apiService = apiService;
        private readonly HttpClient _httpClient = httpClient;
        private readonly NavigationManager _navigationManager = navigationManager;
        private readonly ILogger<ScreenListService> _logger = logger;
        private readonly AuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider;
        private readonly TimeService _timeService = timeService;

        public async Task<bool> IsUserAdminAsync()
        {
            AuthenticationState authenticationState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal user = authenticationState.User;
            return user.IsInRole("Admin");
        }

        public async Task<ScreenListResult> GetScreensAsync(bool isAdminView = false)
        {
            ScreenListResult screenListResult = new();
            try
            {
                _logger.LogInformation("[SCREEN LIST] Fetching screens...");
                screenListResult.IsLoading = true;

                List<ScreenModel> screenList = await _apiService.GetAllScreensAsync() ?? [];

                foreach (ScreenModel screen in screenList)
                {
                    if (screen.LastSeenOnline.HasValue)
                    {
                        screen.LastSeenOnline = _timeService.AdjustToLocalTime(screen.LastSeenOnline.Value);
                    }

                    if (screen.LastUpdated != default)
                    {
                        screen.LastUpdated = _timeService.AdjustToLocalTime(screen.LastUpdated);
                    }
                }

                if (!isAdminView)
                {
                    screenList = [.. screenList.Where(screen => screen.Status != "Private" && screen.Status != "Restricted")];
                }

                screenListResult.Screens = screenList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SCREEN LIST] Error loading screens.");
                screenListResult.ErrorMessage = "An error occurred while loading screens. Please try again.";
            }
            finally
            {
                screenListResult.IsLoading = false;
            }
            return screenListResult;
        }

        public List<ScreenModel> GetPaginatedScreens(
            List<ScreenModel> allScreens,
            string searchTerm,
            string searchField,
            int currentPage,
            int pageSize,
            out int totalFilteredScreens,
            bool isAdminView = false)
        {
            List<ScreenModel> filteredScreenList = isAdminView
                ? allScreens
                : [.. allScreens.Where(screen => screen.Status != "Private" && screen.Status != "Restricted")];

            filteredScreenList = ApplySearchFilter(filteredScreenList, searchTerm, searchField);
            totalFilteredScreens = filteredScreenList.Count;

            return [.. filteredScreenList
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)];
        }

        public async Task<bool> DeleteScreenAsync(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogWarning("[SCREEN LIST] Invalid MAC address provided for deletion.");
                return false;
            }

            try
            {
                _logger.LogInformation("[SCREEN LIST] Deleting screen: {MacAddress}", macAddress);
                return await _apiService.DeleteScreenAsync(macAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SCREEN LIST] Error deleting screen: {MacAddress}", macAddress);
                return false;
            }
        }

        public async Task<LogResponse> FetchScreenLogsAsync(
            string macAddress,
            DateTime? startDate,
            DateTime? endDate,
            int currentPage,
            int pageSize)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogWarning("[SCREEN LIST] Invalid MAC address provided for logs.");
                return new LogResponse();
            }

            try
            {
                int timezoneOffset = TimeZoneInfo.Local.BaseUtcOffset.Minutes * -1;
                return await _apiService.GetScreenLogsAsync(macAddress, timezoneOffset, startDate, endDate, currentPage, pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SCREEN LIST] Error loading logs for MAC: {MacAddress}", macAddress);
                return new LogResponse();
            }
        }

        public List<ScreenModel> ApplySearchFilter(List<ScreenModel> screens, string searchTerm, string searchField)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return screens;
            }

            string normalizedSearchTerm = searchTerm.Trim().ToLowerInvariant();
            string normalizedSearchField = searchField.ToLowerInvariant();

            return [.. screens.Where(screen =>
                normalizedSearchField switch
                {
                    "macaddress" => (screen.MacAddress ?? string.Empty).Contains(normalizedSearchTerm, StringComparison.InvariantCultureIgnoreCase),
                    "status" => (screen.Status ?? string.Empty).Contains(normalizedSearchTerm, StringComparison.InvariantCultureIgnoreCase),
                    "address" => (screen.Address ?? string.Empty).Contains(normalizedSearchTerm, StringComparison.InvariantCultureIgnoreCase),
                    _ => (screen.Name ?? string.Empty).Contains(normalizedSearchTerm, StringComparison.InvariantCultureIgnoreCase)
                })];
        }

        public void NavigateToScreenConfig(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogWarning("[SCREEN LIST] Invalid MacAddress provided for navigation.");
                return;
            }
            _logger.LogInformation("[SCREEN LIST] Navigating to screen config for MAC: {MacAddress}", macAddress);
            _navigationManager.NavigateTo($"/screens/config/{macAddress}", forceLoad: true);
        }

        public void NavigateToScreenLogs(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogWarning("[SCREEN LIST] Invalid MAC address provided for logs.");
                return;
            }

            _logger.LogInformation("[SCREEN LIST] Navigating to logs for MAC: {MacAddress}", macAddress);
            _navigationManager.NavigateTo($"/screens/logs/{macAddress}");
        }

        public string GetStatusClass(string status)
        {
            return status?.ToLower() switch
            {
                "online" => "statusOnline",
                "offline" => "statusOffline",
                "idle" => "statusIdle",
                _ => "statusUnknown"
            };
        }

        public class ScreenListResult
        {
            public List<ScreenModel> Screens { get; set; } = [];
            public bool IsLoading { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}
