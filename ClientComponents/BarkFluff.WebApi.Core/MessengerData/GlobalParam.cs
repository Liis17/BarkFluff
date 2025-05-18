using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using BarkFluff.Proto.Identity;

namespace BarkFluff.WebApi.Core.MessengerData
{
    public class GlobalParam
    {
        #region Приложение 
        public string SocketBeacon { get; set; } = string.Empty; 
        public string SocketUsers { get; set; } = string.Empty; 
        public string SocketIdentity { get; set; } = string.Empty;
        public string AppPath { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public string ServerDescription { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public ClientColors Colors { get; set; } = new ClientColors();

        #endregion
        #region Пользователь
        public string UserName { get; set; } = string.Empty;
        public string UserPass { get; set; } = string.Empty;
        public Token RefreshToken { get; set; } = new Token();
        public Token AccessToken { get; set; } = new Token();

        #endregion
        #region Сохранение/загрузка настроек
        private const int SaltSize = 16; // 128 бит
        private const int KeySize = 32;  // 256 бит
        private const int Iterations = 100_000;

        public string AppPass { get; set; } = string.Empty;
        #endregion

        public static void Save(GlobalParam param, string filePath, string userPin)
        {
            // Генерация соли
            var salt = RandomNumberGenerator.GetBytes(SaltSize);

            // Получение ключа из PIN-кода
            var key = DeriveKeyFromPin(userPin, salt);

            var json = JsonSerializer.Serialize(param);
            var plainBytes = Encoding.UTF8.GetBytes(json);

            // Шифрование
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor();
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Сохраняем: [salt][IV][ciphertext]
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(salt, 0, salt.Length);
            fs.Write(iv, 0, iv.Length);
            fs.Write(encryptedBytes, 0, encryptedBytes.Length);
        }

        public static GlobalParam Load(string filePath, string userPin)
        {
            var allBytes = File.ReadAllBytes(filePath);

            // Извлекаем: [salt][IV][ciphertext]
            var salt = new byte[SaltSize];
            Array.Copy(allBytes, 0, salt, 0, SaltSize);

            var iv = new byte[16]; // IV для AES — 16 байт
            Array.Copy(allBytes, SaltSize, iv, 0, iv.Length);

            var encryptedBytes = new byte[allBytes.Length - SaltSize - iv.Length];
            Array.Copy(allBytes, SaltSize + iv.Length, encryptedBytes, 0, encryptedBytes.Length);

            var key = DeriveKeyFromPin(userPin, salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            var json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<GlobalParam>(json);
        }

        private static byte[] DeriveKeyFromPin(string pin, byte[] salt)
        {
            using var rfc2898 = new Rfc2898DeriveBytes(pin, salt, Iterations, HashAlgorithmName.SHA256);
            return rfc2898.GetBytes(KeySize);
        }

        public static bool VerifyPassword(string filePath, string userPin)
        {
            try
            {
                var allBytes = File.ReadAllBytes(filePath);

                var salt = new byte[SaltSize];
                Array.Copy(allBytes, 0, salt, 0, SaltSize);

                var iv = new byte[16]; // IV для AES — 16 байт
                Array.Copy(allBytes, SaltSize, iv, 0, iv.Length);

                var encryptedBytes = new byte[allBytes.Length - SaltSize - iv.Length];
                Array.Copy(allBytes, SaltSize + iv.Length, encryptedBytes, 0, encryptedBytes.Length);

                var key = DeriveKeyFromPin(userPin, salt);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

                var json = Encoding.UTF8.GetString(decryptedBytes);
                JsonSerializer.Deserialize<GlobalParam>(json); 

                return true; 
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
