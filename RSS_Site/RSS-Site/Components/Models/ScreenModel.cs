using System.Text.Json.Serialization;

namespace RssSite.Components.Models
{
    public class ScreenModel
    {
        [JsonPropertyName("macAddress")]
        public string? MacAddress { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("autoRestart")]
        public bool AutoRestart { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("lastSeenOnline")]
        public DateTime? LastSeenOnline { get; set; }

        [JsonPropertyName("idleScreenUrl")]
        public string? IdleScreenUrl { get; set; }

        [JsonPropertyName("screenResolution")]
        public string ScreenResolution { get; set; }

        [JsonPropertyName("refreshInterval")]
        public int RefreshInterval { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("operatingSystem")]
        public string? OperatingSystem { get; set; }
        [JsonPropertyName("startupEnabled")]
        public bool? StartupEnabled { get; set; }


        public ScreenModel()
        {
            LastUpdated = DateTime.UtcNow;
            ScreenResolution = "1920x1080";
            RefreshInterval = 60;
        }
    }
}
