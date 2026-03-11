using System;
using System.Security.Cryptography;

namespace RSSAPI.Utilities
{
    public static class PasswordHasher
    {
        private const int saltSize = 16;
        private const int keySize = 32;
        private const int iterations = 10000;

        public static (string Hash, string Salt) HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(saltSize);

            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                keySize);

            string hashString = Convert.ToBase64String(hash);
            string saltString = Convert.ToBase64String(salt);

            return (hashString, saltString);
        }

        public static bool VerifyPassword(string password, string hash, string salt)
        {
            byte[] saltBytes = Convert.FromBase64String(salt);

            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                iterations,
                HashAlgorithmName.SHA256,
                keySize);

            string computedHash = Convert.ToBase64String(hashBytes);

            return computedHash == hash;
        }
    }
}