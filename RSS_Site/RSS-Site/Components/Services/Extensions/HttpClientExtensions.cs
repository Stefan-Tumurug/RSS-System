using System.Text.Json;
using RssSite.Components.Models;

namespace RssSite.Components.Services.Extensions
{
    public static class HttpClientExtensions
    {
        public static async Task<T?> GetFromJsonWrapperAsync<T>(this HttpClient client, string url, JsonSerializerOptions? options = null)
        {
            HttpResponseMessage response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync();
                JsonSerializerOptions deserializeOptions = options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                ApiResponse<T>? responseWrapper = JsonSerializer.Deserialize<ApiResponse<T>>(content, deserializeOptions);

                if (responseWrapper != null && responseWrapper.Success)
                {
                    return responseWrapper.Item;
                }
            }
            return default;
        }
    }
}