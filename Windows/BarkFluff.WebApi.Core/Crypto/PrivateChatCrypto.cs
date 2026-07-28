using Konscious.Security.Cryptography;

using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.WebApi.Core.Crypto
{
    /// <summary>
    /// E2E приватных чатов: Argon2id(passphrase, salt) → 32-байтный AES-ключ →
    /// AES-256-GCM шифрование/расшифровка. Сервер ключ никогда не видит.
    /// </summary>
    /// <remarks>
    /// Параметры KDF, константа верификатора и формат AAD обязаны совпадать с остальными
    /// клиентами платформы (эталон — Android, <c>core/.../crypto/PrivateChatCrypto.kt</c>):
    /// любое расхождение делает переписку нечитаемой на другой стороне.
    /// </remarks>
    public static class PrivateChatCrypto
    {
        public const int KeyBytes = 32;
        public const int SaltBytes = 32;
        public const int NonceBytes = 12;
        public const int GcmTagBytes = 16; // 128 бит

        private const int Argon2Iterations = 3;
        private const int Argon2MemoryKib = 64 * 1024;
        private const int Argon2Parallelism = 4;

        private static readonly byte[] VerifierConstant = Encoding.UTF8.GetBytes("BARKFLUFF_PRIVATE_CHAT_VERIFIER");

        /// <summary>
        /// Криптостойкий salt для Argon2id. Генерирует создатель чата, сервер хранит его открыто.
        /// </summary>
        public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltBytes);

        /// <summary>
        /// Криптостойкий nonce для AES-GCM. Обязан быть уникальным для каждого шифрования одним ключом.
        /// </summary>
        public static byte[] GenerateNonce() => RandomNumberGenerator.GetBytes(NonceBytes);

        /// <summary>
        /// Вывести 32-байтный AES-ключ из passphrase и salt через Argon2id (v1.3, t=3, m=64 МиБ, p=4).
        /// Операция тяжёлая (~1 с и 64 МиБ памяти) — вызывать вне UI-потока.
        /// </summary>
        public static byte[] DeriveKey(string passphrase, byte[] salt)
        {
            if (salt == null || salt.Length < 16)
                throw new ArgumentException("Argon2 salt must be at least 16 bytes", nameof(salt));

            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
            {
                Salt = salt,
                Iterations = Argon2Iterations,
                MemorySize = Argon2MemoryKib,
                DegreeOfParallelism = Argon2Parallelism
            };

            return argon2.GetBytes(KeyBytes);
        }

        /// <summary>
        /// HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER"). Отправляется на сервер при создании чата,
        /// чтобы приглашённый мог проверить passphrase до вступления в чат.
        /// </summary>
        public static byte[] ComputeVerifier(byte[] key)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(VerifierConstant);
        }

        /// <summary>
        /// Проверка верификатора за постоянное время (защита от timing-атак).
        /// </summary>
        public static bool ValidateVerifier(byte[] key, byte[] expectedVerifier)
            => CryptographicOperations.FixedTimeEquals(ComputeVerifier(key), expectedVerifier);

        /// <summary>
        /// AES-256-GCM шифрование. Возвращает шифротекст с присоединённым тегом (как в JCA/libsodium)
        /// и сгенерированный nonce.
        /// </summary>
        public static (byte[] ciphertext, byte[] nonce) Encrypt(byte[] plaintext, byte[] key, byte[] aad)
        {
            if (key == null || key.Length != KeyBytes)
                throw new ArgumentException($"AES-256 key must be {KeyBytes} bytes", nameof(key));

            var nonce = GenerateNonce();
            var ciphertext = new byte[plaintext.Length + GcmTagBytes];

            using var aes = new AesGcm(key, GcmTagBytes);
            // Тег кладём в хвост шифротекста: Java/Kotlin-клиенты получают из Cipher.doFinal
            // именно такой формат, а .NET требует разделённые буферы.
            aes.Encrypt(nonce, plaintext, ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length), aad);

            return (ciphertext, nonce);
        }

        /// <summary>
        /// AES-256-GCM расшифровка. Бросает <see cref="CryptographicException"/> при несовпадении
        /// тега, AAD или nonce, а также на повреждённом шифротексте.
        /// </summary>
        public static byte[] Decrypt(byte[] ciphertextWithTag, byte[] nonce, byte[] key, byte[] aad)
        {
            if (key == null || key.Length != KeyBytes)
                throw new ArgumentException($"AES-256 key must be {KeyBytes} bytes", nameof(key));
            if (nonce == null || nonce.Length != NonceBytes)
                throw new ArgumentException($"AES-GCM nonce must be {NonceBytes} bytes", nameof(nonce));
            if (ciphertextWithTag == null || ciphertextWithTag.Length < GcmTagBytes)
                throw new CryptographicException("Шифротекст короче тега аутентификации");

            var payloadLength = ciphertextWithTag.Length - GcmTagBytes;
            var plaintext = new byte[payloadLength];

            using var aes = new AesGcm(key, GcmTagBytes);
            aes.Decrypt(
                nonce,
                ciphertextWithTag.AsSpan(0, payloadLength),
                ciphertextWithTag.AsSpan(payloadLength),
                plaintext,
                aad);

            return plaintext;
        }

        /// <summary>
        /// Стандартный AAD приватного сообщения: <c>barkfluff:private:{chatId}</c>.
        /// Не даёт «переподложить» шифротекст одного чата в другой.
        /// </summary>
        public static byte[] PrivateChatAad(string chatId) => Encoding.UTF8.GetBytes($"barkfluff:private:{chatId}");
    }
}
