# Аудит проекта: BarkFluff.Files

> **Дата:** 2026-07  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)  
> **Сервис:** `Backend/BarkFluff.Files` — файловый сервис (S3/Minio, PostgreSQL, gRPC + REST)  
> **Статус:** 🔴 Найдены критические и высокоприоритетные проблемы

---

## Содержание

1. [🔴 Безопасность](#безопасность)
2. [🟡 Оптимизация](#оптимизация)
3. [🟠 Баги и недоработки](#баги-и-недоработки)
4. [🔵 Прочее / качество кода](#прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Нет авторизации на HTTP endpoint `/download/{fileId}`

#### Описание
Эндпоинт `GET /download/{fileId}` в `FilesController` доступен **без какой-либо аутентификации**. Любой анонимный пользователь может скачать файл, зная его UUID. Аватарки и картинки чатов публичны по задумке, но через этот же эндпоинт доступны превью файлов и картинки бейджей — без проверки токена.

#### В чём проблема
Нет атрибута `[Authorize]` или проверки JWT на контроллере. Идентификаторы файлов — UUID, предсказать их сложно, но возможна утечка через логи, метаданные или брут-форс превью-файлов.

**Путь:** `Backend/BarkFluff.Files/Host/FilesController.cs` : строки 54–76

```csharp
// ❌ Нет [Authorize] — эндпоинт полностью открыт
[HttpGet("download/{fileId}")]
public async Task<IActionResult> DownloadFile([FromRoute] Guid fileId)
{
    // ... скачивание без проверки токена
}
```

#### Варианты решения

**Вариант A** — Разрешить публичный доступ только для публичных типов (аватары, картинки чатов, постеры), остальное — через временные ссылки (уже есть `GetTempDownloadUrl`). Добавить проверку типа ДО скачивания из S3 без изменения публичного API.

**Вариант B** — Добавить опциональную JWT-проверку: если токен есть — верифицировать, если нет — разрешать только публичные типы.

```csharp
// ✅ Вариант A: проверка до скачивания из S3
[HttpGet("download/{fileId}")]
public async Task<IActionResult> DownloadFile([FromRoute] Guid fileId)
{
    // Быстрая проверка типа без скачивания контента
    var fileType = await _mediator.Send(new GetFileTypeQuery { FileId = fileId });

    var isPublicType = fileType is UploadFileType.UserAvatar
        or UploadFileType.ChatPicture
        or UploadFileType.UserProfilePoster;

    if (!isPublicType)
    {
        // Для приватных файлов требуем наличие в TempFiles (временная ссылка)
        var isTempValid = await _mediator.Send(new ValidateTempLinkQuery { FileId = fileId });
        if (!isTempValid)
            return Forbid();
    }
    // ... далее скачивание
}
```

---

### SEC-02 — Нет авторизации на HTTP endpoint `POST /upload/{uploadId}`

#### Описание
`POST /upload/{uploadId}` тоже не защищён токеном на уровне контроллера. Атаку сложно провести без валидного `uploadId` (UUID из `GetUploadUrl`, требующего JWT), однако атакующий знающий UUID может загрузить произвольный файл вместо легитимного пользователя — TOCTOU-уязвимость.

#### В чём проблема
После получения `uploadId` через gRPC у атакующего есть окно для подмены файла. Нет привязки `uploadId` к конкретному JWT-токену/userId на уровне HTTP.

**Путь:** `Backend/BarkFluff.Files/Host/FilesController.cs` : строки 23–52

```csharp
// ❌ Нет [Authorize], нет проверки владельца uploadId
[HttpPost("upload/{uploadId}")]
[RequestSizeLimit(536_870_912)]
public async Task<IActionResult> UploadFile([FromRoute] Guid uploadId, [FromForm] IFormFile? file)
{
    // uploadId создавался для конкретного userId, но здесь нет проверки
    var resultFileId = await _mediator.Send(command);
}
```

#### Варианты решения

Передавать JWT-токен в заголовке и верифицировать принадлежность `uploadId` к конкретному пользователю внутри `UploadFileCommandHandler`.

```csharp
// ✅ В UploadFileCommandHandler.Handle():
// После получения file из БД — проверяем владельца
if (!file.Uploaders.Contains(request.UserId))
{
    _logger.LogWarning("Попытка загрузки в чужой uploadId {FileId} пользователем {UserId}",
        request.FileId, request.UserId);
    throw new UnauthorizedException("Нет доступа к этому uploadId");
}
```

---

### SEC-03 — `GetTempDownloadUrl` не проверяет принадлежность файлов пользователю

#### Описание
В `GetTempDownloadUrlCommandHandler` пользователь передаёт список `FileIds` и получает временные ссылки **без проверки**, что он является владельцем (uploader) этих файлов. Злоумышленник может передать чужие `fileId` и получить ссылки на чужие файлы.

#### В чём проблема
Нет проверки `file.Uploaders.Contains(currentUserId)` перед выдачей временной ссылки. Метод вызывается из `FilesApiService` (клиентский gRPC, `TokenType.User`).

**Путь:** `Backend/BarkFluff.Files/Features/GetTempDownloadUrl/GetTempDownloadUrlCommandHandler.cs` : строки 60–73

```csharp
// ❌ Нет проверки принадлежности файлов текущему пользователю
foreach (var file in files)
{
    // Любой авторизованный пользователь получит ссылку на любой файл
    var tempFile = await _tempFilesStorage.CreateTempFile(file.Id);
    var url = FileUrlHelper.GenerateDownloadUrl(baseUrl, tempFile.Id);
    response.FileUrls.Add(...);
}
```

#### Варианты решения

Добавить `UserContext` и фильтровать файлы по `Uploaders`.

```csharp
// ✅ Проверяем владельца перед созданием временной ссылки
foreach (var file in files)
{
    // Файл должен принадлежать текущему пользователю
    if (!file.Uploaders.Contains(_userContext.UserId))
    {
        _logger.LogWarning(
            "Пользователь {UserId} запросил ссылку на чужой файл {FileId}",
            _userContext.UserId, file.Id);
        throw new PermissionDeniedException("Нет доступа к файлу");
    }
    var tempFile = await _tempFilesStorage.CreateTempFile(file.Id);
    // ...
}
```

---

### SEC-04 — `CheckFileHash` добавляет текущего пользователя как загрузчика без валидации

#### Описание
`CheckFileHashCommandHandler` при нахождении хеша в БД немедленно вызывает `AddUploaderToFile`, записывая текущего пользователя как загрузчика. Но файл с этим хешем может принадлежать **другому типу** или быть в другом бакете — деталь учитывается при серверной дедупликации в `UploadFileCommandHandler`, но `CheckFileHash` этого не делает.

#### В чём проблема
Пользователь может вызвать `CheckFileHash` с хешем любого файла в системе (например, чужого документа), и система запишет его как uploader — что повлияет на статистику хранилища и видимость файла.

**Путь:** `Backend/BarkFluff.Files/Features/CheckFileHash/CheckFileHashCommandHandler.cs` : строки 51–63

```csharp
// ❌ Нет проверки типа файла перед добавлением uploader'а
var fileId = await _hashesStorage.GetFileIdByHash(normalizedHash);
if (fileId.HasValue)
{
    // Добавляем пользователя к любому файлу с этим хешем
    await _filesStorage.AddUploaderToFile(fileId.Value, _userContext.UserId);
    return new CheckFileHashResponse { FileId = fileId.Value.ToString() };
}
```

#### Варианты решения

Передавать запрошенный `FileType` в команде и добавлять uploader только если тип совпадает.

```csharp
// ✅ Проверяем тип файла перед регистрацией uploader'а
var fileId = await _hashesStorage.GetFileIdByHash(normalizedHash);
if (fileId.HasValue)
{
    var existingFile = await _filesStorage.GetFile(fileId.Value);
    // Дедупликация только если тип совпадает с запрошенным
    if (existingFile?.Type == request.FileType && !string.IsNullOrEmpty(existingFile.Etag))
    {
        await _filesStorage.AddUploaderToFile(fileId.Value, _userContext.UserId);
        return new CheckFileHashResponse { FileId = fileId.Value.ToString() };
    }
}
// Тип не совпал — файл не найден с т.з. дедупликации
return new CheckFileHashResponse { FileId = string.Empty };
```

---

### SEC-05 — `DeleteStickerPack` не проверяет владельца пака

#### Описание
`DeleteStickerPackCommandHandler` удаляет стикерпак по ID без проверки, что `CreatorUserId` совпадает с текущим пользователем. Любой авторизованный пользователь может удалить чужой стикерпак, зная его UUID.

#### В чём проблема
`StickerPack` содержит поле `CreatorUserId`, но в хэндлере оно не проверяется.

**Путь:** `Backend/BarkFluff.Files/Features/DeleteStickerPack/DeleteStickerPackCommandHandler.cs` : строки 19–26

```csharp
// ❌ Нет проверки CreatorUserId == currentUserId
public async Task<DeleteStickerPackResponse> Handle(DeleteStickerPackCommand request, ...)
{
    // Любой пользователь может удалить любой стикерпак
    await _stickerPacksStorage.DeleteAsync(request.PackId);
}
```

#### Варианты решения

```csharp
// ✅ Проверяем владельца стикерпака
var pack = await _stickerPacksStorage.GetByIdWithoutStickersAsync(request.PackId)
    ?? throw new NotFoundException("Стикерпак не найден");

if (pack.CreatorUserId != _userContext.UserId)
{
    _logger.LogWarning(
        "Попытка удалить чужой стикерпак {PackId}. Запросил: {UserId}, Владелец: {OwnerId}",
        request.PackId, _userContext.UserId, pack.CreatorUserId);
    throw new PermissionDeniedException("Нет прав на удаление стикерпака");
}

await _stickerPacksStorage.DeleteAsync(request.PackId);
```

---

### SEC-06 — `UploadBadgeImage` не валидирует формат файла

#### Описание
`UploadBadgeImageCommandHandler` принимает байты PNG-изображения от сервисного токена (`TokenType.Service`) без проверки magic bytes. Документация говорит «PNG без сжатия», но кода проверки нет — AdminPanel может прислать произвольные байты.

#### В чём проблема
Хотя endpoint защищён `TokenType.Service`, отсутствие валидации является плохой практикой defence-in-depth. При компрометации AdminPanel злоумышленник может залить произвольный файл в S3.

**Путь:** `Backend/BarkFluff.Files/Features/UploadBadgeImage/UploadBadgeImageCommandHandler.cs` : строки 38–65

```csharp
// ❌ Нет проверки magic bytes входных данных
using var stream = new MemoryStream(request.ImageData);
var contentType = request.Filename.GetContentType(); // Только расширение файла
var etag = await _s3Uploader.UploadAsync(bucketName, $"{badgeImage.Id}", stream, contentType);
```

#### Варианты решения

```csharp
// ✅ Проверяем PNG magic bytes перед загрузкой
private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

if (request.ImageData.Length < 8 ||
    !request.ImageData.Take(8).SequenceEqual(PngSignature))
{
    throw new InvalidFileFormatException("Изображение бейджа должно быть в формате PNG");
}
```

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
```

---

### OPT-02 — SHA256 вычисляется отдельным проходом по файлу после его буферизации

#### Описание
В `UploadFileCommandHandler` файл сначала копируется в `MemoryStream` / `FileStream`, затем отдельным вызовом вычисляется SHA256. Итого — **два полных прохода** по данным (копирование + хеширование).

#### В чём проблема
Для больших файлов (>100 МБ) это вдвое увеличивает I/O. Хеш можно вычислять **во время** первичного копирования потока.

**Путь:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs` : строки 142–151  
(код даже содержит TODO-комментарий об этом)

```csharp
// ❌ Отдельный второй проход для хеширования
// TODO: For better performance with large files, consider computing hash during the initial stream copy
string fileHash;
using (var sha256 = SHA256.Create())
{
    var hashBytes = await sha256.ComputeHashAsync(originalStream, cancellationToken);
    fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
}
originalStream.Position = 0;
```

#### Варианты решения

Использовать `CryptoStream` во время первичного копирования.

```csharp
// ✅ Хеш вычисляется параллельно с копированием потока
var memStream = new MemoryStream();
using var sha256 = SHA256.Create();
using var cryptoStream = new CryptoStream(memStream, sha256, CryptoStreamMode.Write, leaveOpen: true);

await request.FileStream.CopyToAsync(cryptoStream, cancellationToken);
await cryptoStream.FlushFinalBlockAsync(cancellationToken);

var fileHash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
memStream.Position = 0;
originalStream = memStream;
// Один проход вместо двух — копирование + хеш одновременно
```

---

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

### OPT-05 — `AddStickerCommandHandler` скачивает стикер из S3 только для генерации превью

#### Описание
При добавлении стикера обработчик скачивает оригинальный файл из S3 только для того, чтобы создать превью 64×64. Но файл уже был загружен клиентом через `/upload/` — его байты можно было бы передать напрямую без лишнего round-trip к S3.

**Путь:** `Backend/BarkFluff.Files/Features/AddSticker/AddStickerCommandHandler.cs` : строки 66–71

```csharp
// ❌ Скачиваем весь файл из S3 только ради превью
using var originalStream = await _s3Uploader.DownloadAsync(bucketName, $"{file.Id}");
var previewBytes = await _imageCompressor.GenerateStickerPreviewAsync(originalStream);
```

#### Варианты решения

Добавить в команду `AddStickerCommand` опциональное поле `ImageData` (передаётся клиентом, если доступно) или хранить превью на этапе загрузки файла типа `MessageAttachmentSticker` аналогично тому, как это делается для аватарок и картинок чатов.

```csharp
// ✅ Генерировать превью стикера на этапе загрузки файла
// В UploadFileCommandHandler.cs — добавить MessageAttachmentSticker в _filesToNeedGeneratePreview:
private readonly List<UploadFileType> _filesToNeedGeneratePreview = [
    UploadFileType.ChatPicture,
    UploadFileType.MessageAttachmentImage,
    UploadFileType.UserAvatar,
    UploadFileType.MessageAttachmentSticker // ← добавить
];

// Тогда при AddSticker превью уже есть в file.PreviewId — не нужен download из S3
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — `TempFilesStorage.CreateTempFile` использует `int.Parse` без обработки ошибок

#### Описание
Метод `CreateTempFile` парсит конфигурационное значение `TempFiles:ExpiresAt` через `int.Parse` без try/catch и без null-проверки. При отсутствии ключа в конфигурации или неверном значении — неотловленное исключение во время выполнения.

**Путь:** `Backend/BarkFluff.Files/Persistence/TempFilesStorage.cs` : строка 24

```csharp
// ❌ Может выброситься NullReferenceException или FormatException
ExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(
    int.Parse(_configuration["TempFiles:ExpiresAt"]))
```

#### Варианты решения

```csharp
// ✅ Безопасный парсинг с fallback-значением
private const int DefaultTempFileExpiryMinutes = 60;

var expiryMinutes = _configuration.GetValue<int?>("TempFiles:ExpiresAt")
    ?? DefaultTempFileExpiryMinutes;

ExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(expiryMinutes);
```

---

### BUG-02 — `GetTempDownloadUrlCommandHandler` проверяет `files is null` после обращения к `.Count`

#### Описание
В хэндлере есть логически мёртвый код: проверка `if (files is null)` выполняется **после** того, как у `files` уже вызывалось свойство `.Count`. Если бы `files` был `null`, код упал бы на строкой выше с `NullReferenceException`. Проверка никогда не выполнится.

**Путь:** `Backend/BarkFluff.Files/Features/GetTempDownloadUrl/GetTempDownloadUrlCommandHandler.cs` : строки 37–49

```csharp
// ❌ files.Count вызывается ДО проверки на null — мёртвый код
if (files.Count != request.FileIds.Count) // ← NRE если files == null
{
    if (files is null) // ← никогда не выполнится
    {
        throw new FileNotFoundException();
    }
}
```

#### Варианты решения

```csharp
// ✅ Правильный порядок проверок
if (files is null || files.Count == 0)
    throw new FileNotFoundException("Файлы не найдены");

if (files.Count != request.FileIds.Count)
{
    _logger.LogWarning("Найдено {FoundCount} файлов из {RequestedCount} запрошенных",
        files.Count, request.FileIds.Count);
    // При необходимости — выбросить исключение или вернуть частичный результат
}
```

---

### BUG-03 — `DownloadFileCommandHandler` выбрасывает `Exception("Файл не найден")` вместо типизированного исключения

#### Описание
В двух местах `DownloadFileCommandHandler` бросает необобщённый `new Exception(...)`. В `FilesController` это поймает блок `catch (Exception ex)` и вернёт `NotFound($"Ошибка при скачивании файла: {ex.Message}")` — утечка внутреннего сообщения в HTTP-ответ.

**Путь:** `Backend/BarkFluff.Files/Features/DownloadFile/DownloadFileCommandHandler.cs` : строки 47, 116

```csharp
// ❌ Необобщённое исключение + утечка сообщения в HTTP-ответ
throw new Exception("Файл не найден");
// ...
throw new Exception("Файл не найден");
```

```csharp
// В контроллере — внутреннее сообщение попадает в ответ клиенту
catch (Exception ex)
{
    return NotFound($"Ошибка при скачивании файла: {ex.Message}"); // ← утечка
}
```

#### Варианты решения

```csharp
// ✅ Использовать существующее типизированное исключение
throw new FileNotUploadedException("Файл не найден");

// В контроллере — добавить отдельный catch:
catch (FileNotUploadedException)
{
    return NotFound(); // Без деталей внутренней ошибки
}
catch (Exception)
{
    return StatusCode(500); // Или минимальное сообщение без деталей
}
```

---

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

### BUG-05 — Race condition при дедупликации файлов

#### Описание
В `UploadFileCommandHandler` проверка хеша и создание новой записи не атомарны. Два одновременных запроса с одинаковым файлом могут оба пройти проверку `GetFileIdByHash` (вернёт `null`), оба загрузить файл в S3 и оба сохранить `FileHash` — что приведёт к дублированию в S3 и ошибке unique-constraint при втором `AddHash`.

**Путь:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs` : строки 200–344

```csharp
// ❌ Не атомарная операция "проверь-и-вставь"
var existingFileId = await _hashesStorage.GetFileIdByHash(fileHash); // Thread 1: null
// ... Thread 2 тоже получает null в этот момент ...
// Оба идут дальше и загружают в S3

await _hashesStorage.AddHash(fileHashEntity); // Thread 2: unique constraint violation
```

#### Варианты решения

Использовать `INSERT ... ON CONFLICT DO NOTHING` / `UPSERT` в PostgreSQL или оптимистичную блокировку.

```csharp
// ✅ Использовать INSERT с ON CONFLICT для атомарности
public async Task<bool> TryAddHash(FileHash fileHash)
{
    try
    {
        _context.FileHashes.Add(fileHash);
        await _context.SaveChangesAsync();
        return true;
    }
    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true)
    {
        // Другой поток уже вставил этот хеш — дедупликация всё равно произошла
        _context.Entry(fileHash).State = EntityState.Detached;
        return false;
    }
}
```

---

### BUG-06 — `UploadFileCommandHandler.isImageType` не включает `UserProfilePoster`

#### Описание
Переменная `isImageType` используется для принятия решения о буферизации в RAM vs диск. `UserProfilePoster` является графическим типом, но не включён в список — при загрузке постера > 100 МБ он пойдёт на диск, однако после этого не пройдёт через `EnforceOriginalLimitsAsync` (тот проверяет только `MessageAttachmentImage`). Логика расщеплена и несинхронизирована.

**Путь:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs` : строки 115–118, 235

```csharp
// ❌ UserProfilePoster не включён в isImageType
var isImageType = file.Type is UploadFileType.UserAvatar
    or UploadFileType.MessageAttachmentImage
    or UploadFileType.ChatPicture
    or UploadFileType.MessageAttachmentGif;
// UserProfilePoster отсутствует → буферизуется через диск при >100МБ

// Ниже EnforceOriginalLimitsAsync только для MessageAttachmentImage
if (file.Type == UploadFileType.MessageAttachmentImage && contentType.StartsWith("image/"))
// ← UserProfilePoster тоже должен сжиматься по лимитам
```

#### Варианты решения

```csharp
// ✅ Добавить UserProfilePoster в isImageType и в EnforceOriginalLimits
var isImageType = file.Type is UploadFileType.UserAvatar
    or UploadFileType.MessageAttachmentImage
    or UploadFileType.ChatPicture
    or UploadFileType.MessageAttachmentGif
    or UploadFileType.UserProfilePoster; // ← добавить

// И расширить проверку принудительного сжатия:
if (file.Type is (UploadFileType.MessageAttachmentImage or UploadFileType.UserProfilePoster)
    && contentType.StartsWith("image/"))
{
    // Применять EnforceOriginalLimitsAsync для обоих типов
}
```

---

### BUG-07 — `DownloadFileCommandHandler` мутирует объект домена при скачивании превью

#### Описание
При скачивании превью код мутирует поле `file.Id`, заменяя его на `file.PreviewId`. Это хак, который меняет доменный объект для целей рендеринга. Если объект когда-либо будет трекаться EF Core или переиспользован, это приведёт к неочевидным багам.

**Путь:** `Backend/BarkFluff.Files/Features/DownloadFile/DownloadFileCommandHandler.cs` : строки 105–109

```csharp
// ❌ Мутируем Id доменного объекта для подстановки ключа S3
if (file != null)
{
    file.Id = file.PreviewId!.Value; // ← Мутация Id — плохая практика
}
```

#### Варианты решения

```csharp
// ✅ Использовать локальную переменную вместо мутации
Guid s3Key;
if (file != null)
{
    s3Key = file.PreviewId!.Value; // Запоминаем ключ превью
    // file.Id остаётся неизменным
}
// ...
var fileStream = await _s3Uploader.DownloadAsync(bucketName, $"{s3Key}");
```

---

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

### MISC-03 — Отсутствует Rate Limiting на загрузку файлов

#### Описание
`POST /upload/{uploadId}` принимает файлы до 512 МБ без ограничения частоты запросов. Нет rate limiting ни на уровне контроллера, ни на уровне middleware. Один пользователь может заспамить сервер параллельными загрузками.

**Путь:** `Backend/BarkFluff.Files/Host/FilesController.cs` : строка 23  
**Путь:** `Backend/BarkFluff.Files/Program.cs` — rate limiting middleware не подключён

#### Варианты решения

```csharp
// ✅ ASP.NET Core Rate Limiting (встроен с .NET 7+)
// В Program.cs:
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("upload", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Request.RouteValues["uploadId"]?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3,                      // 3 загрузки
                Window = TimeSpan.FromMinutes(1),     // в минуту
                SegmentsPerWindow = 4
            }));
});

app.UseRateLimiter();

// На контроллере:
[HttpPost("upload/{uploadId}")]
[EnableRateLimiting("upload")]
public async Task<IActionResult> UploadFile(...)
```

---

### MISC-04 — Устаревшие/несуществующие `TempFile` записи без очистки S3-объектов

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

---

### MISC-05 — `FileUrlHelper` зависит от `IConfiguration` напрямую вместо Options-паттерна

#### Описание
`FileUrlHelper.GetPublicBaseUrl` принимает `IConfiguration` и `RunSettings` и вручную читает строку `ExternalEndpoint:Host`. Это нарушает принцип Options Pattern и затрудняет тестирование — нужно мокать весь `IConfiguration`.

**Путь:** `Backend/BarkFluff.Files/Helpers/FileUrlHelper.cs`

#### Варианты решения

```csharp
// ✅ Создать strongly-typed options
public class FilesPublicUrlOptions
{
    public string? ExternalHost { get; set; }
}

// В Program.cs:
builder.Services.Configure<FilesPublicUrlOptions>(
    builder.Configuration.GetSection("ExternalEndpoint"));

// FileUrlHelper становится тестируемым через IOptions<FilesPublicUrlOptions>
```

---

*Документ сгенерирован на основе статического анализа кода. Все строки кода проверены вручную по исходным файлам проекта.*
