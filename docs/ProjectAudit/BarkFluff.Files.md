# Аудит проекта: BarkFluff.Files

> **Дата:** 2026-07  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)  
> **Сервис:** `Backend/BarkFluff.Files` — файловый сервис (S3/Minio, PostgreSQL, gRPC + REST)  
> **Статус:** 

---

## 🟡 Оптимизация

---

### OPT-01 — `S3Uploader.DownloadAsync` загружает весь файл в `MemoryStream`

#### Описание

При скачивании файла из S3 весь контент копируется в `MemoryStream` перед отдачей клиенту. Для больших файлов (видео, документы до 512 МБ) это означает полное потребление RAM на каждый запрос.

#### В чём проблема

Отсутствует потоковая передача (streaming) напрямую из S3 в HTTP-ответ. При нескольких одновременных запросах сервис может исчерпать память.

**Путь:** `Backend/BarkFluff.Files/Infrastructure/S3Uploader.cs` : строки 32–49

```csharp
// ❌ Весь файл копируется в RAM
public async Task<Stream> DownloadAsync(string bucket, string key)
{
    var response = await client.GetObjectAsync(request);

    var memoryStream = new MemoryStream();
    await response.ResponseStream.CopyToAsync(memoryStream); // ← буферизация
    memoryStream.Position = 0;

    return memoryStream; // Возвращаем копию, оригинальный стрим закрыт
}
```

#### Варианты решения

Возвращать `response.ResponseStream` напрямую, передав ответственность за `response` через обёртку или `IAsyncDisposable`.

```csharp
// ✅ Потоковая передача без буферизации в RAM
public async Task<(Stream Stream, GetObjectResponse Response)> DownloadStreamAsync(string bucket, string key)
{
    var client = _registry.GetClientForBucket(bucket);
    var response = await client.GetObjectAsync(new GetObjectRequest
    {
        BucketName = bucket,
        Key = key
    });
    // Вызывающий код должен задиспозить response после завершения стриминга
    return (response.ResponseStream, response);
}

// В FilesController:
var (stream, s3Response) = await _s3Uploader.DownloadStreamAsync(bucketName, key);
using (s3Response) // Закрываем S3-ответ после отправки
{
    return File(stream, contentType, fileName);
}
//необходимо проверить правильноть реализации
```

---

### 

### OPT-03 — `ImageCompressor` методы не переиспользуют декодированное изображение

#### Описание

В `UploadFileCommandHandler` при обработке изображений `Image.LoadAsync` вызывается несколько раз на один и тот же поток:

1. `EnforceOriginalLimitsAsync` — декодирование для сжатия оригинала
2. `Image.IdentifyAsync` — определение размеров (отдельная операция)
3. `CompressImageAsync` — декодирование для превью

Каждый раз изображение декодируется заново из байт — дублирование CPU-работы.

**Путь:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs` : строки 235–321

```csharp
// ❌ Три отдельных декодирования одного изображения
// 1. EnforceOriginalLimitsAsync (LoadAsync внутри)
var (compressedBytes, wasCompressed) = await _imageCompressor.EnforceOriginalLimitsAsync(originalStream);

// 2. Image.IdentifyAsync (повторно)
var imageInfo = await Image.IdentifyAsync(originalStream, cancellationToken);

// 3. CompressImageAsync (LoadAsync внутри)
var compressedBytes = await _imageCompressor.CompressImageAsync(originalStream, customWidth);
```

#### Варианты решения

Добавить в `ImageCompressor` метод, который за один проход возвращает одновременно сжатый оригинал, превью и размеры.

```csharp
// ✅ Новый метод ProcessImageAllInOneAsync — один проход по изображению
public async Task<ImageProcessingResult> ProcessImageAllInOneAsync(
    Stream inputStream,
    int previewWidth = 1024,
    bool enforceOriginalLimits = true)
{
    using var image = await Image.LoadAsync(inputStream); // Единственный LoadAsync

    // Получаем размеры из уже загруженного объекта
    var width = image.Width;
    var height = image.Height;

    byte[]? compressedOriginal = null;
    if (enforceOriginalLimits && (image.Width > MaxOriginalSide || image.Height > MaxOriginalSide
        || inputStream.Length > MaxOriginalSizeBytes))
    {
        // Сжимаем оригинал
        if (image.Width > MaxOriginalSide || image.Height > MaxOriginalSide)
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(MaxOriginalSide, MaxOriginalSide) }));

        image.Mutate(x => x.BackgroundColor(Color.White));
        using var origStream = new MemoryStream();
        await image.SaveAsync(origStream, new JpegEncoder { Quality = OriginalJpegQuality });
        compressedOriginal = origStream.ToArray();
    }

    // Создаём превью из уже загруженного изображения (clone для сохранения оригинала)
    using var previewImage = image.Clone(x => x.Resize(new ResizeOptions
        { Mode = ResizeMode.Max, Size = new Size(previewWidth, 0) }));
    previewImage.Mutate(x => x.BackgroundColor(Color.White));

    using var previewStream = new MemoryStream();
    await previewImage.SaveAsync(previewStream, new JpegEncoder { Quality = PreviewJpegQuality });

    return new ImageProcessingResult(
        CompressedOriginal: compressedOriginal,
        PreviewBytes: previewStream.ToArray(),
        Width: width,
        Height: height
    );
}
```

---

### OPT-04 — `GetTempDownloadUrl` создаёт TempFile записи в цикле (N+1 запросов к БД)

#### Описание

При запросе временных ссылок для N файлов выполняется N отдельных `INSERT` в таблицу `TempFiles` — по одному на каждый файл. При большом количестве файлов это N round-trip'ов к PostgreSQL.

**Путь:** `Backend/BarkFluff.Files/Features/GetTempDownloadUrl/GetTempDownloadUrlCommandHandler.cs` : строки 60–73

```csharp
// ❌ N INSERT'ов в цикле
foreach (var file in files)
{
    // Каждый вызов = отдельный SaveChangesAsync → round-trip к БД
    var tempFile = await _tempFilesStorage.CreateTempFile(file.Id);
    var url = FileUrlHelper.GenerateDownloadUrl(baseUrl, tempFile.Id);
    response.FileUrls.Add(...);
}
```

#### Варианты решения

Добавить в `TempFilesStorage` метод пакетного создания.

```csharp
// ✅ Batch-вставка всех TempFile за один SaveChangesAsync
public async Task<List<TempFile>> CreateTempFilesBatchAsync(List<Guid> fileIds)
{
    var expiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(
        int.Parse(_configuration["TempFiles:ExpiresAt"]));

    var tempFiles = fileIds.Select(id => new TempFile
    {
        OriginalFileId = id,
        ExpiresAt = expiresAt
    }).ToList();

    await _context.TempFiles.AddRangeAsync(tempFiles);
    await _context.SaveChangesAsync(); // Один round-trip вместо N

    return tempFiles;
}
```

---

## 🟠 Баги и недоработки

### BUG-04 — Отсутствует очистка устаревших `TempFile` записей

#### Описание

`TempFile` записи имеют поле `ExpiresAt`, но нет фонового задания (Hosted Service, Hangfire, cron) для их периодического удаления из БД. Таблица будет бесконечно расти. При большой нагрузке запросы к `TempFiles` будут деградировать.

**Путь:** `Backend/BarkFluff.Files/Persistence/TempFilesStorage.cs` — метода очистки нет  
**Путь:** `Backend/BarkFluff.Files/Program.cs` — фоновых сервисов очистки нет

```csharp
// ❌ Нигде нет очистки просроченных TempFile
// GetTempFile фильтрует их по ExpiresAt, но не удаляет из БД
public async Task<TempFile?> GetTempFile(Guid tempFileId)
{
    return await _context.TempFiles
        .Where(x => x.ExpiresAt > DateTime.UtcNow) // Фильтрует, но не чистит
        .FirstOrDefaultAsync(x => x.Id == tempFileId);
}
```

#### Варианты решения

```csharp
// ✅ Добавить IHostedService для периодической очистки
public class TempFileCleanupService(IServiceScopeFactory scopeFactory, ILogger<TempFileCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FilesContext>();

            var deleted = await context.TempFiles
                .Where(x => x.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync(stoppingToken); // EF Core 7+ bulk delete

            logger.LogInformation("Удалено {Count} устаревших временных ссылок", deleted);
        }
    }
}

// В Program.cs:
builder.Services.AddHostedService<TempFileCleanupService>();
```

---

### 

## 🔵 Прочее / качество кода

---

### MISC-01 — `S3BucketRegistry` кэш клиентов строится по ключу включающему SecretKey в открытом виде

#### Описание

Ключ кэша для S3-клиентов строится как `"{ServiceUrl}|{AccessKey}|{SecretKey}"`. SecretKey попадает в строку, которая хранится в словаре в памяти. При дампе памяти процесса или утечке heap-профиля секрет может быть обнаружен в открытом виде.

**Путь:** `Backend/BarkFluff.Files/Infrastructure/S3BucketRegistry.cs` : строка 94

```csharp
// ⚠️ SecretKey в открытом виде как ключ словаря
var clientKey = $"{opts.ServiceUrl}|{opts.AccessKey}|{opts.SecretKey}";
```

#### Варианты решения

```csharp
// ✅ Хешировать ключ для идентификации уникальных конфигураций
var rawKey = $"{opts.ServiceUrl}|{opts.AccessKey}|{opts.SecretKey}";
var clientKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
// SecretKey не хранится в открытом виде в словаре
```

---

### MISC-02 — `ImageCompressor` не зарегистрирован как Singleton, создаётся каждый раз как Scoped

#### Описание

`ImageCompressor` зарегистрирован как `Scoped` (`services.AddScoped<ImageCompressor>()`), но не имеет никакого состояния — все методы stateless. Создание нового экземпляра на каждый запрос бессмысленно с точки зрения allocation.

**Путь:** `Backend/BarkFluff.Files/Program.cs` : строка 60

```csharp
// ⚠️ Scoped без необходимости — stateless сервис
builder.Services.AddScoped<ImageCompressor>();
```

#### Варианты решения

```csharp
// ✅ Singleton для stateless сервиса
builder.Services.AddSingleton<ImageCompressor>();
// Аналогично FileTypeDetector уже зарегистрирован как Singleton — такой же паттерн
```

---

### ### MISC-04 — Устаревшие/несуществующие `TempFile` записи без очистки S3-объектов

#### Описание

При дедупликации `UploadFileCommandHandler` вызывает `_filesStorage.DeleteFile(file.Id)` для удаления дубликата из БД, но **не удаляет запись из `FileHashes`**. Если файл был уже добавлен в `FileHashes` (в отличие от дедупликации), это создаёт "висячий" хеш без соответствующей записи в `UploadedFiles`.

Аналогично — при ошибке после `S3Uploader.UploadAsync` (исключение в генерации превью или сохранении в БД) файл уже залит в S3, но запись в БД не создана → `orphan`-объект в S3.

**Путь:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs` : строки 217–224

```csharp
// ❌ Удаляем запись из UploadedFiles, но не из FileHashes
await _filesStorage.AddUploaderToFile(existingFileId.Value, uploaderId);
await _filesStorage.DeleteFile(file.Id);
// ← fileHash уже мог быть сохранён в FileHashes для file.Id — висячая запись
await originalStream.DisposeAsync();
return existingFileId.Value.ToString();
```

#### Варианты решения

```csharp
// ✅ Также чистить FileHashes при удалении дубликата
await _filesStorage.AddUploaderToFile(existingFileId.Value, uploaderId);
await _filesStorage.DeleteFile(file.Id);
// Удалить хеш для удалённого файла (если был сохранён)
await _hashesStorage.DeleteHashByFileId(file.Id);

await originalStream.DisposeAsync();
return existingFileId.Value.ToString();
```



# 
