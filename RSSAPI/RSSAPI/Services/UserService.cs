using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using RSSAPI.Models;
using RSSAPI.Data;
using RSSAPI.Utilities;
using Serilog;

namespace RSSAPI.Services
{
    public class UserService(ScreenDbContext context, IConfiguration configuration)
    {
        private readonly ScreenDbContext databaseContext = context;
        private readonly IConfiguration configuration = configuration;

        public async Task<(bool IsAuthenticated, string JwtToken, string UserRole, string AuthenticationErrorMessage)> AuthenticateUserAsync(string username, string password)
        {
            try
            {
                Log.Information("[AUTH] Authenticating user: {Username}", username);
                User? user = await databaseContext.TblUsers
                    .Where(u => u.Username == username && u.IsActive)
                    .Select(u => new User
                    {
                        UserID = u.UserID,
                        Username = u.Username,
                        PasswordHash = u.PasswordHash,
                        PasswordSalt = u.PasswordSalt,
                        Role = u.Role,
                        IsActive = u.IsActive,
                        LastLoginDate = u.LastLoginDate
                    })
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    Log.Warning("[AUTH] User not found or inactive: {Username}", username);
                    return (false, string.Empty, string.Empty, "User not found or inactive");
                }

                bool isPasswordValid = PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt);
                if (!isPasswordValid)
                {
                    Log.Warning("[AUTH] Invalid password for user: {Username}", username);
                    return (false, string.Empty, string.Empty, "Invalid password");
                }

                string secretKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is missing.");
                SymmetricSecurityKey securityKey = new(Encoding.UTF8.GetBytes(secretKey));
                SigningCredentials credentials = new(securityKey, SecurityAlgorithms.HmacSha256);

                List<Claim> claims =
                [
            new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim("role", user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];

                JwtSecurityToken token = new(
                    issuer: configuration["Jwt:Issuer"] ?? "RSSAPI",
                    audience: configuration["Jwt:Audience"] ?? "RSSSITE",
                    claims: claims,
                    expires: DateTime.UtcNow.AddHours(8),
                    signingCredentials: credentials
                );

                string tokenString = new JwtSecurityTokenHandler().WriteToken(token);
                user.LastLoginDate = DateTime.UtcNow;
                await databaseContext.SaveChangesAsync();

                return (true, tokenString, user.Role, string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AUTH] Authentication error for user {Username}: {Message}", username, ex.Message);
                return (false, string.Empty, string.Empty, "An error occurred during authentication");
            }
        }

        public async Task<bool> ValidateTokenAsync(string jwtToken)
        {
            JwtSecurityTokenHandler tokenHandler = new();
            string jwtSecretKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is missing.");
            byte[] keyBytes = Encoding.UTF8.GetBytes(jwtSecretKey);

            try
            {
                tokenHandler.ValidateToken(jwtToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                return await Task.FromResult(true);
            }
            catch (Exception exception)
            {
                Log.Warning("[AUTH] Invalid or expired token: {Error}", exception.Message);
                return await Task.FromResult(false);
            }
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await databaseContext.TblUsers
                .Select(user => new User
                {
                    UserID = user.UserID,
                    Username = user.Username,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Role = user.Role,
                    IsActive = user.IsActive,
                    CreatedDate = user.CreatedDate,
                    LastLoginDate = user.LastLoginDate
                })
                .ToListAsync();
        }

        public async Task<bool> CreateUserAsync(User newUser, string password)
        {
            bool doesUserExist = await databaseContext.TblUsers.AnyAsync(user => user.Username == newUser.Username);
            if (doesUserExist)
            {
                Log.Warning("[AUTH] Cannot create user {Username} - username already exists", newUser.Username);
                return false;
            }

            (string hashedPassword, string passwordSalt) = PasswordHasher.HashPassword(password);
            newUser.PasswordHash = hashedPassword;
            newUser.PasswordSalt = passwordSalt;
            newUser.CreatedDate = DateTime.UtcNow;

            databaseContext.TblUsers.Add(newUser);
            await databaseContext.SaveChangesAsync();

            Log.Information("[AUTH] User {Username} created successfully", newUser.Username);
            return true;
        }

        public async Task<bool> UpdateUserAsync(User updatedUser)
        {
            User? existingUser = await databaseContext.TblUsers.FindAsync(updatedUser.UserID);
            if (existingUser == null) return false;

            existingUser.Email = updatedUser.Email;
            existingUser.FirstName = updatedUser.FirstName;
            existingUser.LastName = updatedUser.LastName;
            existingUser.Role = updatedUser.Role;
            existingUser.IsActive = updatedUser.IsActive;

            await databaseContext.SaveChangesAsync();
            Log.Information("[AUTH] User {Username} updated successfully", updatedUser.Username);
            return true;
        }

        public async Task<bool> ChangePasswordAsync(int userID, string newPassword)
        {
            User? existingUser = await databaseContext.TblUsers.FindAsync(userID);
            if (existingUser == null) return false;

            (string hashedPassword, string passwordSalt) = PasswordHasher.HashPassword(newPassword);
            existingUser.PasswordHash = hashedPassword;
            existingUser.PasswordSalt = passwordSalt;

            await databaseContext.SaveChangesAsync();
            Log.Information("[AUTH] Password changed for user {Username}", existingUser.Username);
            return true;
        }

        public async Task<bool> DeleteUserAsync(int userID)
        {
            User? existingUser = await databaseContext.TblUsers.FindAsync(userID);
            if (existingUser == null) return false;

            databaseContext.TblUsers.Remove(existingUser);
            await databaseContext.SaveChangesAsync();
            Log.Information("[AUTH] User {Username} deleted successfully", existingUser.Username);
            return true;
        }

        public async Task<bool> UpdateUserProfileAsync(int userID, string? email, string? firstName, string? lastName)
        {
            User? existingUser = await databaseContext.TblUsers.FindAsync(userID);
            if (existingUser == null) return false;

            existingUser.Email = email ?? existingUser.Email;
            existingUser.FirstName = firstName ?? existingUser.FirstName;
            existingUser.LastName = lastName ?? existingUser.LastName;

            await databaseContext.SaveChangesAsync();
            return true;
        }
        public async Task<User?> GetUserByIDAsync(int userID)
        {
            return await databaseContext.TblUsers
                .Where(user => user.UserID == userID)
                .Select(user => new User
                {
                    UserID = user.UserID,
                    Username = user.Username,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Role = user.Role,
                    IsActive = user.IsActive,
                    CreatedDate = user.CreatedDate,
                    LastLoginDate = user.LastLoginDate
                })
                .FirstOrDefaultAsync();
        }
    }
}