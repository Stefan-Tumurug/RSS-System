using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RssSite.Components.Models;
using RssSite.Components.Services;
using static RssSite.Components.Services.ApiService;

namespace RssSite.Pages.Screens
{
    public class ScreenListBase : ComponentBase
    {
        [Inject]
        protected ScreenListService ScreenListService { get; set; } = default!;

        [Inject]
        protected ILogger<ScreenListBase> Logger { get; set; } = default!;

        [Inject]
        protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

        [Inject]
        public IJSRuntime JSRuntime { get; set; } = default!;

        protected ScreenListService.ScreenListResult ScreenListResult { get; set; } = new();
        protected List<ScreenModel> AllScreens { get; set; } = [];
        protected List<ScreenModel> DisplayedScreens { get; set; } = [];

        protected int CurrentPage = 1;
        protected int PageSize = 10;
        protected int TotalScreens = 0;
        protected int TotalPages => (int)Math.Ceiling((double)TotalScreens / PageSize);

        protected string SearchTerm = string.Empty;
        protected string SearchField = "name";

        protected bool ShowDeleteConfirmation { get; set; }
        protected string? ScreenToDelete { get; set; }
        protected List<LogEntry> ScreenLogs { get; set; } = [];
        protected bool ShowLogsModal { get; set; }
        protected int TotalLogs = 0;
        protected DateTime? StartDate { get; set; } = null;
        protected DateTime? EndDate { get; set; } = null;

        protected bool IsAdmin { get; set; } = false;
        protected bool IsAdminView { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            AuthenticationState authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            IsAdmin = authenticationState.User.IsInRole("Admin");

            await RestoreAdminViewPreference();

            Logger.LogInformation("[ScreenListBase] Initializing screen list...");
            await LoadScreens();
        }

        protected async Task LoadScreens()
        {
            try
            {
                ScreenListResult = await ScreenListService.GetScreensAsync(IsAdminView);

                if (ScreenListResult.Screens != null)
                {
                    List<ScreenModel> screenList = [.. ScreenListResult.Screens];
                    AllScreens = screenList;
                    UpdateDisplayedScreens();
                    Logger.LogInformation("[ScreenListBase] API returned: {Count} screens", screenList.Count);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "[ScreenListBase] Unexpected error while loading screens.");
                ScreenListResult.ErrorMessage = "An error occurred while loading screens.";
            }
        }

        private async Task RestoreAdminViewPreference()
        {
            try
            {
                if (IsAdmin)
                {
                    string? storedAdminView = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "isAdminView");
                    if (bool.TryParse(storedAdminView, out bool savedAdminView))
                    {
                        IsAdminView = savedAdminView;
                    }
                    if (!bool.TryParse(storedAdminView, out _))
                    {
                        IsAdminView = false;
                    }
                }
            }
            catch
            {
                IsAdminView = false;
            }
        }

        protected async void ToggleAdminView()
        {
            if (IsAdmin)
            {
                IsAdminView = !IsAdminView;
                SaveAdminViewPreference();
                await LoadScreens();
                StateHasChanged();
            }
        }

        private void SaveAdminViewPreference()
        {
            if (IsAdmin)
            {
                try
                {
                    JSRuntime.InvokeVoidAsync("localStorage.setItem", "isAdminView", IsAdminView.ToString());
                }
                catch
                {
                }
            }
        }

        protected void UpdateDisplayedScreens(bool? adminView = null)
        {
            bool useAdminView = adminView ?? IsAdminView;

            DisplayedScreens = ScreenListService.GetPaginatedScreens(
                AllScreens,
                SearchTerm,
                SearchField,
                CurrentPage,
                PageSize,
                out TotalScreens,
                useAdminView
            );
        }

        protected void Search()
        {
            CurrentPage = 1;
            UpdateDisplayedScreens();
            StateHasChanged();
        }

        protected void GoToPage(int page)
        {
            if (page < 1 || page > TotalPages)
                return;

            CurrentPage = page;
            UpdateDisplayedScreens();
            StateHasChanged();
        }

        protected void NextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                UpdateDisplayedScreens();
                StateHasChanged();
            }
        }

        protected void PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                UpdateDisplayedScreens();
                StateHasChanged();
            }
        }

        protected async Task FetchScreenLogs(string macAddress)
        {
            LogResponse logResponse = await ScreenListService.FetchScreenLogsAsync(macAddress, StartDate, EndDate, CurrentPage, PageSize);

            if (logResponse.Logs is { Count: > 0 })
            {
                ScreenLogs = logResponse.Logs;
                TotalLogs = logResponse.TotalLogs;
                ShowLogsModal = true;
            }
            if (!(logResponse.Logs is { Count: > 0 }))
            {
                Logger.LogWarning("[ScreenListBase] No logs found for MAC: {MacAddress}", macAddress);
            }
        }


        protected void ConfirmDelete(string macAddress)
        {
            if (IsAdminView)
            {
                ScreenToDelete = macAddress;
                ShowDeleteConfirmation = true;
                Logger.LogInformation("[ScreenListBase] Delete confirmation shown for {MacAddress}", macAddress);
                StateHasChanged();
            }
        }
        protected void CancelDelete()
        {
            ShowDeleteConfirmation = false;
            ScreenToDelete = null;
        }

        protected async Task OnDeleteConfirmed()
        {
            if (string.IsNullOrEmpty(ScreenToDelete)) return;

            Logger.LogInformation("[ScreenListBase] Deleting screen {MacAddress}", ScreenToDelete);
            bool isDeleted = await ScreenListService.DeleteScreenAsync(ScreenToDelete);

            if (isDeleted)
            {
                Logger.LogInformation("[ScreenListBase] Screen {MacAddress} deleted successfully", ScreenToDelete);
                await LoadScreens();
            }
            if (!isDeleted)
            {
                Logger.LogError("[ScreenListBase] Failed to delete screen {MacAddress}", ScreenToDelete);
            }

            ShowDeleteConfirmation = false;
            ScreenToDelete = null;
        }

        protected string GetStatusClass(string status)
        {
            return ScreenListService.GetStatusClass(status);
        }

        protected void NavigateToLogs(string macAddress)
        {
            ScreenListService.NavigateToScreenLogs(macAddress);
        }

        protected void NavigateToConfig(string macAddress)
        {
            if (IsAdminView)
            {
                ScreenListService.NavigateToScreenConfig(macAddress);
            }
        }
    }
}