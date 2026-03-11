using System.Text.Json;
using RssSite.Components.Models;
using RssSite.Components.Services.Extensions;
using static RssSite.Components.Services.ApiService;

namespace RssSite.Components.Services.Extensions
{
    public static class ApiServiceExtensions
    {
        public static async Task<HttpResponseMessage> GetAsync(this ApiService apiService, string url)
        {
            await apiService.AddAuthorizationHeader();
            return await apiService._httpClient.GetAsync(url);
        }

        public static async Task<T?> GetFromJsonAsync<T>(this ApiService apiService, string url, CancellationToken cancellationToken = default)
        {
            await apiService.AddAuthorizationHeader();
            return await apiService._httpClient.GetFromJsonAsync<T>(url, cancellationToken);
        }

        public static async Task<HttpResponseMessage> PostAsJsonAsync<T>(this ApiService apiService, string url, T value, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await apiService.AddAuthorizationHeader();
            return await apiService._httpClient.PostAsJsonAsync(url, value, options, cancellationToken);
        }

        public static async Task<HttpResponseMessage> PutAsJsonAsync<T>(this ApiService apiService, string url, T value, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await apiService.AddAuthorizationHeader();
            return await apiService._httpClient.PutAsJsonAsync(url, value, options, cancellationToken);
        }
        public static async Task<LogResponse> GetScreenLogsAsync(this ApiService apiService,
            string macAddress,
            int timezoneOffset = 0,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int page = 1,
            int pageSize = 50)
        {
            await apiService.AddAuthorizationHeader();

            string requestUrl = $"{apiService._apiBaseUrl}/player/{macAddress}/logs?timezoneOffset={timezoneOffset}&page={page}&pageSize={pageSize}";

            if (startDate.HasValue)
                requestUrl += $"&startDate={startDate.Value:yyyy-MM-dd}";

            if (endDate.HasValue)
                requestUrl += $"&endDate={endDate.Value:yyyy-MM-dd}";

            HttpResponseMessage response = await apiService._httpClient.GetAsync(requestUrl);
            string jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                apiService._logger.LogError("[API ERROR] Failed to fetch logs. Status: {StatusCode} - {Response}",
                    response.StatusCode, jsonResponse);
                return new LogResponse { ErrorMessage = "API returned an error: " + response.StatusCode };
            }

            try
            {
                ApiResponse<LogResponse>? apiResponse = JsonSerializer.Deserialize<ApiResponse<LogResponse>>(jsonResponse, apiService.JsonSerializerSettings);


                if (apiResponse == null || !apiResponse.Success || apiResponse.Item == null)
                {
                    return new LogResponse { ErrorMessage = "Invalid API response structure." };
                }

                return apiResponse.Item;
            }
            catch (JsonException ex)
            {
                apiService._logger.LogError(ex, "[API ERROR] JSON parsing failed for logs response: {Response}", jsonResponse);
                return new LogResponse { ErrorMessage = "JSON Parsing Error: " + ex.Message };
            }
        }
    }
}