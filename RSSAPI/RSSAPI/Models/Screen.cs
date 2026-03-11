using System;
using System.ComponentModel.DataAnnotations;

namespace RSSAPI.Models
{
    public class Screen
    {
        [Key]
        public string MacAddress { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = "Default Name";

        public string Url { get; set; } = "No URL configured";

        public string Status { get; set; } = "Offline";

        public bool AutoRestart { get; set; } = false;

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public DateTime? LastSeenOnline { get; set; }

        public string? IdleScreenUrl { get; set; }

        public string ScreenResolution { get; set; } = "1920x1080";

        public int RefreshInterval { get; set; } = 60;

        public string? Address { get; set; }

        public string? OperatingSystem { get; set; }
        public bool? StartupEnabled { get; set; }

    }
}