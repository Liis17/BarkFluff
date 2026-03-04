# Аудит Безопасности: BarkFluff.Files

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team  
**Статус:** 🔴 Критические уязвимости обнаружены

---

## Резюме

Сервис BarkFluff.Files содержит **15 уязвимостей**, включая **4 критические**, **6 высоких**, **5 средних**. Сервис требует немедленного исправления перед развертыванием в продакшен.

---

## Критические уязвимости (Critical)

### 1. Отсутствие валидации типа файла (MIME-type sniffing)
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/UploadFile/UploadFileCommandHandler.cs` |
| **Метод** | `Handle(UploadFileCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-434: Unrestricted Upload of File with Dangerous Type |

**Описание проблемы:**
```csharp
// Тип контента определяется только по расширению файла
var contentType = request.FileName.GetContentType();
// Нет проверки фактического содержимого файла (magic bytes)
```

**Как эксплуатировать:**
1. Злоумышленник может загрузить executable файл с расширением `.jpg`
2. Возможна загрузка вредоносных скриптов (PHP, ASPX) с изображением расширений
3. Обход ограничений на загрузку определенных типов файлов

**Пример эксплуатации:**
```
# Переименовать malware.exe в malware.jpg
upload(filename="malware.jpg", content=<exe_bytes>)

# Или использовать polyglot файл
upload(filename="image.jpg", content=<jpg_header + malicious_payload>)
```

**Рекомендации по исправлению:**
```csharp
private static readonly Dictionary<string, byte[]> AllowedMagicBytes = new()
{
    { "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF } },
    { "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } },
    { "image/gif", new byte[] { 0x47, 0x49, 0x46 } },
    { "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 } }
};

private bool ValidateFileContent(Stream stream, string expectedContentType)
{
    var buffer = new byte[16];
    stream.Read(buffer, 0, buffer.Length);
    stream.Position = 0;
    
    if (AllowedMagicBytes.TryGetValue(expectedContentType, out var magicBytes))
    {
        return buffer.Take(magicBytes.Length).SequenceEqual(magicBytes);
    }
    return false;
}
```

---

### 2. Несанкционированный доступ к файлам (IDOR)
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/DownloadFile/DownloadFileCommandHandler.cs` |
| **Метод** | `Handle(DownloadFileCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-639: Authorization Bypass Through User-Controlled Key |

**Описание проблемы:**
```csharp
// Незя получать файлы по их оригинальным ID кроме аватарок и картинок чата
if (file is { Type: not (UploadFileType.UserAvatar or UploadFileType.ChatPicture) })
{
    throw new Exception("Файл не найден");
}
// Любой пользователь может скачать любую аватарку или картинку чата
// Нет проверки принадлежности файла пользователю
```

**Как эксплуатировать:**
1. Перебор `fileId` для доступа к чужим аватаркам
2. Использование `GetTempDownloadUrl` для получения доступа к файлам других пользователей
3. Доступ к приватным файлам через прямой вызов API

**Рекомендации по исправлению:**
```csharp
public async Task<DownloadFileResult> Handle(DownloadFileCommand request, CancellationToken cancellationToken)
{
    var file = await _filesStorage.GetFile(request.FileId);
    
    // Проверка прав доступа
    if (file == null || !file.Uploaders.Contains(_userContext.UserId))
    {
        // Для публичных типов файлов (аватарки) разрешить доступ
        if (file?.Type != UploadFileType.UserAvatar)
        {
            _logger.LogWarning("Попытка доступа к файлу {FileId} без прав", request.FileId);
            throw new UnauthorizedAccessException("Доступ запрещён");
        }
    }
    
    // ... остальной код
}
```

---

### 3. Публичный доступ к S3 бакетам
| Параметр | Значение |
|----------|----------|
| **Файл** | `Infrastructure/S3BucketInitializer.cs` |
| **Метод** | `SetBucketPolicyAsync(string bucketName)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-284: Improper Access Control |

**Описание проблемы:**
```json
{
    "Effect": "Allow",
    "Principal": { "AWS": "*" },
    "Action": "s3:GetObject",
    "Resource": "arn:aws:s3:::{{bucketName}}/*"
}
```

**Как эксплуатировать:**
1. Любой, кто знает URL файла, может скачать его напрямую из S3
2. Обход авторизации в приложении
3. Прямой доступ к файлам без проверки прав

**Рекомендации по исправлению:**
```csharp
// Убрать публичную политику
// Использовать presigned URL с ограниченным временем жизни
public async Task<string> GeneratePresignedUrl(string bucketName, string key, TimeSpan expiration)
{
    var request = new GetPreSignedUrlRequest
    {
        BucketName = bucketName,
        Key = key,
        Expires = DateTime.UtcNow.Add(expiration)
    };
    
    return await _minioClient.GetPreSignedURLAsync(request);
}
```

---

### 4. XSS через имя файла
| Параметр | Значение |
|----------|----------|
| **Файл** | `Domain/UploadFile.cs` |
| **Метод** | Свойство `Filename` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-79: XSS |

**Описание проблемы:**
- Имя файла сохраняется в БД без санитизации
- Возвращается через gRPC API в `UploadFileInfo.FileName`
- При отображении в веб-интерфейсе возможна XSS

**Как эксплуатировать:**
```
# Загрузка файла с XSS payload
upload(filename="<script>alert('XSS')</script>.jpg")

# При отображении имени файла в UI скрипт выполнится
```

**Рекомендации по исправлению:**
```csharp
private string SanitizeFileName(string fileName)
{
    // Удаляем path traversal символы
    fileName = Path.GetFileName(fileName);
    
    // HTML-encoding для защиты от XSS
    fileName = System.Net.WebUtility.HtmlEncode(fileName);
    
    // Заменяем опасные символы
    fileName = fileName.Replace("\0", "")
                       .Replace("..", "")
                       .Replace("/", "")
                       .Replace("\\", "");
    
    // Ограничиваем длину
    if (fileName.Length > 255)
        fileName = fileName[..255];
    
    return fileName;
}
```

---

## Высокие уязвимости (High)

### 5. Path Traversal через имя файла
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/UploadFile/UploadFileCommandHandler.cs` |
| **Метод** | `Handle(UploadFileCommand request, ...)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-22: Improper Limitation of a Pathname to a Restricted Directory |

**Описание проблемы:**
- Имя файла `request.FileName` используется напрямую без санитизации
- Хотя файл сохраняется в S3 с ключом `{file.Id}`, имя файла сохраняется в БД

**Как эксплуатировать:**
```
filename: "../../../etc/passwd.jpg"
filename: "test\0.jpg" (null byte injection)
```

**Рекомендации по исправлению:**
- Санитизация имени файла через `Path.GetFileName()`
- Валидация на наличие специальных символов

---

### 6. Отсутствие Rate Limiting на загрузку файлов
| Параметр | Значение |
|----------|----------|
| **Файл** | `Host/FilesController.cs` |
| **Метод** | `UploadFile()` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-770: Allocation of Resources Without Limits |

**Как эксплуатировать:**
1. Злоумышленник может отправить тысячи запросов на загрузку
2. Быстрое исчерпание квоты хранилища
3. DoS атака на сервис

**Рекомендации по исправлению:**
```csharp
// Добавить в Program.cs
app.UseRateLimiter(new RateLimiterOptions
{
    FixedWindow = new FixedWindowRateLimiterOptions
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1)
    }
});
```

---

### 7. SSRF через S3 Bucket Configuration
| Параметр | Значение |
|----------|----------|
| **Файл** | `Infrastructure/S3BucketRegistry.cs` |
| **Метод** | `GetBucketConfig(string bucketName)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-918: Server-Side Request Forgery (SSRF) |

**Описание проблемы:**
- S3 конфигурация загружается из `appsettings.json`/переменных окружения
- Если злоумышленник может контролировать конфигурацию, он может указать внутренний URL

**Как эксплуатировать:**
```
ServiceUrl: "http://localhost:8080"
ServiceUrl: "http://169.254.169.254/latest/meta-data/" (AWS metadata)
```

**Рекомендации по исправлению:**
- Валидировать `ServiceUrl` на уровне конфигурации
- Блокировать доступ к localhost и приватным IP
- Использовать whitelist разрешённых хостов

---

### 8. Утечка через временные ссылки
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/TempFilesStorage.cs` |
| **Метод** | `CreateTempFile(Guid fileId)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-200: Information Exposure |

**Описание проблемы:**
```csharp
public async Task<TempFile> CreateTempFile(Guid fileId)
{
    var file = new TempFile()
    {
        OriginalFileId = fileId,
        ExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(int.Parse(_configuration["TempFiles:ExpiresAt"]))
    };
    // Нет привязки к пользователю!
}
```

**Как эксплуатировать:**
1. Любой, кто получил `tempFileId`, может скачать файл
2. Нет проверки, что запросивший пользователь имеет права на файл
3. Перебор `tempFileId` для доступа к файлам

**Рекомендации по исправлению:**
```csharp
public async Task<TempFile> CreateTempFile(Guid fileId, long userId)
{
    var file = new TempFile()
    {
        OriginalFileId = fileId,
        CreatedByUserId = userId, // Добавить поле
        ExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(30) // Фиксированное время
    };
}

public async Task<TempFile?> GetTempFile(Guid tempFileId, long userId)
{
    return await _context.TempFiles
        .Where(x => x.ExpiresAt > DateTime.UtcNow && x.CreatedByUserId == userId)
        .FirstOrDefaultAsync(x => x.Id == tempFileId);
}
```

---

### 9. Недостаточная валидация изображений (Image Trick)
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/ImageCompressor.cs` |
| **Метод** | `CompressImageAsync(Stream inputStream, int width)` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-434: Unrestricted Upload of File with Dangerous Type |

**Описание проблемы:**
- ImageCompressor использует SixLabors.ImageSharp для обработки изображений
- Нет проверки на полиглотные файлы
- Нет проверки на SVG с внедрённым JavaScript

**Как эксплуатировать:**
```
# Загрузка SVG с <script> тегами
upload(filename="image.svg", content="<svg><script>alert(1)</script></svg>")

# Polyglot файлы (JPEG + executable)
```

**Рекомендации по исправлению:**
```csharp
public async Task<byte[]> CompressImageAsync(Stream inputStream, int width = 1024)
{
    // Проверка на SVG
    var buffer = new byte[512];
    await inputStream.ReadAsync(buffer);
    inputStream.Position = 0;
    
    var contentStart = Encoding.ASCII.GetString(buffer.Take(100).ToArray());
    if (contentStart.Contains("<svg", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("SVG файлы не поддерживаются");
    }
    
    // Использовать безопасные настройки
    var configuration = new Configuration { IgnoreMetadata = true };
    using var image = await Image.LoadAsync(configuration, inputStream);
    
    // ... остальной код
}
```

---

### 10. Отсутствие валидации размера файла
| Параметр | Значение |
|----------|----------|
| **Файл** | `Host/FilesController.cs` |
| **Метод** | `UploadFile()` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-770: Allocation of Resources Without Limits |

**Описание проблемы:**
- Лимит 500MB применяется на уровне ASP.NET
- Нет валидации в бизнес-логике для разных типов файлов

**Рекомендации по исправлению:**
```csharp
private readonly Dictionary<UploadFileType, long> _maxFileSizeByType = new()
{
    { UploadFileType.UserAvatar, 5 * 1024 * 1024 }, // 5MB
    { UploadFileType.MessageAttachmentImage, 10 * 1024 * 1024 }, // 10MB
    { UploadFileType.MessageAttachmentDocument, 50 * 1024 * 1024 }, // 50MB
    { UploadFileType.MessageAttachmentVideo, 100 * 1024 * 1024 } // 100MB
};
```

---

## Средние уязвимости (Medium)

### 11. Манипуляции с file_id через предсказуемые GUID
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/GetUploadUrl/GetUploadUrlCommandHandler.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-330: Use of Insufficiently Random Values |

**Рекомендации:**
- Использовать `RandomNumberGenerator.GetBytes(16)` для генерации криптографически стойких ID

---

### 12. Отсутствие аудита и логирования безопасности
| Параметр | Значение |
|----------|----------|
| **Файл** | Все обработчики команд |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-778: Insufficient Logging |

**Рекомендации:**
- Добавить логирование всех неудачных попыток доступа
- Настроить алерты при аномальной активности

---

### 13. Уязвимость в проверке хеша файла
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/CheckFileHash/CheckFileHashCommandHandler.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-639: Authorization Bypass |

**Описание проблемы:**
```csharp
await _filesStorage.AddUploaderToFile(fileId.Value, _userContext.UserId);
// Пользователь может добавить себя в загрузчики любого файла с известным хешем
```

**Рекомендации:**
- Проверять права доступа перед добавлением в загрузчики

---

### 14. Отсутствие Content-Disposition заголовка
| Параметр | Значение |
|----------|----------|
| **Файл** | `Host/FilesController.cs` |
| **Метод** | `DownloadFile()` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-693: Protection Mechanism Failure |

**Рекомендации по исправлению:**
```csharp
var cd = new ContentDisposition
{
    FileName = result.FileName,
    Inline = false // Всегда скачивать, не выполнять
};
Response.Headers.Add("Content-Disposition", cd.ToString());
Response.Headers.Add("X-Content-Type-Options", "nosniff");
```

---

### 15. Потенциальный SQL Injection через LINQ
| Параметр | Значение |
|----------|----------|
| **Файл** | `Persistence/UploadedFilesStorage.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-89: SQL Injection |

**Рекомендации:**
- Проверять сгенерированный SQL через логирование EF Core

---

## Сводная таблица уязвимостей

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | Отсутствие валидации MIME-type | 🔴 Critical | UploadFileCommandHandler.cs |
| 2 | IDOR - несанкционированный доступ | 🔴 Critical | DownloadFileCommandHandler.cs |
| 3 | Публичный S3 bucket | 🔴 Critical | S3BucketInitializer.cs |
| 4 | XSS через имя файла | 🔴 Critical | UploadFile.cs |
| 5 | Path Traversal | 🟠 High | UploadFileCommandHandler.cs |
| 6 | Отсутствие Rate Limiting | 🟠 High | FilesController.cs |
| 7 | SSRF через S3 | 🟠 High | S3BucketRegistry.cs |
| 8 | Утечка временных ссылок | 🟠 High | TempFilesStorage.cs |
| 9 | Image Trick | 🟠 High | ImageCompressor.cs |
| 10 | Нет валидации размера | 🟠 High | FilesController.cs |
| 11 | Предсказуемые GUID | 🟡 Medium | GetUploadUrlCommandHandler.cs |
| 12 | Отсутствие аудита | 🟡 Medium | Все файлы |
| 13 | Манипуляции с хешем | 🟡 Medium | CheckFileHashCommandHandler.cs |
| 14 | Content-Disposition | 🟡 Medium | FilesController.cs |
| 15 | Потенциальный SQLi | 🟡 Medium | UploadedFilesStorage.cs |

---

## Приоритетные рекомендации по исправлению

### Немедленно (Critical):
1. ✅ Добавить валидацию MIME-type по magic bytes
2. ✅ Исправить IDOR в DownloadFileCommandHandler
3. ✅ Убрать публичную политику S3, использовать presigned URLs
4. ✅ Добавить санитизацию имён файлов

### Высокий приоритет:
5. ✅ Добавить проверку прав доступа к временным ссылкам
6. ✅ Добавить лимиты размера файлов по типам
7. ✅ Добавить Rate Limiting
8. ✅ Добавить Content-Disposition: attachment

### Средний приоритет:
9. Добавить аудит безопасности
10. Использовать криптографически стойкие GUID
11. Проверять SQL запросы

---

## Статус Исправления

| Уязвимость | Статус | Дата Исправления | Примечания |
|------------|--------|------------------|------------|
| 1. Валидация MIME-type | ⏳ Ожидает | - | Требуется реализация |
| 2. IDOR | ⏳ Ожидает | - | - |
| 3. Публичный S3 | ⏳ Ожидает | - | Требуется presigned URL |
| 4. XSS | ⏳ Ожидает | - | - |
| 5. Path Traversal | ⏳ Ожидает | - | - |
| 6. Rate Limiting | ⏳ Ожидает | - | Требуется middleware |
| 7. SSRF | ⏳ Ожидает | - | - |
| 8. Временные ссылки | ⏳ Ожидает | - | - |
| 9. Image Trick | ⏳ Ожидает | - | - |
| 10. Размер файла | ⏳ Ожидает | - | - |

---

## Контакты

По вопросам безопасности обращайтесь: security@barkfluff.com
