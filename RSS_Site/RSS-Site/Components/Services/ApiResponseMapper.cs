using System.Text.Json;

namespace RssSite.Components.Services
{
    public class ApiResponseMapper
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public ApiResponseMapper()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public T? MapFromApiResponse<T>(string jsonResponse) where T : class
        {
            try
            {
                ApiResponse? genericResponse = JsonSerializer.Deserialize<ApiResponse>(jsonResponse, _jsonOptions);

                if (genericResponse == null)
                {
                    return null;
                }

                if (!genericResponse.Success)
                {
                    return null;
                }

                if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(List<>))
                {
                    if (genericResponse.Items != null)
                    {
                        return JsonSerializer.Deserialize<T>(
                            JsonSerializer.Serialize(genericResponse.Items, _jsonOptions),
                            _jsonOptions);
                    }

                    return null;
                }

                if (genericResponse.Item != null)
                {
                    return JsonSerializer.Deserialize<T>(
                        JsonSerializer.Serialize(genericResponse.Item, _jsonOptions),
                        _jsonOptions);
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error mapping API response: {ex.Message}");
                return null;
            }
        }
    }

    public class ApiResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public object? Item { get; set; }
        public object? Items { get; set; }
    }
}