using System.Text.Json.Serialization;

namespace RssSite.Components.Models
{
    public class UserConfigurationModel
    {
        public int UserID { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; } = true;

        [JsonIgnore]
        public string? CurrentPassword { get; set; }

        [JsonIgnore]
        public string? NewPassword { get; set; }

        [JsonIgnore]
        public string? ConfirmNewPassword { get; set; }
    }
}
