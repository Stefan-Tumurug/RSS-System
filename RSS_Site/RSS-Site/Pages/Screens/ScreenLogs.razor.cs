using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using RssSite.Components.Services;
using static RssSite.Components.Services.ApiService;

namespace RssSite.Pages.Screens
{
    [Authorize]
    public partial class ScreenLogs
    {
        [Inject] private ApiService apiService { get; set; } = default!;
        [Inject] private ScreenLogService screenLogService { get; set; } = default!;
        [Inject] private TimeService timeService { get; set; } = default!;
        [Inject] private ILogger<ScreenLogs> logger { get; set; } = default!;
        [Inject] private AuthenticationStateProvider authenticationStateProvider { get; set; } = default!;

        [Parameter] public string MacAddress { get; set; } = string.Empty;

        private List<ApiService.LogEntry> logs = [];
        private List<ApiService.LogEntry> filteredLogs = [];
        private ApiService.LogEntry? selectedLog;
        private string? errorMessage;
        private bool isLoading = true;

        private int currentPage = 1;
        private readonly int pageSize = 25;
        private int totalPages;
        private int totalLogs;
        private int currentPageLogCount;

        private string sortColumn = "Timestamp";
        private bool isSortAscending = false;
        private DateTime? startDate;
        private DateTime? endDate;
        private DateTime? specificDate;

        protected override async Task OnInitializedAsync()
        {
            if (!string.IsNullOrEmpty(MacAddress))
            {
                await LoadScreenLogs();
            }
        }
        private async Task LoadScreenLogs()
        {
            try
            {
                isLoading = true;
                int timezoneOffset = timeService.GetTimezoneOffsetInMinutes();

                DateTime? effectiveStart = null;
                DateTime? effectiveEnd = null;

                bool hasSpecific = specificDate.HasValue;
                bool hasRange = startDate.HasValue || endDate.HasValue;

                if (!hasSpecific && !hasRange)
                {
                    effectiveStart = DateTime.Today.AddDays(-1);
                    effectiveEnd = DateTime.Today.AddDays(1);
                }

                if (specificDate.HasValue)
                {
                    DateTime date = specificDate.Value.Date;
                    effectiveStart = date;
                    effectiveEnd = date.AddDays(1);
                }

                if (!specificDate.HasValue && hasRange)
                {
                    if (startDate.HasValue)
                        effectiveStart = startDate.Value.Date;

                    if (endDate.HasValue)
                        effectiveEnd = endDate.Value.Date.AddDays(1);
                }

                LogResponse response = await screenLogService.GetPaginatedScreenLogsAsync(
                    MacAddress,
                    timezoneOffset,
                    effectiveStart,
                    effectiveEnd,
                    currentPage,
                    pageSize
                );

                logs = response.Logs;
                currentPageLogCount = logs.Count;
                totalLogs = response.TotalLogs;
                totalPages = response.TotalPages;
                filteredLogs = logs;

                errorMessage = string.Empty;
            }
            catch (Exception exception)
            {
                errorMessage = $"Error loading logs: {exception.Message}";
                logger.LogError(exception, "[ScreenLogs] Error loading logs.");
            }
            finally
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        private async Task ApplyFilters()
        {
            currentPage = 1;
            if (specificDate.HasValue)
            {
                startDate = null;
                endDate = null;
            }

            await LoadScreenLogs();
        }


        private async Task GoToPage(int page)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (page > totalPages)
            {
                page = totalPages;
            }

            if (page == currentPage)
            {
                return;
            }

            currentPage = page;
            await LoadScreenLogs();
            StateHasChanged();
        }


        private async Task PrevPage()
        {
            logger.LogInformation($"PrevPage called. Current Page: {currentPage}, Total Pages: {totalPages}");

            if (currentPage > 1)
            {
                currentPage--;

                await LoadScreenLogs();

                StateHasChanged();
            }
        }

        private async Task NextPage()
        {
            logger.LogInformation($"NextPage called. Current Page: {currentPage}, Total Pages: {totalPages}");

            if (currentPage < totalPages)
            {
                currentPage++;

                await LoadScreenLogs();

                StateHasChanged();
            }
        }


        private void ShowLogDetails(ApiService.LogEntry log)
        {
            selectedLog = log;
        }

        private void CloseLogDetails()
        {
            selectedLog = null;
        }

        private void SortByTimestamp()
        {
            if (sortColumn == "Timestamp")
            {
                isSortAscending = !isSortAscending;
            }

            if (sortColumn != "Timestamp")
            {
                sortColumn = "Timestamp";
                isSortAscending = true;
            }

            filteredLogs = isSortAscending
                ? [.. logs.OrderBy(l => l.Timestamp)]
                : [.. logs.OrderByDescending(l => l.Timestamp)];
        }
    }
}
