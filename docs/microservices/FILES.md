# Files Microservice

## Назначение

Сервис Files отвечает за **управление файлами и медиа-контентом** в системе BarkFluff. Он управляет:

- 📤 Загрузкой файлов в S3 хранилище (Minio)
- 📥 Скачиванием файлов с проверкой доступа
- 🖼️ Автоматической генерацией превью для изображений
- 🗑️ Удалением файлов
- ✅ Валидацией файлов для других сервисов
- 🔗 Генерацией presigned URLs для загрузки

**Порт**: 7005
**База данных**: PostgreSQL (`files_db`)
**Хранилище**: Minio (S3-совместимый)
**Зависимости**: Configuration service

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Entity Framework Core**: ORM
- **PostgreSQL**: База данных метаданных
- **Minio**: S3-совместимое объектное хранилище
- **SixLabors.ImageSharp**: Обработка изображений
- **HTTP/REST**: Эндпоинты для загрузки/скачивания

## Архитектура

```
┌─────────────────────────────────────────────┐
│              Files Service                   │
├─────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌───────────┐ │
│  │ gRPC API │  │ HTTP API │  │  Storage  │ │
│  └────┬─────┘  └────┬─────┘  └─────┬─────┘ │
│       │             │               │       │
│       └─────────────┴───────────────┘       │
│                     ↓                       │
│            ┌─────────────────┐              │
│            │  S3Uploader     │              │
│            └────────┬────────┘              │
└─────────────────────┼───────────────────────┘
                      │
                      ↓
              ┌──────────────┐
              │    Minio     │
              │ (S3 Storage) │
              └──────────────┘
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **UploadedFiles** | Метаданные загруженных файлов |
| **TempFiles** | Временные файлы для двухфазной загрузки |

### Основные сущности

#### UploadedFile
```csharp
public class UploadedFile
{
    public Guid Id { get; set; }
    public FileType Type { get; set; }           // UserAvatar, MessageAttachment*
    public string FileName { get; set; }          // Оригинальное имя
    public long Size { get; set; }                // Размер в байтах
    public DateTime CreatedAt { get; set; }
    public long UserId { get; set; }              // Владелец файла
    public string? Etag { get; set; }             // Etag из Minio (null до загрузки)
    public Guid? PreviewId { get; set; }          // ID превью (для изображений)
}
```

**Важно**: `Etag == null` означает, что файл создан, но ещё не загружен.

#### TempFile
```csharp
public class TempFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public DateTime CreatedAt { get; set; }
    public long UserId { get; set; }
}
```

**Назначение**: Хранение временных файлов перед их привязкой к сущностям.

## Типы файлов

### FileType Enum

| Тип | Описание | Bucket | Превью |
|-----|----------|--------|--------|
| **UserAvatar** | Аватар пользователя | `user-avatars` | ✅ Да (1024px) |
| **MessageAttachmentImage** | Изображение в сообщении | `message-attachments` | ✅ Да (1024px) |
| **MessageAttachmentVideo** | Видео в сообщении | `message-attachments` | ❌ Нет |
| **MessageAttachmentGif** | GIF в сообщении | `message-attachments` | ❌ Нет |
| **MessageAttachmentDocument** | Документ в сообщении | `message-attachments` | ❌ Нет |

### Маппинг типов на Buckets

**Логика** (FilesApiService.cs:143):
```csharp
private string GetBucketName(FileType fileType)
{
    return fileType switch
    {
        FileType.UserAvatar => "user-avatars",
        FileType.MessageAttachmentImage => "message-attachments",
        FileType.MessageAttachmentVideo => "message-attachments",
        FileType.MessageAttachmentGif => "message-attachments",
        FileType.MessageAttachmentDocument => "message-attachments",
        _ => throw new InvalidOperationException($"Unknown file type: {fileType}")
    };
}
```

## Ключевые функции

### 1. Двухфазная загрузка файлов

**Процесс**:
```
Phase 1: Получение URL для загрузки
1. Client → GetUploadUrl(type=MessageAttachmentImage)
2. Files → Создание UploadedFile (Etag=null)
3. Files → Сохранение в PostgreSQL
4. Client ← { url: "http://files:7005/upload/{fileId}", fileId }

Phase 2: Фактическая загрузка
5. Client → HTTP POST /upload/{fileId} (multipart/form-data)
6. Files → S3Uploader.Upload(bucket, fileId, stream)
7. Files → Генерация превью (для изображений)
8. Files → Обновление UploadedFile (Etag, PreviewId, Size)
9. Client ← Success
```

**gRPC Method**: `GetUploadUrl`

**Request**:
```protobuf
message GetUploadUrlRequest {
  FileType type = 1;
}
```

**Response**:
```protobuf
message GetUploadUrlResponse {
  string url = 1;           // http://files:7005/upload/uuid
  string file_id = 2;       // uuid
}
```

**Реализация** (Features/GetUploadUrl/GetUploadUrlCommandHandler.cs):
```csharp
var uploadedFile = new UploadedFile
{
    Id = Guid.NewGuid(),
    Type = request.Type,
    UserId = userContext.UserId,
    CreatedAt = DateTime.UtcNow,
    Etag = null  // Будет заполнено при загрузке
};

await _storage.UploadedFiles.AddAsync(uploadedFile);

var uploadUrl = $"http://{host}:{port}/upload/{uploadedFile.Id}";

return new GetUploadUrlResponse
{
    Url = uploadUrl,
    FileId = uploadedFile.Id.ToString()
};
```

### 2. HTTP Upload Endpoint

**Endpoint**: `POST /upload/{fileId}`

**Content-Type**: `multipart/form-data`

**Process** (Host/FilesApiService.cs:93):
```csharp
[HttpPost("/upload/{fileId}")]
public async Task<IActionResult> UploadFile(Guid fileId, IFormFile file)
{
    // 1. Получение метаданных из БД
    var uploadedFile = await _storage.UploadedFiles
        .FirstOrDefaultAsync(f => f.Id == fileId);

    if (uploadedFile == null)
        return NotFound("File not found");

    // 2. Определение bucket
    var bucketName = GetBucketName(uploadedFile.Type);

    // 3. Загрузка в Minio
    var etag = await _s3Uploader.UploadAsync(
        bucketName,
        fileId.ToString(),
        file.OpenReadStream()
    );

    // 4. Генерация превью (для изображений)
    Guid? previewId = null;
    if (ShouldGeneratePreview(uploadedFile.Type))
    {
        previewId = await GeneratePreviewAsync(
            file.OpenReadStream(),
            bucketName
        );
    }

    // 5. Обновление метаданных
    uploadedFile.Etag = etag;
    uploadedFile.PreviewId = previewId;
    uploadedFile.Size = file.Length;
    uploadedFile.FileName = file.FileName;

    await _storage.SaveChangesAsync();

    return Ok();
}
```

### 3. Генерация превью для изображений

**Логика** (FilesApiService.cs:167):
```csharp
private async Task<Guid> GeneratePreviewAsync(
    Stream imageStream,
    string bucketName)
{
    using var image = await Image.LoadAsync(imageStream);

    // Ограничение максимального размера: 1024px
    const int maxSize = 1024;

    if (image.Width > maxSize || image.Height > maxSize)
    {
        var ratio = Math.Min(
            (double)maxSize / image.Width,
            (double)maxSize / image.Height
        );

        var newWidth = (int)(image.Width * ratio);
        var newHeight = (int)(image.Height * ratio);

        image.Mutate(x => x.Resize(newWidth, newHeight));
    }

    // Сохранение превью в Minio
    var previewId = Guid.NewGuid();
    using var previewStream = new MemoryStream();

    await image.SaveAsJpegAsync(previewStream);
    previewStream.Position = 0;

    await _s3Uploader.UploadAsync(
        bucketName,
        $"{previewId}_preview",
        previewStream
    );

    return previewId;
}
```

**Максимальный размер превью**: 1024px (с сохранением пропорций)
**Формат превью**: JPEG

### 4. Скачивание файлов

**gRPC Method**: `GetFile`

**Request**:
```protobuf
message GetFileRequest {
  string file_id = 1;
  bool preview = 2;     // Скачать превью вместо оригинала
}
```

**Response**: HTTP redirect на Minio presigned URL

**Реализация** (Features/GetFile/GetFileQueryHandler.cs):
```csharp
var file = await _storage.UploadedFiles
    .FirstOrDefaultAsync(f => f.Id == fileId);

if (file == null)
    throw new RpcException(new Status(
        StatusCode.NotFound,
        "File not found"
    ));

var bucketName = GetBucketName(file.Type);
var objectName = preview && file.PreviewId.HasValue
    ? $"{file.PreviewId}_preview"
    : file.Id.ToString();

// Генерация presigned URL (действителен 1 час)
var presignedUrl = await _s3Client.PresignedGetObjectAsync(
    bucketName,
    objectName,
    expiresIn: 3600
);

return new GetFileResponse
{
    Url = presignedUrl
};
```

### 5. Валидация файлов для других сервисов

**Service-to-Service Methods**:

#### GetFileData
Проверка существования и получение метаданных одного файла.

**Request**:
```protobuf
message GetFileDataRequest {
  string file_id = 1;
}
```

**Response**:
```protobuf
message FileDataResponse {
  string file_id = 1;
  FileType type = 2;
  int64 size = 3;
  string file_name = 4;
}
```

**Использование**: Users проверяет, что загруженный файл - аватар.

#### GetFilesData
Массовая проверка нескольких файлов.

**Request**:
```protobuf
message GetFilesDataRequest {
  repeated string file_ids = 1;
}
```

**Response**:
```protobuf
message GetFilesDataResponse {
  repeated FileDataResponse files = 1;
}
```

**Использование**: Messages проверяет вложения перед отправкой сообщения.

**Реализация** (Features/GetFilesData/GetFilesDataQueryHandler.cs):
```csharp
var fileIds = request.FileIds
    .Select(id => Guid.Parse(id))
    .ToList();

var files = await _storage.UploadedFiles
    .Where(f => fileIds.Contains(f.Id))
    .Where(f => f.Etag != null)  // Только загруженные файлы
    .ToListAsync();

// Проверка, что все файлы найдены
if (files.Count != fileIds.Count)
{
    var missingIds = fileIds
        .Except(files.Select(f => f.Id))
        .ToList();

    throw new RpcException(new Status(
        StatusCode.NotFound,
        $"Files not found: {string.Join(", ", missingIds)}"
    ));
}

return new GetFilesDataResponse
{
    Files = files.Select(f => new FileDataResponse
    {
        FileId = f.Id.ToString(),
        Type = f.Type,
        Size = f.Size,
        FileName = f.FileName
    }).ToList()
};
```

### 6. Удаление файлов

**gRPC Method**: `DeleteFile`

**Request**:
```protobuf
message DeleteFileRequest {
  string file_id = 1;
}
```

**Процесс** (Features/DeleteFile/DeleteFileCommandHandler.cs):
```csharp
var file = await _storage.UploadedFiles
    .FirstOrDefaultAsync(f => f.Id == fileId);

if (file == null)
    throw new RpcException(new Status(
        StatusCode.NotFound,
        "File not found"
    ));

// Проверка владельца
if (file.UserId != userContext.UserId)
    throw new RpcException(new Status(
        StatusCode.PermissionDenied,
        "You are not the owner of this file"
    ));

var bucketName = GetBucketName(file.Type);

// Удаление из Minio
await _s3Client.RemoveObjectAsync(bucketName, file.Id.ToString());

// Удаление превью (если есть)
if (file.PreviewId.HasValue)
{
    await _s3Client.RemoveObjectAsync(
        bucketName,
        $"{file.PreviewId}_preview"
    );
}

// Удаление из БД
_storage.UploadedFiles.Remove(file);
await _storage.SaveChangesAsync();
```

## Взаимодействие с Minio

### S3Uploader Service

**Интерфейс** (Infrastructure/S3Uploader.cs):
```csharp
public interface IS3Uploader
{
    Task<string> UploadAsync(string bucket, string objectName, Stream stream);
    Task<string> PresignedGetObjectAsync(string bucket, string objectName, int expiresIn);
    Task RemoveObjectAsync(string bucket, string objectName);
}
```

**Конфигурация**:
```csharp
var minioClient = new MinioClient()
    .WithEndpoint(config["Minio:Endpoint"])
    .WithCredentials(
        config["Minio:AccessKey"],
        config["Minio:SecretKey"]
    )
    .Build();
```

### Buckets

При старте сервиса создаются необходимые buckets:

```csharp
public async Task EnsureBucketsExistAsync()
{
    var buckets = new[] { "user-avatars", "message-attachments" };

    foreach (var bucket in buckets)
    {
        var exists = await _minioClient.BucketExistsAsync(bucket);

        if (!exists)
        {
            await _minioClient.MakeBucketAsync(bucket);
            _logger.LogInformation("Created bucket: {Bucket}", bucket);
        }
    }
}
```

## Зависимости

### Configuration Service (gRPC)

**Методы**:
- `LoadConfiguration` - загрузка настроек при старте

**Настройки**:
```json
{
  "Minio": {
    "Endpoint": "minio:9000",
    "AccessKey": "admin",
    "SecretKey": "password"
  },
  "Server": {
    "Host": "0.0.0.0",
    "Port": 7005
  }
}
```

## API Reference

### gRPC Methods (FilesApi)

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `GetUploadUrl` | ✅ User | Получение URL для загрузки файла |
| `GetFile` | ✅ User | Скачивание файла (redirect на Minio) |
| `DeleteFile` | ✅ User | Удаление файла |

### gRPC Methods (FilesServerApi)

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `GetFileData` | ✅ Service | Получение метаданных файла |
| `GetFilesData` | ✅ Service | Массовое получение метаданных |

### HTTP Endpoints

| Endpoint | Method | Описание |
|----------|--------|----------|
| `/upload/{fileId}` | POST | Загрузка файла (multipart/form-data) |
| `/download/{fileId}` | GET | Скачивание файла (альтернативный метод) |

## Конфигурация

### appsettings.json

```json
{
  "Minio": {
    "Endpoint": "minio:9000",
    "AccessKey": "admin",
    "SecretKey": "password",
    "UseSSL": false
  },
  "FilesDb": "Host=postgres;Database=files_db;Username=postgres;Password=postgres",
  "JwtSettings": {
    "SecretKey": "your-secret-key",
    "Issuer": "BarkFluff.Identity",
    "Audience": "BarkFluff"
  }
}
```

### Переменные окружения

- `FilesDb` - строка подключения PostgreSQL
- `Minio:Endpoint` - адрес Minio сервера
- `Minio:AccessKey` - access key для Minio
- `Minio:SecretKey` - secret key для Minio

## Ограничения и валидация

### Размер файлов

**Максимальный размер**: Настраивается через Kestrel limits

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
});
```

### Типы файлов

**Валидация**: В текущей реализации отсутствует проверка MIME-типов.

**Рекомендация**: Добавить валидацию:
```csharp
var allowedTypes = new Dictionary<FileType, string[]>
{
    [FileType.UserAvatar] = new[] { "image/jpeg", "image/png", "image/webp" },
    [FileType.MessageAttachmentImage] = new[] { "image/jpeg", "image/png", "image/webp" },
    [FileType.MessageAttachmentVideo] = new[] { "video/mp4", "video/webm" },
    [FileType.MessageAttachmentGif] = new[] { "image/gif" },
    [FileType.MessageAttachmentDocument] = new[] { "application/pdf", "text/plain" }
};
```

## Известные проблемы

### 🟡 Средние

1. **Отсутствие проверки MIME-типов**
   - Пользователи могут загружать любые файлы
   - **Рекомендация**: Добавить валидацию Content-Type

2. **Нет ограничения на количество файлов**
   - Пользователь может загрузить неограниченное количество файлов
   - **Рекомендация**: Добавить квоту на пользователя

3. **Превью генерируется синхронно**
   - Блокирует HTTP запрос
   - **Рекомендация**: Генерация через background job

### 🟢 Низкие

4. **Нет автоматической очистки неиспользуемых файлов**
   - Файлы с `Etag == null` накапливаются
   - **Рекомендация**: Background job для очистки старых TempFiles

5. **Presigned URLs действительны 1 час**
   - Может быть недостаточно для медленных соединений
   - **Рекомендация**: Сделать настраиваемым

## Troubleshooting

### Проблема: "File not found" при загрузке

**Причина**: Вызван GetUploadUrl, но истекло время жизни записи или она была удалена.

**Решение**:
1. Проверить, что `fileId` существует в БД
2. Убедиться, что `Etag == null` (файл ещё не загружен)

### Проблема: "Bucket not found" в Minio

**Причина**: Bucket не был создан при старте сервиса.

**Решение**:
```bash
# Войти в Minio console
mc alias set local http://minio:9000 admin password

# Создать bucket вручную
mc mb local/user-avatars
mc mb local/message-attachments
```

### Проблема: Превью не генерируется

**Причина**: Файл не является изображением или повреждён.

**Решение**:
1. Проверить формат файла (JPEG, PNG, WEBP)
2. Убедиться, что ImageSharp поддерживает формат
3. Проверить логи на ошибки декодирования

## Метрики и мониторинг

### Ключевые метрики

- **Uploads per minute**
- **Average upload size**
- **Preview generation time**
- **Minio storage usage**
- **Failed uploads ratio**

### Логи

Все операции логируются:
- Успешные/неуспешные загрузки
- Генерация превью
- Ошибки взаимодействия с Minio
- Валидация файлов от других сервисов

## Примеры использования

### Пример 1: Загрузка аватара

```csharp
// 1. Получение upload URL
var uploadUrlResponse = await filesApi.GetUploadUrlAsync(new GetUploadUrlRequest
{
    Type = FileType.UserAvatar
});

// 2. Загрузка файла
using var fileStream = File.OpenRead("avatar.jpg");
using var content = new MultipartFormDataContent();
content.Add(new StreamContent(fileStream), "file", "avatar.jpg");

var httpClient = new HttpClient();
var uploadResponse = await httpClient.PostAsync(
    uploadUrlResponse.Url,
    content
);

// 3. Использование fileId в Users.UpdateUser
await usersApi.UpdateUserAsync(new UpdateUserRequest
{
    AvatarFileId = uploadUrlResponse.FileId
});
```

### Пример 2: Отправка сообщения с изображением

```csharp
// 1. Загрузка изображения
var uploadUrlResponse = await filesApi.GetUploadUrlAsync(new GetUploadUrlRequest
{
    Type = FileType.MessageAttachmentImage
});

using var imageStream = File.OpenRead("photo.jpg");
await UploadFileAsync(uploadUrlResponse.Url, imageStream);

// 2. Отправка сообщения с вложением
await messagesApi.SendMessageAsync(new SendMessageRequest
{
    ChatId = "chat-uuid",
    Text = "Посмотри на это фото!",
    AttachmentFileIds = { uploadUrlResponse.FileId }
});
```

### Пример 3: Валидация файлов (service-to-service)

```csharp
// Messages service проверяет вложения
var filesData = await filesServerApi.GetFilesDataAsync(new GetFilesDataRequest
{
    FileIds = { fileId1, fileId2, fileId3 }
});

foreach (var file in filesData.Files)
{
    if (file.Type != FileType.MessageAttachmentImage &&
        file.Type != FileType.MessageAttachmentVideo)
    {
        throw new InvalidOperationException(
            $"Invalid file type for message: {file.Type}"
        );
    }
}
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Files/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/FilesApiService.cs` - gRPC + HTTP endpoints
- `Features/*/` - CQRS handlers
- `Infrastructure/S3Uploader.cs` - интеграция с Minio
- `Persistence/FilesDbContext.cs` - EF Core контекст
- `Persistence/Storage/FilesStorage.cs` - репозитории
