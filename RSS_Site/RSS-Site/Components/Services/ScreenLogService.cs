using System.Text.Json;
using RssSite.Components.Models;
using RssSite.Components.Services.Extensions;
using static RssSite.Components.Services.ApiService;

namespace RssSite.Components.Services
{
    public class ScreenLogService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl;
        private readonly ILogger<ScreenLogService> _logger;
        private readonly JsonSerializerOptions JsonSerializerSettings;
        private readonly ApiService _apiService;
        public ScreenLogService(HttpClient httpClient, IConfiguration configuration, ApiService apiService, ILogger<ScreenLogService> logger)
        {
            _apiService = apiService;
            _httpClient = httpClient;
            _logger = logger;
            _apiBaseUrl = $"{configuration["ApiSettings:BaseUrl"]}/api/screens";
            JsonSerializerSettings = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<IEnumerable<LogModel>> GetScreenLogsByDateAsync(
            string macAddress, int year, int month, int day, string logMessage)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                _logger.LogError("[LOG SERVICE] MacAddress cannot be empty!");
                return [];
            }

            try
            {
                double timeZoneOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes;
                string requestUrl = $"{_apiBaseUrl}/player/{macAddress}/logs/{year}/{month:D2}/{day:D2}?timezoneOffset={timeZoneOffset}";

                _logger.LogInformation("[LOG SERVICE] {Message}", logMessage);
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[LOG SERVICE] No logs found for {MacAddress} on {Year}-{Month:D2}-{Day:D2}. Status: {StatusCode}, Response: {ResponseContent}",
                        macAddress, year, month, day, response.StatusCode, responseContent);
                    return [];
                }

                List<LogModel>? logs = JsonSerializer.Deserialize<List<LogModel>>(responseContent, JsonSerializerSettings);

                return logs ?? Enumerable.Empty<LogModel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LOG SERVICE] Exception fetching logs for {MacAddress} on {Year}-{Month}-{Day}", macAddress, year, month, day);
                return [];
            }
        }
        public async Task<LogResponse> GetPaginatedScreenLogsAsync(
            string macAddress,
            int timezoneOffset = 0,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int page = 1,
            int pageSize = 50)
        {
            try
            {
                LogResponse response = await _apiService.GetScreenLogsAsync(
                    macAddress,
                    timezoneOffset,
                    startDate,
                    endDate,
                    page,
                    pageSize
                );

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LOG SERVICE] Error fetching paginated logs for {MacAddress}", macAddress);
                return new LogResponse { ErrorMessage = "Failed to fetch logs: " + ex.Message };
            }
        }
    }
}
