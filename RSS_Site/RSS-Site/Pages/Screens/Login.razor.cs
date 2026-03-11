using Microsoft.AspNetCore.Components;
using RssSite.Components.Authentication;
using RssSite.Components.Models;

namespace RssSite.Pages.Screens
{
    public class LoginBase : ComponentBase
    {
        protected LoginModel UserLoginModel { get; set; } = new();
        protected bool IsLoading { get; set; }
        protected string? LoginErrorMessage { get; set; }
        private bool IsLoginInProgress { get; set; } = false;

        [Parameter]
        [SupplyParameterFromQuery(Name = "returnUrl")]
        public string? ReturnUrl { get; set; }

        [Inject] protected AuthService AuthenticationService { get; set; } = default!;
        [Inject] protected NavigationManager NavigationService { get; set; } = default!;
        [Inject] protected ILogger<LoginBase> Logger { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            Logger.LogInformation("[LOGIN] Initializing login page...");

            try
            {
                bool isUserAuthenticated = await AuthenticationService.IsAuthenticatedAsync();
                if (isUserAuthenticated)
                {
                    Logger.LogInformation("[LOGIN] User already authenticated. Redirecting...");
                    AuthenticationService.NavigateToHomepage();
                }
                if (!isUserAuthenticated)
                {
                    Logger.LogInformation("[LOGIN] User not authenticated. Staying on login page.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[LOGIN] Error checking authentication state.");
            }
        }

        protected async Task HandleValidSubmit()
        {
            if (IsLoginInProgress)
            {
                Logger.LogWarning("[LOGIN] Login attempt ignored. Already in progress.");
                return;
            }

            IsLoginInProgress = true;
            LoginErrorMessage = null;
            IsLoading = true;

            try
            {
                Logger.LogInformation("[LOGIN] Attempting login for user: {Username}", UserLoginModel.Username);

                (bool isSuccessful, string? loginErrorMessage) = await AuthenticationService.ProcessLoginAsync(
                    UserLoginModel.Username, UserLoginModel.Password, UserLoginModel.RememberMe);

                if (isSuccessful)
                {
                    Logger.LogInformation("[LOGIN] Login successful. Redirecting to {ReturnUrl}", ReturnUrl ?? "home page");

                    await Task.Delay(200);
                    AuthenticationService.NavigateAfterLogin(ReturnUrl);
                }
                if (!isSuccessful)
                {
                    LoginErrorMessage = loginErrorMessage ?? "Invalid username or password.";
                    Logger.LogWarning("[LOGIN] Login failed. Error: {ErrorMessage}", LoginErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[LOGIN] Exception occurred during login.");
                LoginErrorMessage = $"An unexpected error occurred: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsLoginInProgress = false;
                StateHasChanged();
            }
        }

        protected void ToggleRememberMe()
        {
            UserLoginModel.RememberMe = !UserLoginModel.RememberMe;
            Logger.LogInformation("[LOGIN] RememberMe toggled. Current state: {RememberMe}", UserLoginModel.RememberMe);
        }

        protected void OnRememberMeChanged(ChangeEventArgs e)
        {
            if (e.Value is bool rememberMeValue)
            {
                UserLoginModel.RememberMe = rememberMeValue;
                Logger.LogInformation("[LOGIN] RememberMe state changed to: {RememberMe}", UserLoginModel.RememberMe);
            }
        }
    }
}