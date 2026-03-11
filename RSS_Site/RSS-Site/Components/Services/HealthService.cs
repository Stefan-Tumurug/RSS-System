namespace RssSite.Components.Services
{
    public class HealthService(HttpClient httpClient, IConfiguration configuration, ILogger<HealthService> logger)
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly string _apiBaseUrl = $"{configuration["ApiSettings:BaseUrl"]}/api/screens";
        private readonly ILogger<HealthService> _logger = logger;

        public async Task<bool> IsApiHealthyAsync()
        {
            try
            {
                string requestUrl = $"{_apiBaseUrl}/health";
                _logger.LogInformation("[HEALTH CHECK] Checking API health at {Url}", requestUrl);

                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[HEALTH CHECK] API health check failed. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, responseContent);
                    return false;
                }

                _logger.LogInformation("[HEALTH CHECK] API is healthy.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HEALTH CHECK] API Health check failed.");
                return false;
            }
        }
    }
}
