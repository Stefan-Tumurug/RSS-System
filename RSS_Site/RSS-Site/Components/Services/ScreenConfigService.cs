using RssSite.Components.Models;

namespace RssSite.Components.Services
{
    public class ScreenConfigService(ApiService apiService, ILogger<ScreenConfigService> logger)
    {
        private readonly ApiService _apiService = apiService;
        private readonly ILogger<ScreenConfigService> _logger = logger;

        public async Task<ScreenModel?> GetConfigAsync(string macAddress)
        {
            await _apiService.AddAuthorizationHeader();
            try
            {
                _logger.LogInformation("[SCREEN CONFIG] Fetching config for MAC: {MacAddress}", macAddress);
                ScreenModel? screen = await _apiService.GetScreenConfigAsync(macAddress);

                if (screen == null)
                {
                    _logger.LogWarning("[SCREEN CONFIG] No config found for MAC: {MacAddress}, returning default.", macAddress);
                    return CreateDefaultConfig(macAddress);
                }

                screen.MacAddress = macAddress;
                screen.ScreenResolution ??= "1920x1080";
                screen.RefreshInterval = screen.RefreshInterval > 0 ? screen.RefreshInterval : 60;
                screen.LastUpdated = screen.LastUpdated == default ? DateTime.UtcNow : screen.LastUpdated;

                return screen;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SCREEN CONFIG] Exception fetching config for {MacAddress}", macAddress);
                return null;
            }
        }

        public async Task<bool> SaveConfigAsync(ScreenModel updatedScreen)
        {
            await _apiService.AddAuthorizationHeader();

            try
            {
                _logger.LogInformation("[SCREEN CONFIG] Saving config for MAC: {MacAddress}", updatedScreen.MacAddress);

                return await _apiService.SaveScreenConfigAsync(updatedScreen);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SCREEN CONFIG] Exception saving config for {MacAddress}", updatedScreen.MacAddress);
                return false;
            }
        }

        public ScreenModel CreateDefaultConfig(string macAddress)
        {
            _logger.LogInformation("[SCREEN CONFIG] Creating default configuration for {MacAddress}", macAddress);

            return new ScreenModel
            {
                MacAddress = macAddress,
                Name = "Unknown Screen",
                Status = "Offline",
                Url = string.Empty,
                AutoRestart = false,
                StartupEnabled = null,
                LastUpdated = DateTime.UtcNow,
                LastSeenOnline = null,
                RefreshInterval = 60,
                ScreenResolution = "1920x1080",
                Address = string.Empty,
                OperatingSystem = string.Empty
            };
        }
    }
}
