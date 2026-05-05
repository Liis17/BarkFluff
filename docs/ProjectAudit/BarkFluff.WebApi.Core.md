# Аудит проекта: BarkFluff.WebApi.Core

> **Дата:** 2025  
> **Ревьюер:** GitHub Copilot (BarkfluffAgent)  
> **Проект:** `Windows\BarkFluff.WebApi.Core`  
> **Версия .NET:** .NET 9 / .NET 10  
> **Тип:** Shared-библиотека, клиентская обёртка над gRPC-сервисами платформы

---

## Навигация

- [🔴 Безопасность](#безопасность)
- [🟠 Баги и недоработки](#баги-и-недоработки)
- [🟡 Оптимизация и производительность](#оптимизация-и-производительность)
- [🔵 Архитектура и прочее](#архитектура-и-прочее)

---

## Безопасность

---

### SEC-01 — EnsureHttpPrefix по умолчанию подставляет `http://` (небезопасный протокол)

**Описание:**  
Утилитарный метод `EnsureHttpPrefix` при отсутствии схемы у URL добавляет `http://`, а не `https://`. Это значит, что если клиент передаст адрес сервера без явной схемы (например `server.barkfluff.com:443`), все gRPC-соединения будут установлены по незащищённому каналу без TLS — токены, сообщения и метаданные устройств будут передаваться открытым текстом.

**Конкретно в чём проблема:**  
Нет явного требования HTTPS; при неверной конфигурации происходит тихий downgrade до HTTP.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\WebApi.cs` : строки 158–163

```csharp
// ❌ ПРОБЛЕМА: при отсутствии схемы добавляется http://, а не https://
public static string EnsureHttpPrefix(string _url)
{
    return !_url.StartsWith("http://") && !_url.StartsWith("https://")
           ? "http://" + _url   // ← небезопасно
           : _url;
}
```

**Варианты решения:**  
1. Заменить дефолтную схему на `https://`  
2. Добавить предупреждение / выброс исключения при обнаружении `http://` в production

```csharp
// ✅ РЕШЕНИЕ: дефолт — https://
public static string EnsureHttpPrefix(string url)
{
    if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return url;

    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
    {
        // Опционально: логировать предупреждение о небезопасном соединении
        System.Diagnostics.Debug.WriteLine($"WARNING: Using insecure HTTP connection to {url}");
        return url;
    }

    return "https://" + url; // ← безопасный дефолт
}
```

---

### SEC-02 — Двойная инъекция Bearer-токена в streaming-методах

**Описание:**  
В `WebApiUpdateManager`, `WebApiOnlinerManager` токен авторизации вручную добавляется в `Metadata` заголовки перед вызовом streaming gRPC-методов. При этом `JwtClientInterceptor` уже зарегистрирован на канале и автоматически добавляет тот же заголовок `Authorization`. В итоге каждый streaming-запрос несёт два идентичных заголовка `Authorization: Bearer ...`.

**Конкретно в чём проблема:**  
- Дублирование токена в заголовках — нарушение принципа единственной ответственности  
- Ручной токен не обновляется автоматически вместе с интерцептором после `TokenRefreshed`  
- Потенциально может конфликтовать с серверной логикой парсинга заголовков

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiUpdateManager.cs` : строки 27–31, 87–91  
**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiOnlinerManager.cs` : строки 32–36

```csharp
// ❌ ПРОБЛЕМА: ручное добавление токена — он уже есть через JwtClientInterceptor на канале
var headers = new Metadata();
if (!string.IsNullOrEmpty(globalParam.AccessToken?.Value))
{
    headers.Add("Authorization", $"Bearer {globalParam.AccessToken.Value}"); // ← дубль
}
var response = UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest { }, headers);
```

**Варианты решения:**  
Убрать ручное добавление заголовка — интерцептор сделает это сам.

```csharp
// ✅ РЕШЕНИЕ: без ручных заголовков — интерцептор JwtClientInterceptor уже обрабатывает это
var response = UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest());
```

---

### SEC-03 — Старые gRPC-каналы не закрываются при переинициализации

**Описание:**  
При каждом вызове `AddInterceptor` (что происходит при обновлении токена) создаются новые `GrpcChannel` для всех 7 сервисов. Старые каналы просто перезаписываются в полях `_webApi.*Channel` — `Dispose()` на них не вызывается. Канал поддерживает HTTP/2 соединения и занимает сокеты ОС. За 8 часов активной сессии (обновление токена каждые ~4 минуты) будет утечка ~120 незакрытых каналов.

**Конкретно в чём проблема:**  
Утечка ресурсов: открытые TCP-соединения, хэндлы ОС, потоки обработки.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiClientManager.cs` : строки 107–143

```csharp
// ❌ ПРОБЛЕМА: старые каналы не закрываются перед присвоением новых
_webApi.MessagesChannel = GrpcChannel.ForAddress(_gParam.SocketMessages); // старый канал потерян
_webApi.FilesChannel    = GrpcChannel.ForAddress(_gParam.SocketFiles);
_webApi.IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
// ... и так далее
```

**Варианты решения:**  
Добавить `?.Dispose()` перед пересозданием каналов.

```csharp
// ✅ РЕШЕНИЕ: явное освобождение старых каналов
_webApi.MessagesChannel?.Dispose();
_webApi.MessagesChannel = GrpcChannel.ForAddress(_gParam.SocketMessages);

_webApi.FilesChannel?.Dispose();
_webApi.FilesChannel = GrpcChannel.ForAddress(_gParam.SocketFiles);

_webApi.IdentityChannel?.Dispose();
_webApi.IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);

_webApi.BeaconChannel?.Dispose();
_webApi.BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);

_webApi.UserChannel?.Dispose();
_webApi.UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);

_webApi.UpdatesChannel?.Dispose();
_webApi.UpdatesChannel = GrpcChannel.ForAddress(_gParam.SocketUpdates);

_webApi.OnlinerChannel?.Dispose();
_webApi.OnlinerChannel = GrpcChannel.ForAddress(_gParam.SocketOnliner);
```

---

### SEC-04 — `GlobalParam.Load` не проверяет минимальную длину файла

**Описание:**  
Метод `Load` читает весь файл в `byte[]` и делает `Array.Copy` по фиксированным смещениям (SaltSize=16, IV=16). Если файл повреждён или подменён злоумышленником и его длина меньше 32 байт — `Array.Copy` выбросит `ArgumentException` с раскрывающим сообщением, либо будет скопированы некорректные данные. Также `JsonSerializer.Deserialize<GlobalParam>(json)` может вернуть `null`, но результат используется без проверки.

**Конкретно в чём проблема:**  
- Отсутствует валидация минимальной длины файла  
- `Deserialize` может вернуть `null` — NullReferenceException в вызывающем коде

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\MessengerData\GlobalParam.cs` : строки 114–138

```csharp
// ❌ ПРОБЛЕМА: нет проверки длины файла и нет проверки null результата десериализации
public static GlobalParam Load(string filePath, string userPin)
{
    var allBytes = File.ReadAllBytes(filePath);
    // ← нет: if (allBytes.Length < SaltSize + 16) throw ...

    var salt = new byte[SaltSize];
    Array.Copy(allBytes, 0, salt, 0, SaltSize); // может упасть с неинформативной ошибкой

    // ...

    var json = Encoding.UTF8.GetString(decryptedBytes);
    return JsonSerializer.Deserialize<GlobalParam>(json); // ← может вернуть null!
}
```

**Варианты решения:**  

```csharp
// ✅ РЕШЕНИЕ: добавить валидацию и null-guard
public static GlobalParam Load(string filePath, string userPin)
{
    var allBytes = File.ReadAllBytes(filePath);

    const int MinFileSize = SaltSize + 16 + 1; // salt + iv + хотя бы 1 байт данных
    if (allBytes.Length < MinFileSize)
        throw new InvalidDataException("Файл настроек повреждён или имеет неверный формат.");

    var salt = new byte[SaltSize];
    Array.Copy(allBytes, 0, salt, 0, SaltSize);

    var iv = new byte[16];
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
    return JsonSerializer.Deserialize<GlobalParam>(json)
        ?? throw new InvalidDataException("Не удалось десериализовать настройки приложения.");
}
```

---

## Баги и недоработки

---

### BUG-01 — `UploadUserAvatarAsync` всегда возвращает `IsSuccess = true` — результат SafeCallAsync игнорируется

**Описание:**  
Метод `UploadUserAvatarAsync` вызывает `SafeCallAsync`, но **не использует** его возвращаемое значение. После блока `try` безусловно возвращается `new ErrorReturner(true)`. Это означает: даже если загрузка аватара упала с ошибкой (неверный токен, нет места, ошибка сети) — вызывающий код получит `IsSuccess = true` и ни о чём не узнает.

**Конкретно в чём проблема:**  
Критическая ошибка логики — успех всегда возвращается независимо от реального результата операции.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiFileManager.cs` : строки 205–250

```csharp
// ❌ ПРОБЛЕМА: результат SafeCallAsync не сохраняется и не возвращается
public async Task<ErrorReturner> UploadUserAvatarAsync(GlobalParam globalParam, byte[] jpegImageBytes)
{
    try
    {
        await _webApi.TokenManager.SafeCallAsync<ErrorReturner>(async () => // ← результат выброшен
        {
            // ... загрузка аватара ...
            return new ErrorReturner(true);
        }, globalParam);
    }
    catch (...)
    {
        return new ErrorReturner(false, "...");
    }
    return new ErrorReturner(true); // ← ВСЕГДА true, даже при ошибке внутри SafeCallAsync
}
```

**Варианты решения:**  
Сохранить и вернуть результат `SafeCallAsync`.

```csharp
// ✅ РЕШЕНИЕ: сохраняем и возвращаем результат
public async Task<ErrorReturner> UploadUserAvatarAsync(GlobalParam globalParam, byte[] jpegImageBytes)
{
    try
    {
        // ← добавить return
        return await _webApi.TokenManager.SafeCallAsync<ErrorReturner>(async () =>
        {
            var getLinkUpload = await FilesAC!.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest
            {
                FileType = Proto.Files.UploadFileType.UserAvatar
            });

            using var formData = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(jpegImageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formData.Add(fileContent, "file", "avatar.jpg");

            var response = await _httpClient.PostAsync(getLinkUpload.Url, formData);
            if (!response.IsSuccessStatusCode)
                return new ErrorReturner(false, $"Ошибка загрузки аватара: {response.StatusCode}");

            try
            {
                await UsersAC!.SetProfilePictureAsync(new Proto.Users.SetProfilePictureRequest
                {
                    FileId = getLinkUpload.FileId
                });
            }
            catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                return new ErrorReturner(false, "Тип файла не соответствует изображению профиля");
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                return new ErrorReturner(false, "Неверный формат идентификатора файла");
            }

            return new ErrorReturner(true);
        }, globalParam);
    }
    catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
    {
        return new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid");
    }
    catch (Exception ex)
    {
        return new ErrorReturner(false, $"Ошибка загрузки аватара: {ex.Message}");
    }
}
```

---

### BUG-02 — `CreateChat` не передаёт `userId` в запрос (TODO, нерабочий метод)

**Описание:**  
Публичный метод `CreateChat(globalParam, userId)` принимает `userId`, но при формировании gRPC-запроса `CreateGroupChatRequest` параметр **не передаётся**. Запрос отправляется пустым. Метод обозначен TODO-комментарием, но по факту экспортируется в публичный API `WebApi` и может быть вызван клиентами.

**Конкретно в чём проблема:**  
Метод публично доступен, ничего не делает правильно, молча возвращает `(true, "")`.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiMessageManager.cs` : строки 196–222

```csharp
// ❌ ПРОБЛЕМА: userId игнорируется, запрос пустой
public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId)
{
    try
    {
        return await _webApi.TokenManager.SafeCallAsync(async () =>
        {
            await MessagesAC!.CreateGroupChatAsync(new Proto.Messages.CreateGroupChatRequest
            {
                // TODO: Добавить UserIds в API запрос (Backend task) ← метод нерабочий
            });

            return (true, string.Empty);
        }, globalParam);
    }
    // ...
}
```

**Варианты решения:**  
1. Реализовать метод корректно при наличии соответствующего gRPC-метода  
2. Временно пометить `[Obsolete]` и бросать `NotImplementedException`

```csharp
// ✅ ВАРИАНТ A: пометить как нереализованный явно
/// <summary>
/// Создание личного чата с пользователем.
/// </summary>
/// <remarks>Требует реализации соответствующего gRPC-метода на бекенде.</remarks>
[Obsolete("Метод не реализован. Ожидается реализация gRPC-контракта CreatePersonalChat на бекенде.")]
public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId)
{
    throw new NotImplementedException(
        "CreateChat требует реализации gRPC-метода CreatePersonalChat. " +
        "Используйте SendMessage с UserId для начала личного чата.");
}

// ✅ ВАРИАНТ B: если на бекенде уже есть нужный метод — передать userId
public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId)
{
    if (!long.TryParse(userId, out var userIdLong))
        return (false, "Неверный формат идентификатора пользователя");

    try
    {
        return await _webApi.TokenManager.SafeCallAsync(async () =>
        {
            var request = new Proto.Messages.CreateGroupChatRequest();
            request.UserIds.Add(userIdLong); // ← передать userId
            var response = await MessagesAC!.CreateGroupChatAsync(request);
            return (true, string.Empty);
        }, globalParam);
    }
    catch (Exception ex)
    {
        return (false, $"Ошибка создания чата: {ex.Message}");
    }
}
```

---

### BUG-03 — `FastAuthManager` не получает клиентов через `SetClients` / `UpdateManagerClients`

**Описание:**  
В `WebApiClientManager.UpdateManagerClients` вызывается `SetClients` для всех 11 менеджеров, кроме `FastAuthManager`. При этом `FastAuthAC` и `FastAuthChannel` существуют в отдельных полях `WebApi` и управляются через `CreateFastAuthClient`/`DisposeFastAuthClient`. Сам `WebApiFastAuthManager` наследует `WebApiBase` и теоретически может получать клиенты через `SetClients`, но `FastAuthAC` не входит в сигнатуру `SetClients` (что является отдельной архитектурной проблемой — см. ARC-02).

**Конкретно в чём проблема:**  
`FastAuthManager` изолирован от общего механизма обновления клиентов — потенциальный рассинхрон состояния при переинициализации.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiClientManager.cs` : строки 221–265

```csharp
// ❌ ПРОБЛЕМА: FastAuthManager отсутствует в UpdateManagerClients
private void UpdateManagerClients()
{
    _webApi.ServerManager.SetClients(...);
    _webApi.TokenManager.SetClients(...);
    _webApi.UserManager.SetClients(...);
    // ... другие менеджеры ...
    _webApi.OnlinerManager.SetClients(...);
    // ← FastAuthManager здесь нет
}
```

**Варианты решения:**  
Добавить `FastAuthManager` в `UpdateManagerClients`, либо вынести управление FastAuth-клиентом в единый механизм.

```csharp
// ✅ РЕШЕНИЕ: добавить FastAuthManager в цепочку обновления
private void UpdateManagerClients()
{
    // ... остальные менеджеры ...
    _webApi.OnlinerManager.SetClients(...);

    // FastAuthManager получает общие клиенты (FastAuthAC управляется отдельно)
    _webApi.FastAuthManager.SetClients(
        _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC,
        _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC, _webApi.OnlinerAC,
        _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel,
        _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel, _webApi.OnlinerChannel);
}
```

---

### BUG-04 — Streaming-методы используют `CancellationToken.None` — стримы невозможно отменить

**Описание:**  
Все три streaming-метода (`JustUpdate`, `SubscribeToReadReceipts`, `SubscribeToOnlineStatus`) вызывают `response.ResponseStream.MoveNext(CancellationToken.None)`. Это означает, что после запуска стрим нельзя отменить программно — ни при смене экрана, ни при разлогине, ни при вызове `Dispose()`. Стрим живёт до закрытия соединения сервером или краша приложения.

**Конкретно в чём проблема:**  
- Нет механизма отмены стрима на стороне клиента  
- При переподключении (после `TokenRefreshed`) старый стрим продолжает работать параллельно с новым  
- Утечка горутин/потоков при частых пересозданиях соединения

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiUpdateManager.cs` : строки 46, 109  
**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiOnlinerManager.cs` : строка 54

```csharp
// ❌ ПРОБЛЕМА: CancellationToken.None — стрим неотменяемый
hasNext = await response.ResponseStream.MoveNext(CancellationToken.None); // ← нет отмены
```

**Варианты решения:**  
Передавать `CancellationToken` через параметр метода.

```csharp
// ✅ РЕШЕНИЕ: добавить CancellationToken в сигнатуру и передавать в MoveNext
public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(
    GlobalParam globalParam,
    CancellationToken cancellationToken = default) // ← новый параметр
{
    return await _webApi.TokenManager.SafeCallAsync(async () =>
    {
        var response = UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest());

        async IAsyncEnumerable<NewMessageEvent> GetMessageStream(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool hasNext;
                try
                {
                    hasNext = await response.ResponseStream.MoveNext(ct); // ← передаём токен
                    if (!hasNext) yield break;
                }
                catch (OperationCanceledException) { yield break; }
                catch (RpcException) { yield break; }
                catch (Exception) { yield break; }

                yield return response.ResponseStream.Current;
            }
        }

        return (new ErrorReturner(true), GetMessageStream(cancellationToken));
    }, globalParam);
}
```

---

### BUG-05 — `GetChats` имеет хардкод размера страницы 50 без пагинации — чаты сверх лимита теряются

**Описание:**  
`GetChats` запрашивает список чатов с `Size = 50` (константа `DefaultPageSize`) и не реализует пагинацию. Если у пользователя более 50 чатов — остальные никогда не будут получены. Метод не информирует об этом вызывающий код.

**Конкретно в чём проблема:**  
Silent data loss — пользователи с большим количеством чатов видят неполный список без каких-либо предупреждений.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiMessageManager.cs` : строки 24–48

```csharp
// ❌ ПРОБЛЕМА: жёсткий лимит 50 чатов без пагинации
var response = await MessagesAC!.ListChatsAsync(new Proto.Messages.ListChatsRequest
{
    Pagination = new Proto.Shared.PageRequest { Size = DefaultPageSize }, // ← только первые 50
});
var chatsList = response.Chats.ToList(); // теряем остальные чаты
```

**Варианты решения:**  
Добавить параметры пагинации в публичный API и/или реализовать получение всех чатов через цикл.

```csharp
// ✅ РЕШЕНИЕ A: добавить параметры пагинации
public async Task<(ErrorReturner error, List<Proto.Messages.Chat>? chats)> GetChats(
    GlobalParam globalParam,
    int offset = 0,
    int size = 50)
{
    try
    {
        return await _webApi.TokenManager.SafeCallAsync(async () =>
        {
            var response = await MessagesAC!.ListChatsAsync(new Proto.Messages.ListChatsRequest
            {
                Pagination = new Proto.Shared.PageRequest { Offset = offset, Size = size },
            });

            return (new ErrorReturner(true), response.Chats.ToList());
        }, globalParam);
    }
    // ...
}

// ✅ РЕШЕНИЕ B: получить все чаты через цикл
public async Task<(ErrorReturner error, List<Proto.Messages.Chat>? chats)> GetAllChats(GlobalParam globalParam)
{
    var allChats = new List<Proto.Messages.Chat>();
    int offset = 0;
    const int pageSize = 50;

    while (true)
    {
        var (error, page) = await GetChats(globalParam, offset, pageSize);
        if (!error.IsSuccess || page == null || page.Count == 0)
            break;

        allChats.AddRange(page);
        if (page.Count < pageSize) break; // последняя страница

        offset += pageSize;
    }

    return (new ErrorReturner(true), allChats);
}
```

---

## Оптимизация и производительность

---

### PERF-01 — Статический `HttpClient` без `IHttpClientFactory` — риск Socket Exhaustion

**Описание:**  
`WebApiFileManager` использует статический `HttpClient`, созданный как `static readonly`. Хотя статический `HttpClient` лучше, чем создание нового на каждый запрос, у него есть критическая проблема: DNS-записи кэшируются навсегда (до рестарта приложения). Кроме того, тайм-аут в 5 минут ничем не подкреплён — при зависании сервера поток будет заблокирован на 5 минут.

**Конкретно в чём проблема:**  
- Отсутствие `SocketsHttpHandler` с настройкой `PooledConnectionLifetime` → DNS-запись никогда не обновляется  
- Нет retry-политики при временных сбоях сети  
- Нет ограничения параллельных соединений

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiFileManager.cs` : строки 10–11

```csharp
// ❌ ПРОБЛЕМА: HttpClient без настройки PooledConnectionLifetime
private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromMinutes(5);
private static readonly HttpClient _httpClient = new HttpClient { Timeout = DefaultHttpTimeout };
```

**Варианты решения:**  

```csharp
// ✅ РЕШЕНИЕ: HttpClient с правильными настройками SocketsHttpHandler
private static readonly HttpClient _httpClient = new HttpClient(
    new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15), // DNS обновляется каждые 15 мин
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 10,
    })
{
    Timeout = TimeSpan.FromMinutes(5)
};
```

---

### PERF-02 — `UpdateManagerClients` вызывает `SetClients` с 16 параметрами для каждого из 11 менеджеров

**Описание:**  
При каждом обновлении токена `UpdateManagerClients` вызывает `SetClients(...)` 11 раз, передавая в каждый вызов 16 параметров (8 клиентов + 8 каналов). Это 176 передач ссылок на каждый refresh. Помимо накладных расходов это сигнализирует о глубокой архитектурной проблеме — каждый менеджер хранит собственные копии всех клиентов, хотя достаточно было бы одной общей ссылки на `WebApi`.

**Конкретно в чём проблема:**  
- Высокая связность: добавление нового gRPC-сервиса требует правки всех 11 вызовов `SetClients`  
- 11 одинаковых копий ссылок на одни и те же объекты в памяти

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiClientManager.cs` : строки 221–265

```csharp
// ❌ ПРОБЛЕМА: 11 вызовов SetClients по 16 параметров каждый при каждом обновлении токена
_webApi.ServerManager.SetClients(
    _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC,
    _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC, _webApi.OnlinerAC,
    _webApi.BeaconChannel, _webApi.UserChannel, /* ... ещё 6 параметров */);
// ... повторяется ещё 10 раз ...
```

**Варианты решения:**  
Менеджеры уже хранят `_webApi` — они могут обращаться к клиентам напрямую через `_webApi.UsersAC` вместо локальных копий. `SetClients` / `WebApiBase` можно упразднить.

```csharp
// ✅ РЕШЕНИЕ: менеджеры используют WebApi напрямую — не нужны локальные копии клиентов
internal abstract class WebApiBase
{
    protected readonly WebApi _webApi;

    protected WebApiBase(WebApi webApi)
    {
        _webApi = webApi;
    }

    // Свойства-прокси для удобного доступа — без хранения отдельных копий
    protected BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC => _webApi.UsersAC;
    protected BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC => _webApi.IdentityAC;
    // ... остальные свойства ...
    // SetClients больше не нужен — удалить
}
```

---

### PERF-03 — Дублирование кода маппинга сообщений в `GetMessages` и `GetMessagesWithOffset`

**Описание:**  
Оба метода содержат идентичный блок LINQ-маппинга из gRPC-модели `Message` в `MessageModel` (~20 строк). При изменении структуры `MessageModel` необходимо синхронно менять два места, что неизбежно приводит к расхождениям.

**Конкретно в чём проблема:**  
Нарушение DRY — код продублирован дважды, риск рассинхронизации при доработке.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiMessageManager.cs` : строки 238–259, 293–314

```csharp
// ❌ ПРОБЛЕМА: одинаковый маппинг скопирован в двух методах
return (new ErrorReturner(true), response.Messages.Select(m => new MessageModel
{
    MessageId = m.Id,
    ChatId = chatId,
    Text = m.Content.Text,
    Attachments = m.Content.Attachments.Select(a => new AttachmentsModel { ... }).ToList(),
    SenderId = m.SenderId,
    SentAt = m.SentAt,
    Type = m.Type,
    ReadBy = m.ReadBy.ToList(),
}).ToList()); // ← один и тот же код в GetMessages и GetMessagesWithOffset
```

**Варианты решения:**  

```csharp
// ✅ РЕШЕНИЕ: выделить приватный метод маппинга
private static MessageModel MapMessage(Proto.Messages.Message m, string chatId) => new()
{
    MessageId = m.Id,
    ChatId = chatId,
    Text = m.Content.Text,
    Attachments = m.Content.Attachments.Select(a => new AttachmentsModel
    {
        Id = a.Id,
        Type = a.Type,
        PreviewUrl = a.PreviewUrl,
        FileId = a.FileId,
        PreviewFileId = a.PreviewFileId,
        FileName = a.FileName,
        Size = a.AttachmentSize,
        ImageWidth = a.ImageWidth,
        ImageHeight = a.ImageHeight,
    }).ToList(),
    SenderId = m.SenderId,
    SentAt = m.SentAt,
    Type = m.Type,
    ReadBy = m.ReadBy.ToList(),
};

// Использование в GetMessages и GetMessagesWithOffset:
return (new ErrorReturner(true), response.Messages.Select(m => MapMessage(m, chatId)).ToList());
```

---

### PERF-04 — `ImageProcessor.ConvertToJpegAsync` использует `System.Drawing.Common` для JPEG на Windows

**Описание:**  
Для `.jpg`/`.jpeg` файлов используется `System.Drawing.Common` (Windows-only GDI+), тогда как `ImageSharp` уже подключён и поддерживает все форматы включая JPEG. GDI+ создаёт внутренние GDI-объекты, держит хэндлы, работает только на Windows. Разделение на два бэкенда без реальной необходимости усложняет код.

**Конкретно в чём проблема:**  
- `System.Drawing.Common` на Windows медленнее ImageSharp для больших файлов  
- Двойная зависимость без обоснования

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\ImageProcessor.cs` : строки 77–84

```csharp
// ❌ ПРОБЛЕМА: для JPEG используется System.Drawing, хотя ImageSharp уже есть
if (extension == ".webp" || extension == ".png" || extension == ".bmp")
{
    return await ConvertToJpegWithImageSharpAsync(sourcePath, outputPath, quality);
}
// Для .jpg/.jpeg — System.Drawing.Common (Windows-only, медленнее)
return await ConvertToJpegWithSystemDrawingAsync(sourcePath, outputPath, quality);
```

**Варианты решения:**  
Использовать только ImageSharp для всех форматов.

```csharp
// ✅ РЕШЕНИЕ: единый бэкенд — ImageSharp для всех форматов
public static async Task<bool> ConvertToJpegAsync(string sourcePath, string outputPath, int quality = JPEG_QUALITY)
{
    try
    {
        var fileInfo = new FileInfo(sourcePath);
        if (fileInfo.Length > MAX_IMAGE_SIZE_BYTES)
        {
            System.Diagnostics.Debug.WriteLine($"ImageProcessor: Image too large ({fileInfo.Length} bytes)");
            return false;
        }

        // ← ImageSharp для всех форматов: WebP, PNG, BMP, JPEG и др.
        return await ConvertToJpegWithImageSharpAsync(sourcePath, outputPath, quality);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"ImageProcessor: Failed to convert image: {ex.Message}");
        return false;
    }
}
// ConvertToJpegWithSystemDrawingAsync можно удалить
```

---

## Архитектура и прочее

---

### ARC-01 — `ErrorReturner.ErrorCode` без типизации — магические числа

**Описание:**  
`ErrorReturner` имеет поле `ErrorCode` типа `int`, где единственный задокументированный код — `1` (список сообщений пуст). Во всём проекте нет перечисления для этих кодов. Вызывающий код вынужден использовать магические числа при проверке (`if (result.ErrorCode == 1)`), а добавление новых кодов не имеет централизованного места.

**Конкретно в чём проблема:**  
Нет типобезопасности, нет discoverability, нет единого реестра кодов ошибок.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\ErrorReturner.cs` : строки 1–17

```csharp
// ❌ ПРОБЛЕМА: ErrorCode — int без enum
public class ErrorReturner
{
    public int ErrorCode { get; set; } = 0;
    /// <summary>
    /// 1 - получаемый список сообщений в чате пустой  ← единственный задокументированный код
    /// </summary>
}
```

**Варианты решения:**  

```csharp
// ✅ РЕШЕНИЕ: enum для кодов ошибок
public enum ApiErrorCode
{
    None = 0,
    /// <summary>Список сообщений в чате пуст</summary>
    MessagesListEmpty = 1,
    /// <summary>Недостаточно места в хранилище</summary>
    StorageInsufficient = 2,
    /// <summary>Файл не найден</summary>
    FileNotFound = 3,
    /// <summary>Токен истёк и не может быть обновлён</summary>
    TokenInvalidated = 4,
}

public class ErrorReturner
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public ApiErrorCode ErrorCode { get; set; } = ApiErrorCode.None; // ← типизировано

    public ErrorReturner(bool isSuccess, string? errorMessage = null, ApiErrorCode errorCode = ApiErrorCode.None)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }
}
```

---

### ARC-02 — `WebApiBase.SetClients` не включает `FastAuthAC` — несогласованность архитектуры

**Описание:**  
`WebApiBase.SetClients` принимает 8 клиентов и 8 каналов, но `FastAuthAC` / `FastAuthChannel` в сигнатуру не включены. Это нарушает единообразие — все остальные клиенты управляются через `SetClients`, а `FastAuth` — через отдельные методы `CreateFastAuthClient`/`DisposeFastAuthClient`. Добавление нового сервиса в будущем потребует правки `SetClients` во всех 11 менеджерах.

**Конкретно в чём проблема:**  
Архитектурная несогласованность — два механизма управления клиентами вместо одного.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\WebApiBase.cs` : строки 36–71

```csharp
// ❌ ПРОБЛЕМА: SetClients не содержит FastAuthAC — он управляется отдельным механизмом
internal void SetClients(
    UsersApiClient? usersAC,
    BeaconApiClient? beaconAC,
    // ... 6 других клиентов ...
    // ← FastAuthApiClient отсутствует
    GrpcChannel? beaconChannel,
    // ... 7 других каналов ...
    // ← FastAuthChannel отсутствует
)
```

**Варианты решения:**  
Долгосрочно — перейти к паттерну с одной общей ссылкой на `WebApi` вместо копирования (см. PERF-02). Краткосрочно — включить `FastAuthAC` в `SetClients`.

---

### ARC-03 — Глобальное подавление исключений без логирования в продакшн-среде

**Описание:**  
Большинство `catch (Exception)` блоков во всех менеджерах используют `System.Diagnostics.Debug.WriteLine(...)` или вовсе ничего не логируют. `Debug.WriteLine` работает **только в режиме Debug** — в Release-сборке и в продакшне эти сообщения исчезают. При возникновении ошибки в производственной среде у разработчика не будет никакого способа диагностировать проблему.

**Конкретно в чём проблема:**  
Полное отсутствие наблюдаемости (observability) в production-сборках.

**Путь к файлу:** Все файлы в `Windows\BarkFluff.WebApi.Core\Managers\` — системная проблема

```csharp
// ❌ ПРОБЛЕМА: Debug.WriteLine невидим в Release, catch (Exception) без логирования
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"WebApi: Failed: {ex.Message}"); // ← только в Debug
    return (new ErrorReturner(false, "Ошибка"), null);
}

// Или хуже — без каких-либо логов:
catch (Exception)
{
    return new ErrorReturner(false, "Ошибка изменения биографии"); // ← полная тишина
}
```

**Варианты решения:**  
Внедрить `ILogger` или централизованный логгер. Минимально — использовать `Trace` вместо `Debug`.

```csharp
// ✅ РЕШЕНИЕ A: использовать Microsoft.Extensions.Logging
internal class WebApiUserManager : WebApiBase
{
    private readonly WebApi _webApi;
    private readonly ILogger<WebApiUserManager>? _logger; // ← внедрить через DI или фабрику

    public WebApiUserManager(WebApi webApi, ILogger<WebApiUserManager>? logger = null) : base(webApi)
    {
        _webApi = webApi;
        _logger = logger;
    }

    public async Task<ErrorReturner> ChangeBio(string bio, GlobalParam globalParam)
    {
        try { /* ... */ }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ChangeBio failed for user"); // ← видно в продакшне
            return new ErrorReturner(false, "Ошибка изменения биографии");
        }
    }
}

// ✅ РЕШЕНИЕ B (минимальное): Trace вместо Debug — работает в Release
catch (Exception ex)
{
    System.Diagnostics.Trace.TraceError($"WebApi.ChangeBio error: {ex}");
    return new ErrorReturner(false, "Ошибка изменения биографии");
}
```

---

## Сводная таблица

| ID | Категория | Критичность | Файл | Краткое описание |
|----|-----------|-------------|------|-----------------|
| SEC-01 | 🔴 Безопасность | Высокая | `WebApi.cs` | `EnsureHttpPrefix` дефолт `http://` вместо `https://` |
| SEC-02 | 🔴 Безопасность | Средняя | `WebApiUpdateManager.cs`, `WebApiOnlinerManager.cs` | Двойная инъекция Bearer-токена в streaming |
| SEC-03 | 🔴 Безопасность | Высокая | `WebApiClientManager.cs` | Старые gRPC-каналы не закрываются при reinit |
| SEC-04 | 🔴 Безопасность | Средняя | `GlobalParam.cs` | Нет проверки длины файла при Load, нет null-guard |
| BUG-01 | 🟠 Баг | Критическая | `WebApiFileManager.cs` | `UploadUserAvatarAsync` всегда возвращает `true` |
| BUG-02 | 🟠 Баг | Высокая | `WebApiMessageManager.cs` | `CreateChat` не передаёт `userId`, метод нерабочий |
| BUG-03 | 🟠 Баг | Средняя | `WebApiClientManager.cs` | `FastAuthManager` не включён в `UpdateManagerClients` |
| BUG-04 | 🟠 Баг | Высокая | `WebApiUpdateManager.cs`, `WebApiOnlinerManager.cs` | Стримы нельзя отменить (`CancellationToken.None`) |
| BUG-05 | 🟠 Баг | Средняя | `WebApiMessageManager.cs` | `GetChats` теряет чаты сверх лимита 50 |
| PERF-01 | 🟡 Оптимизация | Средняя | `WebApiFileManager.cs` | `HttpClient` без `PooledConnectionLifetime` |
| PERF-02 | 🟡 Оптимизация | Средняя | `WebApiClientManager.cs` | 11×16 параметров в `UpdateManagerClients` |
| PERF-03 | 🟡 Оптимизация | Низкая | `WebApiMessageManager.cs` | Дублирование кода маппинга сообщений |
| PERF-04 | 🟡 Оптимизация | Низкая | `ImageProcessor.cs` | Лишний `System.Drawing.Common` при наличии ImageSharp |
| ARC-01 | 🔵 Архитектура | Низкая | `ErrorReturner.cs` | `ErrorCode` — `int` без `enum` |
| ARC-02 | 🔵 Архитектура | Низкая | `WebApiBase.cs` | `FastAuthAC` не в `SetClients` — несогласованность |
| ARC-03 | 🔵 Архитектура | Высокая | Все менеджеры | `Debug.WriteLine` — нет логов в production |
