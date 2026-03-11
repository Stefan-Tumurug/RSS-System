using System;
using System.ComponentModel.DataAnnotations;

namespace RSSAPI.Models
{
    public class Config
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Url { get; set; }

        public bool AutoRestart { get; set; } = false;

        public string? IdleScreenUrl { get; set; }

        public string? ScreenResolution { get; set; }

        public int RefreshInterval { get; set; } = 60;

        public string? Address { get; set; }

        public string? OperatingSystem { get; set; }
        public bool? StartupEnabled { get; set; }
    }
}