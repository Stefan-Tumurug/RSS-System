using System.ComponentModel.DataAnnotations;

namespace RSSAPI.Models
{
    public class PlayerRegistration
    {
        [Required]
        public string MacAddress { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Url { get; set; } = string.Empty;

        public bool AutoRestart { get; set; } = false;

        public string? ScreenResolution { get; set; }

        public int RefreshInterval { get; set; } = 60;

        public string? IdleScreenUrl { get; set; }

        public string? Address { get; set; }

        public string? OperatingSystem { get; set; }
        public bool? StartupEnabled { get; set; }
    }
}