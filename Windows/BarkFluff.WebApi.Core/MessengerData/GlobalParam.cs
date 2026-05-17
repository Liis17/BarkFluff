using BarkFluff.Proto.Identity;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BarkFluff.WebApi.Core.MessengerData
{
    /// <summary>
    /// Режим отображения уведомлений
    /// </summary>
    public enum NotificationDisplayMode
    {
        /// <summary>
        /// Уведомления полностью отключены
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Скрыть отправителя и содержимое ("Вам пришло новое сообщение")
        /// </summary>
        HiddenContent = 1,

        /// <summary>
        /// Показать отправителя, скрыть содержимое
        /// </summary>
        SenderOnly = 2,

        /// <summary>
        /// Показать отправителя и текст, без превью медиа
        /// </summary>
        FullTextNoPreview = 3,

        /// <summary>
        /// Полное отображение: отправитель, текст и превью медиа
        /// </summary>
        FullWithPreview = 4
    }

    public class GlobalParam
    {
        #region Приложение 
        public string SocketBeacon { get; set; } = string.Empty;
        public string SocketUsers { get; set; } = string.Empty;
        public string SocketIdentity { get; set; } = string.Empty;
        public string SocketFiles { get; set; } = string.Empty;
        public string SocketMessages { get; set; } = string.Empty;
        public string SocketUpdates { get; set; } = string.Empty;
        public string SocketOnliner { get; set; } = string.Empty;
        public string SocketFastAuth { get; set; } = string.Empty;
        public string AppPath { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public string ServerDescription { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public ClientColors Colors { get; set; } = new ClientColors();
        public string IpAddress { get; set; } = string.Empty;
        public NotificationDisplayMode NotificationMode { get; set; } = NotificationDisplayMode.FullWithPreview;

        /// <summary>
        /// Радиус скругления углов пузырьков сообщений (0–20). Локальная настройка.
        /// </summary>
        public int MessageBubbleCornerRadius { get; set; } = 12;

        /// <summary>
        /// Тема приложения (зеркало ThemeRegistryHelper): "light" | "dark" | "system".
        /// </summary>
        public string AppTheme { get; set; } = "system";

        /// <summary>
        /// Язык интерфейса (зеркало LanguageRegistryHelper): "system" | "ru" | "en".
        /// </summary>
        public string AppLanguage { get; set; } = "system";

        /// <summary>
        /// Звук уведомлений (отдельно от NotificationMode).
        /// </summary>
        public bool NotificationSoundEnabled { get; set; } = true;

        /// <summary>
        /// Включено ли размытие фона чата.
        /// </summary>
        public bool BackgroundBlurEnabled { get; set; } = false;

        /// <summary>
        /// Радиус размытия фона чата (1–25).
        /// </summary>
        public int BackgroundBlurRadius { get; set; } = 12;

        /// <summary>
        /// Затемнение фона чата в процентах (0–100).
        /// </summary>
        public int BackgroundDimPercent { get; set; } = 0;

        /// <summary>
        /// FileId выбранного фона чата (из списка ChatBackgroundFileIds Personalization).
        /// </summary>
        public string CurrentBackgroundFileId { get; set; } = string.Empty;

        #endregion
        #region Пользователь
        #region Токены пользователя
        public Token RefreshToken { get; set; } = null!;
        public Token AccessToken { get; set; } = null!;
        #endregion

        public long UserId { get; set; } = 0;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PictureId { get; set; } = string.Empty;
        public string PictureUrl { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime RegistrationDate { get; set; } = DateTime.MinValue;

        #endregion
        #region Сохранение/загрузка настроек
        private const int SaltSize = 16;   // 128 бит
        private const int KeySize = 32;    // 256 бит
        private const int NonceSize = 12;  // 96 бит — рекомендуемая длина для AES-GCM
        private const int TagSize = 16;    // 128 бит — длина аутентификационного тэга
        private const int Iterations = 100_000;

        // "BFV2" — маркер формата AES-GCM. Старый CBC-формат не поддерживается.
        private static readonly byte[] FileMagic = [0x42, 0x46, 0x56, 0x32];
        private const int HeaderSize = 4 + SaltSize + NonceSize + TagSize;

        public string AppPass { get; set; } = string.Empty;
        #endregion

        public static void Save(GlobalParam param, string filePath, string userPin)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var key = DeriveKeyFromPin(userPin, salt);

            var json = JsonSerializer.Serialize(param);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var ciphertext = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plainBytes, ciphertext, tag);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plainBytes);
            }

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(FileMagic);
            fs.Write(salt);
            fs.Write(nonce);
            fs.Write(tag);
            fs.Write(ciphertext);
        }

        public static GlobalParam Load(string filePath, string userPin)
        {
            var allBytes = File.ReadAllBytes(filePath);
            if (allBytes.Length < HeaderSize)
                throw new CryptographicException("Файл GlobalParam слишком короткий или повреждён.");
            if (!allBytes.AsSpan(0, FileMagic.Length).SequenceEqual(FileMagic))
                throw new CryptographicException("Неподдерживаемый формат файла GlobalParam (ожидается BFV2).");

            var span = allBytes.AsSpan(FileMagic.Length);
            var salt = span[..SaltSize].ToArray();
            var nonce = span.Slice(SaltSize, NonceSize).ToArray();
            var tag = span.Slice(SaltSize + NonceSize, TagSize).ToArray();
            var ciphertext = span[(SaltSize + NonceSize + TagSize)..].ToArray();

            var key = DeriveKeyFromPin(userPin, salt);
            var plainBytes = new byte[ciphertext.Length];

            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<GlobalParam>(json)
                   ?? throw new CryptographicException("Не удалось десериализовать GlobalParam.");
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
                if (allBytes.Length < HeaderSize)
                    return false;
                if (!allBytes.AsSpan(0, FileMagic.Length).SequenceEqual(FileMagic))
                    return false;

                var span = allBytes.AsSpan(FileMagic.Length);
                var salt = span[..SaltSize].ToArray();
                var nonce = span.Slice(SaltSize, NonceSize).ToArray();
                var tag = span.Slice(SaltSize + NonceSize, TagSize).ToArray();
                var ciphertext = span[(SaltSize + NonceSize + TagSize)..].ToArray();

                var key = DeriveKeyFromPin(userPin, salt);
                var plainBytes = new byte[ciphertext.Length];

                try
                {
                    using var aes = new AesGcm(key, TagSize);
                    aes.Decrypt(nonce, ciphertext, tag, plainBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }

                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
