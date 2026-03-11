using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using RssSite.Components.Authentication;
using RssSite.Components.Services;

namespace RssSite.Components.Pages
{
    public partial class Index
    {
        [Inject]
        private NavigationManager navigationManager { get; set; } = default!;

        [Inject]
        private AuthService authService { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider authenticationStateProvider { get; set; } = default!;

        [Inject]
        private ScreenListService screenListService { get; set; } = default!;

        public int OnlineScreenCount { get; private set; }
        public int OfflineScreenCount { get; private set; }
        public int IdleScreenCount { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            AuthenticationState authState = await authenticationStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity is not { IsAuthenticated: true })
            {
                navigationManager.NavigateTo("/login", forceLoad: true);
                return;
            }
            await LoadScreenStatusCounts();
        }

        private async Task LoadScreenStatusCounts()
        {
            try
            {
                ScreenListService.ScreenListResult screenListResult = await screenListService.GetScreensAsync(isAdminView: true);
                if (screenListResult.Screens != null)
                {
                    OnlineScreenCount = screenListResult.Screens.Count(s =>
                        s.Status?.ToLower() == "online");
                    OfflineScreenCount = screenListResult.Screens.Count(s =>
                        s.Status?.ToLower() == "offline");
                    IdleScreenCount = screenListResult.Screens.Count(s =>
                        s.Status?.ToLower() == "idle");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading screen status: {ex.Message}");
                OnlineScreenCount = 0;
                OfflineScreenCount = 0;
                IdleScreenCount = 0;
            }
        }
    }
}