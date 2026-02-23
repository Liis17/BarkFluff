# Техническое описание шифрования в BarkFluff

**Дата вступления в силу:** 24 января 2026 г.
**Последнее обновление:** 24 января 2026 г.

---

## 1. Введение

Безопасность и конфиденциальность пользовательских данных являются приоритетом для BarkFluff. Настоящий документ предоставляет детальное техническое описание методов шифрования, применяемых на всех уровнях системы.

### 1.1 Уровни защиты

BarkFluff применяет **многоуровневую систему защиты**:

1. **Шифрование в состоянии покоя (Encryption at Rest)** — защита данных, хранящихся на серверах
2. **Шифрование при передаче (Encryption in Transit)** — защита данных при передаче по сети
3. **Шифрование на стороне клиента (Client-Side Encryption)** — защита данных на устройстве пользователя
4. **Контроль доступа (Access Control)** — ограничение доступа к данным на уровне приложения

**Примечание:** End-to-End шифрование (E2EE) в настоящее время **НЕ реализовано**, но планируется в будущих версиях.

---

## 2. Шифрование в состоянии покоя (Encryption at Rest)

### 2.1 База данных PostgreSQL

#### 2.1.1 Transparent Data Encryption (TDE)

**Метод:** Все данные в PostgreSQL защищены на уровне хранилища с использованием TDE.

**Описание:**
- Данные автоматически шифруются при записи на диск
- Данные автоматически расшифровываются при чтении (прозрачно для приложения)
- Шифрование происходит на уровне **страниц базы данных** (8 КБ блоки)

**Алгоритм:**
- **AES-256** (Advanced Encryption Standard, 256-bit key)
- Режим: **CBC (Cipher Block Chaining)** или **XTS (XEX-based Tweaked Codebook mode)**

**Управление ключами:**
- Мастер-ключ хранится в **защищенном хранилище ключей** (KMS — Key Management Service)
- Ключи шифрования регулярно ротируются (рекомендуется каждые 90 дней)

**Конфигурация** (пример для Docker):
```yaml
services:
  postgres:
    image: postgres:15
    environment:
      POSTGRES_INITDB_ARGS: "-E UTF8 --data-checksums"
    volumes:
      - postgres_data:/var/lib/postgresql/data  # Encrypted volume
    command:
      - "postgres"
      - "-c"
      - "ssl=on"
      - "-c"
      - "ssl_cert_file=/etc/ssl/certs/server.crt"
      - "-c"
      - "ssl_key_file=/etc/ssl/private/server.key"
```

**Что защищено:**
- ✅ Все таблицы во всех микросервисах (Identity, Users, Messages, Files)
- ✅ Пароли (хеши): bcrypt с cost factor 12
- ✅ Refresh Tokens (зашифрованы AES-256 перед сохранением)
- ✅ Сообщения (текст, метаданные)
- ✅ Журналы безопасности (логи аутентификации, IP-адреса)

#### 2.1.2 Шифрование на уровне столбцов (Column-Level Encryption)

Для особо чувствительных данных применяется **дополнительное шифрование на уровне столбцов**:

**Защищенные поля:**
- `Identity.Users.PasswordHash` — хеш пароля (bcrypt + AES-256)
- `Identity.Sessions.RefreshToken` — токены обновления (AES-256-GCM)
- `Users.TwoFactorSecrets.Secret` — секреты 2FA (AES-256-GCM)

**Реализация** (пример на C#):
```csharp
public class EncryptionService
{
    private readonly byte[] _key; // 256-bit ключ из Configuration Service

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        aes.Mode = CipherMode.GCM;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // IV + Encrypted Data + Auth Tag (GCM)
        return Convert.ToBase64String(aes.IV.Concat(encryptedBytes).ToArray());
    }
}
```

---

### 2.2 Файловое хранилище Minio (S3-compatible)

#### 2.2.1 Server-Side Encryption (SSE)

**Метод:** Все файлы автоматически шифруются на стороне сервера при загрузке.

**Алгоритм:**
- **AES-256-GCM** (Galois/Counter Mode)
- **Аутентичное шифрование** (authenticated encryption) — защита от подделки данных

**Режим шифрования:**
- **SSE-S3** (Minio управляет ключами) или
- **SSE-KMS** (ключи хранятся в внешнем KMS)

**Конфигурация Minio** (docker-compose):
```yaml
services:
  minio:
    image: minio/minio:latest
    environment:
      MINIO_ROOT_USER: admin
      MINIO_ROOT_PASSWORD: ${MINIO_PASSWORD}
      MINIO_KMS_SECRET_KEY: "my-minio-key:bXltaW5pb2tleQ==" # Base64-encoded 256-bit key
    command: server /data --console-address ":9001"
    volumes:
      - minio_data:/data
```

**Загрузка файла с шифрованием** (клиентский код):
```csharp
var minioClient = new MinioClient()
    .WithEndpoint("minio:9000")
    .WithCredentials("admin", "password")
    .Build();

var putObjectArgs = new PutObjectArgs()
    .WithBucket("barkfluff-files")
    .WithObject("avatar-12345.jpg")
    .WithFileName("/tmp/avatar.jpg")
    .WithServerSideEncryption(sse); // SSE-S3 или SSE-KMS

await minioClient.PutObjectAsync(putObjectArgs);
```

**Что защищено:**
- ✅ Аватары пользователей
- ✅ Вложения в сообщениях (изображения, видео, документы)
- ✅ Превью файлов (thumbnails)

**Метаданные файлов:**
- Метаданные (имя файла, размер, MIME-type) хранятся в PostgreSQL (также зашифрованы TDE)

---

### 2.3 Кеш Redis

#### 2.3.1 Шифрование данных в Redis

**Статус:** Redis используется для **краткосрочного кеширования** (TTL 5 минут), поэтому шифрование опционально.

**Что хранится в Redis:**
- Онлайн-статусы пользователей (`user:12345:online` → `true`)
- Время последнего посещения (`user:12345:last_seen` → `2026-01-24T12:00:00Z`)

**Защита:**
- **Шифрование TLS** для соединений между сервисами и Redis
- **Аутентификация:** Redis защищен паролем (хранится в Configuration Service)
- **Изоляция сети:** Redis недоступен извне, только из внутренней сети Docker/Kubernetes

**Конфигурация Redis** (с TLS):
```yaml
redis:
  image: redis:7-alpine
  command: >
    redis-server
    --requirepass ${REDIS_PASSWORD}
    --tls-port 6380
    --port 0
    --tls-cert-file /etc/ssl/certs/redis.crt
    --tls-key-file /etc/ssl/private/redis.key
    --tls-ca-cert-file /etc/ssl/certs/ca.crt
```

---

### 2.4 Шифрование на стороне клиента (Desktop/Mobile)

#### 2.4.1 Локальное хранилище настроек (GlobalParam.json)

**Файл:** `%AppData%\BarkFluff\GlobalParam.json` (Windows) или `~/.config/barkfluff/GlobalParam.json` (Linux)

**Содержимое:**
- Настройки приложения (тема, язык)
- Access Token (JWT, срок действия 15 минут)
- Refresh Token (срок действия 30 дней)
- Device ID (UUID)

**Шифрование:**
- **Алгоритм:** AES-256-CBC
- **Ключ:** Производный от PIN-кода пользователя (если установлен) с использованием **PBKDF2**

**Реализация**:
```csharp
public class LocalStorageEncryption
{
    private const int Iterations = 100_000; // PBKDF2 iterations
    private const int KeySize = 256; // bits

    public static byte[] DeriveKey(string pin, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize / 8); // 32 bytes
    }

    public static string EncryptJson(string json, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = DeriveKey(pin, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Format: [Salt (16 bytes)][IV (16 bytes)][Encrypted Data]
        var result = salt.Concat(aes.IV).Concat(encryptedBytes).ToArray();
        return Convert.ToBase64String(result);
    }
}
```

**Если PIN не установлен:**
- Используется ключ по умолчанию, хранящийся в защищенном хранилище ОС:
  - **Windows:** DPAPI (Data Protection API)
  - **Linux:** GNOME Keyring / KWallet
  - **macOS:** Keychain

#### 2.4.2 Локальный кеш сообщений (LiteDB)

**Файл:** `%AppData%\BarkFluff\cache.db`

**Содержимое:**
- Последние 1000 сообщений для каждого чата (для офлайн-доступа)
- Превью файлов

**Шифрование:**
- **Статус:** НЕ зашифровано (поскольку это только кеш, не содержит секретов)
- **Защита:** Доступ ограничен правами файловой системы (только текущий пользователь ОС)

**Рекомендация:** В будущих версиях планируется шифрование LiteDB с использованием того же ключа, что и GlobalParam.json.

---

## 3. Шифрование при передаче (Encryption in Transit)

### 3.1 gRPC / HTTP/2 с TLS 1.3

#### 3.1.1 Описание

Все соединения между клиентом и сервером защищены протоколом **TLS 1.3** (Transport Layer Security).

**Преимущества TLS 1.3:**
- **Более быстрое рукопожатие** (0-RTT и 1-RTT)
- **Удаление устаревших алгоритмов** (RC4, MD5, SHA-1, DES)
- **Perfect Forward Secrecy** (невозможно расшифровать прошлые сессии даже при компрометации ключа сервера)

**Поддерживаемые cipher suites:**
- `TLS_AES_256_GCM_SHA384` (рекомендуется)
- `TLS_CHACHA20_POLY1305_SHA256`
- `TLS_AES_128_GCM_SHA256`

#### 3.1.2 Конфигурация сервера (Kestrel в .NET)

**Program.cs** (пример для Identity Service):
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(7000, listenOptions =>
    {
        // Для Production: использовать сертификат Let's Encrypt или коммерческий CA
        listenOptions.UseHttps("/certs/server.pfx", "password", httpsOptions =>
        {
            httpsOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12; // Fallback для старых клиентов
        });

        listenOptions.Protocols = HttpProtocols.Http2; // gRPC требует HTTP/2
    });
});
```

**Для разработки** (самоподписанный сертификат):
```bash
# Генерация самоподписанного сертификата
dotnet dev-certs https -ep ${HOME}/.aspnet/https/aspnetapp.pfx -p password
dotnet dev-certs https --trust
```

#### 3.1.3 Проверка сертификата на клиенте

**C# (WPF клиент):**
```csharp
var httpHandler = new HttpClientHandler();

#if DEBUG
// Для разработки: отключить проверку сертификата (ТОЛЬКО ДЛЯ ТЕСТИРОВАНИЯ!)
httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#else
// Для Production: строгая проверка
httpHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
{
    if (errors == SslPolicyErrors.None) return true;

    // Логирование ошибки
    _logger.LogError($"SSL Error: {errors}");
    return false;
};
#endif

var grpcChannel = GrpcChannel.ForAddress("https://api.barkfluff.com:7000", new GrpcChannelOptions
{
    HttpHandler = httpHandler
});
```

---

### 3.2 RabbitMQ с TLS

#### 3.2.1 Описание

Внутренние соединения между микросервисами через RabbitMQ также защищены TLS.

**Конфигурация RabbitMQ** (docker-compose):
```yaml
rabbitmq:
  image: rabbitmq:3-management-alpine
  environment:
    RABBITMQ_SSL_CERTFILE: /etc/rabbitmq/certs/server.crt
    RABBITMQ_SSL_KEYFILE: /etc/rabbitmq/certs/server.key
    RABBITMQ_SSL_CACERTFILE: /etc/rabbitmq/certs/ca.crt
  ports:
    - "5671:5671" # AMQP over TLS
    - "15671:15671" # Management UI over HTTPS
```

**Конфигурация MassTransit** (клиент):
```csharp
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", 5671, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
            h.UseSsl(s =>
            {
                s.Protocol = SslProtocols.Tls13 | SslProtocols.Tls12;
                s.CertificatePath = "/certs/client.pfx";
                s.CertificatePassphrase = "password";
            });
        });
    });
});
```

---

### 3.3 WebSocket (для Updates Service)

**Протокол:** WSS (WebSocket Secure) поверх TLS 1.3

**Использование:**
- Updates Service использует gRPC **Server Streaming** для push-уведомлений в реальном времени
- Соединение защищено TLS (как описано в разделе 3.1)

---

## 4. End-to-End шифрование (E2EE)

### 4.1 Текущий статус

**End-to-End шифрование в настоящее время НЕ реализовано.**

Это означает, что:
- ❌ Сервер имеет **техническую возможность** доступа к содержимому сообщений
- ✅ Сообщения защищены **на уровне хранилища** (TDE, AES-256-GCM)
- ✅ Сообщения защищены **при передаче** (TLS 1.3)
- ✅ Применяется **строгий контроль доступа** (только авторизованные пользователи могут читать свои сообщения)

---

### 4.2 Планируемая реализация E2EE (Future Roadmap)

**Архитектура E2EE** для приватных чатов:

#### 4.2.1 Генерация ключей

**При регистрации:**
1. Клиент генерирует пару ключей (Public/Private) с использованием **Curve25519** (ECDH)
2. **Public Key** отправляется на сервер и хранится в `Users.PublicKeys`
3. **Private Key** хранится **только на устройстве** клиента (зашифрованный с помощью PIN-кода пользователя)

**Алгоритмы:**
- **Обмен ключами:** X25519 (Elliptic Curve Diffie-Hellman)
- **Симметричное шифрование:** AES-256-GCM
- **Подпись сообщений:** Ed25519 (для аутентификации отправителя)

#### 4.2.2 Отправка сообщения

**Процесс:**
1. Клиент A запрашивает **Public Key** пользователя B с сервера
2. Клиент A выполняет **ECDH** с Public Key пользователя B и своим Private Key → получает **Shared Secret**
3. Из Shared Secret выводится **симметричный ключ AES-256** (с использованием HKDF)
4. Клиент A шифрует сообщение с помощью AES-256-GCM
5. Клиент A отправляет:
   - `encrypted_message` — зашифрованное сообщение
   - `sender_ephemeral_public_key` — временный Public Key клиента A
   - `signature` — Ed25519 подпись сообщения
6. Сервер сохраняет зашифрованное сообщение **без возможности расшифровки**

#### 4.2.3 Получение сообщения

**Процесс:**
1. Клиент B получает зашифрованное сообщение от сервера
2. Клиент B выполняет **ECDH** с Public Key отправителя (из `sender_ephemeral_public_key`) и своим Private Key
3. Получает тот же **Shared Secret** и выводит **AES-256 ключ**
4. Расшифровывает сообщение
5. Проверяет **подпись Ed25519** для подтверждения подлинности отправителя

#### 4.2.4 Групповые чаты (Sender Keys)

Для групповых чатов используется **Sender Key Protocol** (как в Signal):
1. Каждый участник генерирует **Chain Key** (симметричный ключ)
2. При отправке сообщения:
   - Сообщение шифруется с помощью **Message Key** (производный от Chain Key)
   - Chain Key обновляется с использованием **Ratcheting** (Double Ratchet Algorithm)
3. Chain Key распространяется участникам, зашифрованный их Public Keys

#### 4.2.5 Недостатки E2EE

**Проблемы:**
- ❌ **Невозможен поиск по содержимому** на сервере (только на клиенте)
- ❌ **Сложная синхронизация** между устройствами (требуется экспорт ключей)
- ❌ **Невозможно восстановить сообщения** при потере Private Key
- ❌ **Усложнение модерации контента** (невозможно автоматически обнаружить спам/запрещенный контент)

**Решения:**
- Экспорт/импорт ключей с помощью QR-кодов или безопасных каналов
- Опциональное E2EE (пользователь выбирает для каких чатов включить)

---

## 5. Управление ключами (Key Management)

### 5.1 Хранение мастер-ключей

**Для Production:**
- Мастер-ключи хранятся в **KMS (Key Management Service)**:
  - **AWS KMS** (если развертывание на AWS)
  - **Google Cloud KMS** (если развертывание на GCP)
  - **HashiCorp Vault** (для on-premise развертываний)

**Для разработки:**
- Ключи хранятся в переменных окружения (Docker Secrets или Kubernetes Secrets)

**Конфигурация HashiCorp Vault** (пример):
```bash
# Инициализация Vault
vault secrets enable -path=barkfluff kv-v2

# Сохранение мастер-ключа
vault kv put barkfluff/database/encryption key=$(openssl rand -base64 32)

# Получение ключа (в приложении)
vault kv get -field=key barkfluff/database/encryption
```

### 5.2 Ротация ключей

**Периодичность:**
- **Мастер-ключи БД:** Каждые 90 дней
- **TLS-сертификаты:** Каждые 90 дней (автоматически с Let's Encrypt)
- **JWT Secret:** Каждые 180 дней
- **Refresh Tokens:** Автоматически при каждом обновлении Access Token

**Процесс ротации ключей БД:**
1. Генерация нового ключа в KMS
2. Перешифрование данных с использованием нового ключа (в фоновом режиме)
3. Удаление старого ключа через 30 дней (после завершения перешифрования)

---

## 6. Аутентификация и авторизация (XAuth)

### 6.1 JWT (JSON Web Tokens)

**Алгоритм подписи:** HMAC-SHA256 (симметричный) или RS256 (асимметричный для межсервисной коммуникации)

**Структура Access Token:**
```json
{
  "header": {
    "alg": "HS256",
    "typ": "JWT"
  },
  "payload": {
    "sub": "12345", // UserId
    "username": "john_doe",
    "type": "User", // TokenType.User или TokenType.Service
    "iat": 1737718800, // Issued At (timestamp)
    "exp": 1737719700  // Expiration (15 минут после iat)
  },
  "signature": "..."
}
```

**Секретный ключ:**
- Хранится в **Configuration Service**
- Длина: **256 бит** (32 символа)
- Генерация: `openssl rand -base64 32`

**Пример проверки JWT** (C#):
```csharp
var tokenHandler = new JwtSecurityTokenHandler();
var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]);

tokenHandler.ValidateToken(token, new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(key),
    ValidateIssuer = false,
    ValidateAudience = false,
    ClockSkew = TimeSpan.Zero // Точная проверка времени
}, out SecurityToken validatedToken);
```

---

### 6.2 Refresh Tokens

**Формат:** UUID v4 (случайная строка, 128 бит энтропии)

**Хранение:**
- Токены хранятся в `Identity.Sessions` в зашифрованном виде (AES-256-GCM)
- Привязаны к Device ID для защиты от кражи

**Процесс обновления:**
1. Клиент отправляет Refresh Token
2. Сервер проверяет:
   - Токен существует в БД
   - Не истек срок действия (30 дней)
   - Device ID совпадает
3. Сервер генерирует **новую пару** (Access Token + Refresh Token)
4. Старый Refresh Token **инвалидируется** (удаляется из БД)

---

## 7. Защита от атак

### 7.1 Защита от Man-in-the-Middle (MITM)

**Методы:**
- ✅ **TLS 1.3** с проверкой сертификатов (Certificate Pinning для критичных клиентов)
- ✅ **HSTS (HTTP Strict Transport Security)** — заголовок, принуждающий использовать HTTPS
- ✅ **Certificate Transparency** — логирование сертификатов для обнаружения поддельных

**Конфигурация HSTS** (в .NET):
```csharp
app.UseHsts(); // Автоматически добавляет заголовок: Strict-Transport-Security: max-age=31536000
```

---

### 7.2 Защита от Replay Attacks

**Проблема:** Злоумышленник перехватывает зашифрованное сообщение и отправляет его повторно.

**Решения:**
- ✅ **Nonce (Number Once):** Каждое сообщение содержит уникальный идентификатор
- ✅ **Timestamp:** Сообщения старше 5 минут отклоняются
- ✅ **GCM Mode:** AES-GCM автоматически защищает от повторной отправки благодаря аутентификационному тегу

---

### 7.3 Защита от Brute-Force атак

**На пароли:**
- ✅ **bcrypt** с cost factor 12 (генерация хеша занимает ~250 мс)
- ✅ **Rate Limiting:** Максимум 5 попыток входа за 15 минут (блокировка на 1 час)

**На 2FA коды:**
- ✅ **TOTP (Time-based One-Time Password):** Коды меняются каждые 30 секунд
- ✅ **Rate Limiting:** Максимум 3 неверных попытки → блокировка на 10 минут

---

## 8. Соответствие стандартам

### 8.1 Соответствие GDPR

- ✅ **Шифрование данных** (at rest и in transit)
- ✅ **Псевдонимизация:** UserId используется вместо реальных имен в логах
- ✅ **Право на удаление:** Безопасное удаление ключей шифрования → данные становятся нечитаемыми

---

### 8.2 Соответствие FIPS 140-2 (Federal Information Processing Standards)

**Алгоритмы, соответствующие FIPS 140-2:**
- ✅ AES-256 (FIPS approved)
- ✅ SHA-256, SHA-384 (FIPS approved)
- ✅ HMAC-SHA256 (FIPS approved)
- ✅ RSA-2048 (FIPS approved)

**Не соответствуют FIPS:**
- ❌ Curve25519 (не включен в FIPS, но рекомендуется NIST для E2EE)
- Альтернатива для FIPS: **NIST P-256** (secp256r1)

---

## 9. Аудит безопасности

### 9.1 Логирование криптографических операций

**Что логируется:**
- Генерация новых ключей
- Ротация ключей
- Неудачные попытки расшифровки (индикатор атаки)
- Изменения в конфигурации TLS

**Формат** (Serilog):
```json
{
  "Timestamp": "2026-01-24T12:00:00Z",
  "Level": "Information",
  "MessageTemplate": "Key rotation completed for service {ServiceId}",
  "Properties": {
    "ServiceId": "Identity",
    "OldKeyId": "key-2024-10",
    "NewKeyId": "key-2026-01"
  }
}
```

---

### 9.2 Регулярные проверки

**Рекомендации:**
- ✅ **Ежеквартальный аудит** конфигураций шифрования
- ✅ **Пентесты** (penetration testing) для проверки устойчивости к атакам
- ✅ **Сканирование уязвимостей** (Nessus, OpenVAS)
- ✅ **Проверка сертификатов** (SSL Labs, testssl.sh)

---

## 10. Контактная информация

**По вопросам безопасности:**
- Email: security@barkfluff.com
- PGP Key: [Публичный PGP-ключ для зашифрованной переписки]

**Responsible Disclosure Policy:**
Если вы обнаружили уязвимость в BarkFluff, пожалуйста, свяжитесь с нами **до публичного раскрытия**. Мы обязуемся:
- Ответить в течение **48 часов**
- Выпустить исправление в течение **30 дней**
- Публично поблагодарить исследователя (с его согласия)

---

## 11. Заключение

BarkFluff применяет современные методы шифрования для защиты ваших данных на всех уровнях:
- **Шифрование at rest:** AES-256 (TDE в PostgreSQL, SSE в Minio)
- **Шифрование in transit:** TLS 1.3 для всех соединений
- **Аутентификация:** JWT с HMAC-SHA256, bcrypt для паролей
- **Планы на будущее:** Реализация E2EE для приватных чатов

Мы постоянно улучшаем наши меры безопасности и приветствуем обратную связь от сообщества.

---

**Спасибо за использование BarkFluff!**

*Документ действителен с 24 января 2026 г.*
