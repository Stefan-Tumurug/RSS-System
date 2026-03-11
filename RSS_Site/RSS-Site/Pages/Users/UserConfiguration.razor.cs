using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using RssSite.Components.Authentication;
using RssSite.Components.Models;
using RssSite.Components.Services;
using RssSite.Components.Services.Extensions;

namespace RssSite.Pages.Users
{
    public class UserConfigurationBase : ComponentBase, IDisposable
    {
        [Inject] protected AuthService AuthService { get; set; } = default!;
        [Inject] protected ApiService ApiService { get; set; } = default!;
        [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
        [Inject] protected ILogger<UserConfigurationBase> Logger { get; set; } = default!;

        [Parameter] public int? UserID { get; set; }

        [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }
        private System.Threading.Timer? successPopupTimer;
        private string? _cachedUserRole;
        protected UserConfigurationModel UserConfig { get; set; } = new();
        protected string? ErrorMessage { get; set; }
        protected string? SuccessMessage { get; set; }
        protected bool IsLoading = false;
        protected bool IsAdminConfiguring => UserID.HasValue;

        protected bool ShowSuccessPopup = false;


        protected bool CanSkipCurrentPasswordValidation()
        {
            if (IsAdminConfiguring)
            {
                return _cachedUserRole == "Admin";
            }
            return false;
        }
        private async Task InitializeUserRoleAsync()
        {
            try
            {
                _cachedUserRole = await AuthService.GetUserRoleAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error retrieving user role during initialization");
                _cachedUserRole = null;
            }
        }
        protected override async Task OnInitializedAsync()
        {
            try
            {
                await InitializeUserRoleAsync();

                if (IsAdminConfiguring)
                {
                    await FetchUserDetailsForAdmin();
                }

                if (!IsAdminConfiguring)
                {
                    await FetchCurrentUserDetails();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during component initialization");
                ErrorMessage = "An error occurred while loading the page. Please try again.";
            }
        }
        protected void StartSuccessPopupTimer()
        {
            successPopupTimer?.Dispose();
            successPopupTimer = new System.Threading.Timer(_ =>
            {
                InvokeAsync(() =>
                {
                    ShowSuccessPopup = false;
                    StateHasChanged();
                });
            }, null, 5000, System.Threading.Timeout.Infinite);
        }

        protected void CloseSuccessPopup()
        {
            ShowSuccessPopup = false;
            StateHasChanged();
        }

        public void Dispose()
        {
            successPopupTimer?.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task FetchCurrentUserDetails()
        {
            if (AuthState != null)
            {
                AuthenticationState authState = await AuthState;
                ClaimsPrincipal user = authState.User;
                if (user.Identity?.IsAuthenticated == true)
                {
                    await FetchUserDetails(user);
                }
                if (user.Identity?.IsAuthenticated != true)
                {
                    NavigationManager.NavigateTo("/login");
                }
            }
        }

        private async Task FetchUserDetailsForAdmin()
        {
            if (UserID == null)
            {
                ErrorMessage = "Invalid user ID";
                return;
            }
            try
            {
                HttpResponseMessage response = await ApiService.GetAsync($"api/users/{UserID}");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Logger.LogInformation("User details response: {Content}", content);
                    JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };
                    ApiResponse<UserConfigurationModel>? responseWrapper = JsonSerializer.Deserialize<ApiResponse<UserConfigurationModel>>(
                        content,
                        jsonOptions
                    );
                    if (responseWrapper != null && responseWrapper.Success)
                    {
                        UserConfig = responseWrapper.Item ?? new UserConfigurationModel();
                        Logger.LogInformation("Fetched user configuration: {UserConfig}",
                            System.Text.Json.JsonSerializer.Serialize(UserConfig));
                    }
                    if (responseWrapper == null || !responseWrapper.Success)
                    {
                        ErrorMessage = responseWrapper?.ErrorMessage ?? "Failed to parse user details";
                        Logger.LogWarning("Failed to fetch user details. Error: {Error}", ErrorMessage);
                    }
                }
                if (!response.IsSuccessStatusCode)
                {
                    ErrorMessage = $"Failed to fetch user. Status: {response.StatusCode}";
                    Logger.LogError("Failed to fetch user details. Status: {Status}", response.StatusCode);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Error fetching user details");
                ErrorMessage = "An error occurred while fetching user details.";
            }
        }

        private async Task FetchUserDetails(ClaimsPrincipal user)
        {
            try
            {
                string? userID = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userID))
                {
                    ErrorMessage = "Unable to retrieve user information.";
                    return;
                }
                HttpResponseMessage response = await ApiService.GetAsync($"api/users/{userID}");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Logger.LogInformation("User details response: {Content}", content);
                    JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };
                    ApiResponse<UserConfigurationModel>? responseWrapper = JsonSerializer.Deserialize<ApiResponse<UserConfigurationModel>>(
                        content,
                        jsonOptions
                    );
                    if (responseWrapper != null && responseWrapper.Success)
                    {
                        UserConfig = responseWrapper.Item ?? new UserConfigurationModel();
                    }
                    if (responseWrapper == null || !responseWrapper.Success)
                    {
                        ErrorMessage = responseWrapper?.ErrorMessage ?? "Failed to parse user details";
                    }
                }
                if (!response.IsSuccessStatusCode)
                {
                    ErrorMessage = $"Failed to fetch user. Status: {response.StatusCode}";
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Error fetching user details");
                ErrorMessage = "An error occurred while fetching user details.";
            }
        }

        protected async Task UpdateProfile()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = null;
                SuccessMessage = null;
                ShowSuccessPopup = false;
                if (IsAdminConfiguring)
                {
                    await ApiService.PutAsJsonAsync($"api/users/{UserConfig.UserID}", UserConfig);
                }
                if (!IsAdminConfiguring)
                {
                    UserConfigurationModel profileData = new()
                    {
                        Email = UserConfig.Email,
                        FirstName = UserConfig.FirstName,
                        LastName = UserConfig.LastName
                    };
                    await ApiService.PutAsJsonAsync($"api/users/{UserConfig.UserID}/profile", profileData);
                }
                SuccessMessage = "Profile updated successfully.";
                ShowSuccessPopup = true;
                StartSuccessPopupTimer();
                if (IsAdminConfiguring)
                {
                    await Task.Delay(1000);
                    NavigationManager.NavigateTo("/admin/user/management");
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Error updating profile");
                ErrorMessage = "An error occurred while updating profile.";
            }
            finally
            {
                IsLoading = false;
            }
        }
        protected async Task ChangePassword()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = null;
                SuccessMessage = null;
                ShowSuccessPopup = false;

                if (UserConfig.NewPassword != UserConfig.ConfirmNewPassword)
                {
                    ErrorMessage = "New passwords do not match.";
                    return;
                }

                bool skipCurrentPasswordValidation = CanSkipCurrentPasswordValidation();

                if (!skipCurrentPasswordValidation)
                {
                    if (string.IsNullOrEmpty(UserConfig.CurrentPassword))
                    {
                        ErrorMessage = "Current password is required.";
                        return;
                    }

                    (bool Success, string? ErrorMessage) loginResult = await AuthService.ProcessLoginAsync(
                        UserConfig.Username,
                        UserConfig.CurrentPassword,
                        false
                    );

                    if (!loginResult.Success)
                    {
                        ErrorMessage = "Current password is incorrect.";
                        return;
                    }
                }

                object passwordChangeRequest = new
                {
                    UserConfig.CurrentPassword,
                    UserConfig.NewPassword
                };

                HttpResponseMessage response = await ApiService.PostAsJsonAsync($"api/users/{UserConfig.UserID}/password", passwordChangeRequest);

                if (!response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Logger.LogWarning("Password change failed. Status: {Status}, Response: {Content}",
                        response.StatusCode, content);
                    ErrorMessage = $"Failed to change password. Server responded with {response.StatusCode}.";
                    return;
                }

                SuccessMessage = "Password changed successfully.";
                ShowSuccessPopup = true;
                UserConfig.CurrentPassword = null;
                UserConfig.NewPassword = null;
                UserConfig.ConfirmNewPassword = null;
                StartSuccessPopupTimer();

                try
                {
                    string? currentUserRole = await AuthService.GetUserRoleAsync();
                    bool isCurrentUserAdmin = currentUserRole == "Admin";

                    if (IsAdminConfiguring && isCurrentUserAdmin)
                    {
                        Logger.LogInformation("Admin changing another user's password. Navigating to admin area after delay.");
                        await Task.Delay(2000);
                        NavigationManager.NavigateTo("/admin/user/management", forceLoad: true);
                        return;
                    }

                    if (!IsAdminConfiguring || (IsAdminConfiguring && !isCurrentUserAdmin))
                    {
                        Logger.LogInformation("User changing own password. Navigating to homepage after delay.");
                        await Task.Delay(2000);
                        NavigationManager.NavigateTo("/", forceLoad: true);
                        return;
                    }
                }
                catch (Exception navEx)
                {
                    Logger.LogError(navEx, "Error during navigation after password change");
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Error changing password");
                ErrorMessage = "An error occurred while changing password.";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}