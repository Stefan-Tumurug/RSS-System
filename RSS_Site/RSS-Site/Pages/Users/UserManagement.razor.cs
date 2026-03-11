using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RssSite.Components.Authentication;
using RssSite.Components.Models;
using RssSite.Components.Services;
using RssSite.Components.Services.Extensions;
using RssSite.Components.Storage;

namespace RssSite.Pages.Users
{
    public partial class UserManagement : ComponentBase
    {
        protected List<User>? UserList { get; set; }
        protected List<User> DisplayedUsers { get; set; } = [];
        protected string? ErrorMessage { get; set; }
        protected bool ShowDeleteConfirmation { get; set; }

        protected int CurrentPage { get; set; } = 1;
        protected int PageSize { get; set; } = 10;
        protected int TotalUsers { get; set; }
        protected int TotalPages => (int)System.Math.Ceiling((double)TotalUsers / PageSize);
        protected string SearchTerm { get; set; } = string.Empty;
        protected int UserIDToDelete { get; set; }
        protected string? UsernameToDelete { get; set; }

        [Inject] protected IJSRuntime JavaScriptRuntime { get; set; } = default!;
        [Inject] protected ApiService ApiService { get; set; } = default!;
        [Inject] protected AuthService AuthService { get; set; } = default!;
        [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
        [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

        protected bool IsLoadingAuthState { get; set; } = true;
        protected bool IsAdmin { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            AuthenticationState authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal currentUser = authState.User;

            if (currentUser.Identity is { IsAuthenticated: true })
            {
                IsAdmin = currentUser.IsInRole("Admin");

                if (!IsAdmin)
                {
                    NavigationManager.NavigateTo("/", forceLoad: true);
                }
            }

            if (!(currentUser.Identity is { IsAuthenticated: true }))
            {
                NavigationManager.NavigateTo("/login", forceLoad: true);
            }

            IsLoadingAuthState = false;
            StateHasChanged();
            await LoadUsersAsync();
        }

        protected async Task LoadUsersAsync()
        {
            string? token = await SecureStorage.GetAuthToken(JavaScriptRuntime);
            if (string.IsNullOrEmpty(token) || !await AuthService.IsTokenValid(token))
            {
                ErrorMessage = "Session expired. Please log in again.";
                await AuthService.LogoutAsync();
                return;
            }

            await ApiService.AddAuthorizationHeader();
            HttpResponseMessage response = await ApiService.GetAsync("api/users/all");

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                ApiResponseWrapper<List<User>>? responseWrapper = JsonSerializer.Deserialize<ApiResponseWrapper<List<User>>>(
                    jsonResponse,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                UserList = responseWrapper?.Items ?? [];
                UpdateDisplayedUsers();
            }

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to load users. Status: {response.StatusCode}";
                UserList = [];
            }
        }

        protected void UpdateDisplayedUsers()
        {
            if (UserList == null) return;

            List<User> filteredUsers = string.IsNullOrWhiteSpace(SearchTerm)
                ? UserList
                : UserList.FindAll(u =>
                    u.Username.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(u.Email) && u.Email.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                    u.Role.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                );

            TotalUsers = filteredUsers.Count;

            DisplayedUsers = [.. filteredUsers
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)];
        }

        protected void Search()
        {
            CurrentPage = 1;
            UpdateDisplayedUsers();
        }

        protected void GoToPage(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > TotalPages) return;

            CurrentPage = pageNumber;
            UpdateDisplayedUsers();
        }

        protected void NextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                UpdateDisplayedUsers();
            }
        }

        protected void PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                UpdateDisplayedUsers();
            }
        }

        protected void ConfirmDelete(int userID, string username)
        {
            UserIDToDelete = userID;
            UsernameToDelete = username;
            ShowDeleteConfirmation = true;
        }

        protected void CancelDelete()
        {
            ShowDeleteConfirmation = false;
            UserIDToDelete = 0;
            UsernameToDelete = null;
        }

        protected async Task OnDeleteConfirmedAsync()
        {
            HttpResponseMessage deleteResponse = await ApiService.DeleteAsync($"api/users/{UserIDToDelete}");
            if (deleteResponse.IsSuccessStatusCode)
            {
                UserList?.RemoveAll(u => u.UserID == UserIDToDelete);
                UpdateDisplayedUsers();
                ErrorMessage = null;
            }

            if (!deleteResponse.IsSuccessStatusCode)
            {
                ErrorMessage = "Failed to delete user.";
            }

            ShowDeleteConfirmation = false;
            UserIDToDelete = 0;
            UsernameToDelete = null;
        }

        protected void NavigateToUserConfiguration(int userID)
        {
            NavigationManager.NavigateTo($"/admin/user/management/configuration/{userID}");
        }

        protected void NavigateToUserCreation()
        {
            NavigationManager.NavigateTo("/admin/user/creation");
        }

        public class ApiResponseWrapper<T>
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public T? Items { get; set; }
        }
    }
}