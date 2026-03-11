using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RssSite.Components.Authentication;
using RssSite.Components.Models;
using RssSite.Components.Services;
using RssSite.Components.Services.Extensions;

namespace RssSite.Components.Shared
{
    public class NavMenuBase : ComponentBase
    {
        protected bool IsNavigationCollapsed { get; set; } = true;
        protected string? NavigationMenuCssClass => IsNavigationCollapsed ? "collapse" : null;
        protected bool IsUserAdmin { get; set; }
        protected bool IsUserAuthenticated { get; set; }
        protected bool IsUserAuthLoading { get; set; } = true;
        protected string CurrentUsername { get; set; } = "User";

        protected string UserDisplayName { get; set; } = "User";
        protected int? CurrentUserID { get; set; }

        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public AuthService AuthService { get; set; } = default!;
        [Inject] public ApiService ApiService { get; set; } = default!;
        [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public ILogger<NavMenuBase> Logger { get; set; } = default!;

        private AuthenticationState? authStateData;
        private ClaimsPrincipal authenticatedUser = new(new ClaimsIdentity());

        protected override async Task OnInitializedAsync()
        {
            IsUserAuthLoading = true;
            Logger.LogInformation("[NAV MENU] Initializing user authentication state...");

            try
            {
                authStateData = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                authenticatedUser = authStateData.User;

                if (authenticatedUser.Identity is { IsAuthenticated: true })
                {
                    IsUserAuthenticated = true;
                    IsUserAdmin = authenticatedUser.IsInRole("Admin");

                    Claim? userIDClaim = authenticatedUser.FindFirst(ClaimTypes.NameIdentifier);
                    if (userIDClaim != null && int.TryParse(userIDClaim.Value, out int userID))
                    {
                        CurrentUserID = userID;
                        await LoadUserDetailsAsync(userID);
                    }

                    if (userIDClaim == null || !int.TryParse(userIDClaim.Value, out _))
                    {
                        Logger.LogWarning("[NAV MENU] User ID claim not found or invalid.");
                    }

                    CurrentUsername = authenticatedUser.FindFirst(ClaimTypes.Name)?.Value
                                    ?? authenticatedUser.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value
                                    ?? authenticatedUser.FindFirst("sub")?.Value
                                    ?? "User";

                    UserDisplayName = CurrentUsername;
                }

                if (!(authenticatedUser.Identity is { IsAuthenticated: true }))
                {
                    ResetUserState();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[NAV MENU] Error initializing authentication state.");
                ResetUserState();
            }

            IsUserAuthLoading = false;
            StateHasChanged();
        }

        private async Task LoadUserDetailsAsync(int userID)
        {
            try
            {
                Logger.LogInformation("[NAV MENU] Fetching user details for UserID: {UserID}", userID);

                HttpResponseMessage userResponse = await ApiService.GetAsync($"api/users/{userID}");
                string responseContent = await userResponse.Content.ReadAsStringAsync();
                Logger.LogInformation("[NAV MENU] User details response: {Response}", responseContent);

                if (userResponse.IsSuccessStatusCode)
                {
                    JsonSerializerOptions jsonOptions = new()
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    ApiResponse<User>? wrapper = JsonSerializer.Deserialize<ApiResponse<User>>(responseContent, jsonOptions);

                    if (wrapper != null && wrapper.Success && wrapper.Item != null)
                    {
                        User userDetails = wrapper.Item;
                        UserDisplayName = !string.IsNullOrWhiteSpace(userDetails.FirstName)
                            ? userDetails.FirstName
                            : CurrentUsername;

                        Logger.LogInformation("[NAV MENU] User display name set to: {DisplayName}", UserDisplayName);
                    }

                    if (wrapper == null || !wrapper.Success || wrapper.Item == null)
                    {
                        Logger.LogWarning("[NAV MENU] Failed to extract user details from response");
                        UserDisplayName = CurrentUsername;
                    }
                }

                if (!userResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[NAV MENU] Failed to retrieve user details. Status: {StatusCode}", userResponse.StatusCode);
                    UserDisplayName = CurrentUsername;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[NAV MENU] Error fetching user details for UserID: {UserID}", userID);
                UserDisplayName = CurrentUsername;
            }
        }
        private void ResetUserState()
        {
            Logger.LogInformation("[NAV MENU] Resetting user state.");
            IsUserAuthenticated = false;
            IsUserAdmin = false;
            CurrentUsername = "User";
            UserDisplayName = "User";
            CurrentUserID = null;
        }

        protected async Task PerformUserLogout()
        {
            try
            {
                Logger.LogInformation("[NAV MENU] Logging out user.");
                await AuthService.LogoutAsync();
                NavigationManager.NavigateTo("/login", forceLoad: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[NAV MENU] Error during user logout.");
            }
        }

        protected void RedirectToUserProfile()
        {
            if (CurrentUserID.HasValue)
            {
                Logger.LogInformation("[NAV MENU] Redirecting to user profile: {UserID}", CurrentUserID.Value);
                NavigationManager.NavigateTo($"/admin/user/management/configuration/{CurrentUserID.Value}");
            }
        }

        protected void RedirectToScreenList()
        {
            if (!IsUserAuthenticated)
            {
                Logger.LogWarning("[NAV MENU] User is not authenticated. Redirecting to Login.");
                NavigationManager.NavigateTo("/login");
                return;
            }

            Logger.LogInformation("[NAV MENU] Redirecting to Screen List.");
            NavigationManager.NavigateTo("/screens/list");
        }
    }
}
