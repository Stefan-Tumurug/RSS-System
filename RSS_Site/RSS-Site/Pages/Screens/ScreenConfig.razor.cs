using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using RssSite.Components.Models;
using RssSite.Components.Services;

namespace RssSite.Pages.Screens
{
    [Authorize(Roles = "Admin")]
    public partial class ScreenConfig : ComponentBase
    {
        [Inject] private ScreenConfigService _screenConfigService { get; set; } = default!;
        [Inject] private ILogger<ScreenConfig> _logger { get; set; } = default!;

        [Parameter] public string MacAddress { get; set; } = string.Empty;

        private ScreenModel _screenConfigData { get; set; } = new();
        private string? errorMessage { get; set; }
        private string? successMessage { get; set; }
        private bool isLoading { get; set; } = true;
        private bool showSuccessPopup { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            _logger.LogInformation("[SCREEN CONFIG] Initializing with MAC: {MacAddress}", MacAddress);
            if (!string.IsNullOrEmpty(MacAddress))
            {
                await LoadScreenConfig();
            }
        }

        private async Task LoadScreenConfig()
        {
            try
            {
                isLoading = true;
                _logger.LogInformation("[SCREEN CONFIG] Fetching configuration for MAC: {MacAddress}", MacAddress);

                ScreenModel? fetchedConfig = await _screenConfigService.GetConfigAsync(MacAddress);

                if (fetchedConfig == null)
                {
                    _logger.LogWarning("[SCREEN CONFIG] No configuration found for {MacAddress}. Using defaults.", MacAddress);
                    _screenConfigData = _screenConfigService.CreateDefaultConfig(MacAddress);
                    return;
                }

                _logger.LogInformation("[SCREEN CONFIG] Configuration loaded successfully for {MacAddress}", MacAddress);
                _screenConfigData = fetchedConfig;
            }
            catch (InvalidOperationException opEx)
            {
                errorMessage = $"Operation error: {opEx.Message}";
                _logger.LogError(opEx, "[SCREEN CONFIG] Invalid operation during configuration loading.");
            }
            catch (ArgumentException argEx)
            {
                errorMessage = $"Invalid argument: {argEx.Message}";
                _logger.LogError(argEx, "[SCREEN CONFIG] Invalid argument during configuration loading.");
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error loading configuration: {ex.Message}";
                _logger.LogError(ex, "[SCREEN CONFIG] Unexpected error loading configuration.");
            }
            finally
            {
                isLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
        private async Task SaveConfiguration()
        {
            try
            {
                isLoading = true;
                _logger.LogInformation("[SCREEN CONFIG] Saving configuration for {MacAddress}", MacAddress);
                bool success = await _screenConfigService.SaveConfigAsync(_screenConfigData);

                if (!success)
                {
                    _logger.LogWarning("[SCREEN CONFIG] Failed to save configuration for {MacAddress}", MacAddress);
                    errorMessage = "Failed to save configuration.";
                    return;
                }

                _logger.LogInformation("[SCREEN CONFIG] Configuration saved successfully for {MacAddress}", MacAddress);
                successMessage = "Screen configuration updated successfully.";
                showSuccessPopup = true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error saving configuration: {ex.Message}";
                _logger.LogError(ex, "[SCREEN CONFIG] Error saving configuration.");
            }
            finally
            {
                isLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
        private void UpdateAutoRestart(string? value)
        {
            if (bool.TryParse(value, out bool result))
            {
                _screenConfigData.AutoRestart = result;
                StateHasChanged();
            }
        }

        private void UpdateStartupEnabled(string? value)
        {
            if (bool.TryParse(value, out bool result))
            {
                _screenConfigData.StartupEnabled = result;
                StateHasChanged();
            }
        }
        private async Task CloseSuccessPopup()
        {
            showSuccessPopup = false;
            successMessage = null;
            await InvokeAsync(StateHasChanged);
        }
    }
}