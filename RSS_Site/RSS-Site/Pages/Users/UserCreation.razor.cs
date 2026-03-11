using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RssSite.Components.Models;
using RssSite.Components.Services;

namespace RssSite.Pages.Users
{
    public class UserCreationBase : ComponentBase
    {
        [Inject] protected ApiService ApiService { get; set; } = default!;
        [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
        [Inject] protected IJSRuntime JavaScriptRuntime { get; set; } = default!;
        [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
        [Inject] protected ILogger<UserCreationBase> Logger { get; set; } = default!;

        protected CreateUserRequest NewUserRequest { get; set; } = new();
        protected string ErrorMessage { get; set; } = string.Empty;

        protected async Task CreateUserAsync()
        {
            if (string.IsNullOrWhiteSpace(NewUserRequest.Username) || string.IsNullOrWhiteSpace(NewUserRequest.Password))
            {
                ErrorMessage = "Username and password are required.";
                return;
            }

            try
            {
                Logger.LogInformation("[UserCreationBase] Sending user creation request for: {Username}", NewUserRequest.Username);

                HttpResponseMessage response = await ApiService.PostAsync("api/users", NewUserRequest);

                if (!response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    Logger.LogError("[UserCreationBase] User creation failed. Status: {Status}, Response: {Content}",
                                    response.StatusCode, responseContent);
                    ErrorMessage = $"Failed to create user: {response.StatusCode}";
                    return;
                }

                CreateUserResponse? createUserResponse = await response.Content.ReadFromJsonAsync<CreateUserResponse>();

                if (createUserResponse == null || !createUserResponse.Success)
                {
                    ErrorMessage = $"User creation failed: {createUserResponse?.Message ?? "Unknown error"}";
                    return;
                }

                Logger.LogInformation("[UserCreationBase] User created successfully: {Username}", NewUserRequest.Username);
                NavigationManager.NavigateTo("/admin/user/management", forceLoad: true);
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "[UserCreationBase] Error creating user.");
                ErrorMessage = $"An error occurred while creating user: {exception.Message}";
            }
        }

        protected void Cancel()
        {
            NavigationManager.NavigateTo("/admin/user/management");
        }

        public class CreateUserResponse
        {
            public string Message { get; set; } = string.Empty;
            public bool Success { get; set; }
        }
    }
}
