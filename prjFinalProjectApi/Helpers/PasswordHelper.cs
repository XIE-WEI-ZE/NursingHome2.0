using System.Security.Cryptography;
using System.Text;

namespace prjFinalProjectApi.Helpers
{
    public static class PasswordHelper
    {
        // 建立 Hash + Salt
        public static (string Hash, string Salt) HashPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("密碼不能為空白");

            var saltBytes = new byte[32];
            RandomNumberGenerator.Fill(saltBytes);
            var salt = Convert.ToBase64String(saltBytes);
            var hash = ComputeHash(password, salt);
            return (hash, salt);
        }

        // 驗證密碼
        public static bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            var hash = ComputeHash(password, storedSalt);
            return hash == storedHash;
        }

        private static string ComputeHash(string password, string base64Salt)
        {
            var salt = Convert.FromBase64String(base64Salt);
            using var hmac = new HMACSHA256(salt);
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}
