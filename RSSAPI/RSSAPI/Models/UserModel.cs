using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace RSSAPI.Models
{
    [Table("tblUser")]
    public class User
    {
        [Key]
        [Column("userID")]
        public int UserID { get; set; }

        [Required]
        [Column("username")]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [JsonIgnore]
        [Column("passwordHash")]
        [StringLength(128)]
        public string PasswordHash { get; set; } = string.Empty;

        [JsonIgnore]
        [Column("passwordSalt")]
        [StringLength(128)]
        public string PasswordSalt { get; set; } = string.Empty;

        [Column("email")]
        [StringLength(100)]
        public string? Email { get; set; }

        [Column("firstName")]
        [StringLength(50)]
        public string? FirstName { get; set; }

        [Column("lastName")]
        [StringLength(50)]
        public string? LastName { get; set; }

        [Required]
        [Column("role")]
        [StringLength(20)]
        public string Role { get; set; } = "User";

        [Required]
        [Column("isActive")]
        public bool IsActive { get; set; } = true;

        [Required]
        [Column("createdDate")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Column("lastLoginDate")]
        public DateTime? LastLoginDate { get; set; }

        [JsonIgnore]
        [Column("sessionToken")]
        [StringLength(128)]
        public string? SessionToken { get; set; }

        [JsonIgnore]
        [Column("tokenExpiryDate")]
        public DateTime? TokenExpiryDate { get; set; }
    }
}