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

        // Унаследованный формат: PBKDF2-SHA256, 100k итераций. Читаем для миграции старых файлов.
        private const int LegacyIterationsV2 = 100_000;

        // Текущий формат: PBKDF2-SHA512, 600k итераций (OWASP 2023+).
        private const int IterationsV3 = 600_000;

        // "BFV2" — старый формат (PBKDF2-SHA256-100k), "BFV3" — текущий (PBKDF2-SHA512-600k).
        private static readonly byte[] FileMagicV2 = [0x42, 0x46, 0x56, 0x32];
        private static readonly byte[] FileMagicV3 = [0x42, 0x46, 0x56, 0x33];
        private const int HeaderSize = 4 + SaltSize + NonceSize + TagSize;

        public string AppPass { get; set; } = string.Empty;
        #endregion

        public static void Save(GlobalParam param, string filePath, string userPin)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var key = DeriveKeyV3(userPin, salt);

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
            fs.Write(FileMagicV3);
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

            var magic = allBytes.AsSpan(0, FileMagicV3.Length);
            bool isV3 = magic.SequenceEqual(FileMagicV3);
            bool isV2 = magic.SequenceEqual(FileMagicV2);
            if (!isV3 && !isV2)
                throw new CryptographicException("Неподдерживаемый формат файла GlobalParam (ожидается BFV2 или BFV3).");

            var span = allBytes.AsSpan(FileMagicV3.Length);
            var salt = span[..SaltSize].ToArray();
            var nonce = span.Slice(SaltSize, NonceSize).ToArray();
            var tag = span.Slice(SaltSize + NonceSize, TagSize).ToArray();
            var ciphertext = span[(SaltSize + NonceSize + TagSize)..].ToArray();

            var key = isV3 ? DeriveKeyV3(userPin, salt) : DeriveKeyV2Legacy(userPin, salt);
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

            try
            {
                var json = Encoding.UTF8.GetString(plainBytes);
                if (string.IsNullOrWhiteSpace(json))
                    throw new CryptographicException("GlobalParam расшифрован, но содержимое пустое.");

                GlobalParam? deserialized;
                try
                {
                    deserialized = JsonSerializer.Deserialize<GlobalParam>(json);
                }
                catch (JsonException ex)
                {
                    throw new CryptographicException("Не удалось разобрать JSON GlobalParam.", ex);
                }

                return deserialized
                       ?? throw new CryptographicException("Не удалось десериализовать GlobalParam: получен null.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        private static byte[] DeriveKeyV3(string pin, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(pin, salt, IterationsV3, HashAlgorithmName.SHA512, KeySize);
        }

        private static byte[] DeriveKeyV2Legacy(string pin, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(pin, salt, LegacyIterationsV2, HashAlgorithmName.SHA256, KeySize);
        }

        public static bool VerifyPassword(string filePath, string userPin)
        {
            try
            {
                var allBytes = File.ReadAllBytes(filePath);
                if (allBytes.Length < HeaderSize)
                    return false;

                var magic = allBytes.AsSpan(0, FileMagicV3.Length);
                bool isV3 = magic.SequenceEqual(FileMagicV3);
                bool isV2 = magic.SequenceEqual(FileMagicV2);
                if (!isV3 && !isV2)
                    return false;

                var span = allBytes.AsSpan(FileMagicV3.Length);
                var salt = span[..SaltSize].ToArray();
                var nonce = span.Slice(SaltSize, NonceSize).ToArray();
                var tag = span.Slice(SaltSize + NonceSize, TagSize).ToArray();
                var ciphertext = span[(SaltSize + NonceSize + TagSize)..].ToArray();

                var key = isV3 ? DeriveKeyV3(userPin, salt) : DeriveKeyV2Legacy(userPin, salt);
                var plainBytes = new byte[ciphertext.Length];

                try
                {
                    using var aes = new AesGcm(key, TagSize);
                    aes.Decrypt(nonce, ciphertext, tag, plainBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                    CryptographicOperations.ZeroMemory(plainBytes);
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
