# BarkFluff.WebApi.Core

gRPC-клиентская библиотека для WPF-клиента. .NET 10, windows10.0.26100.

Расположение: `Windows/BarkFluff.WebApi.Core/`

> Полная карта файлов и внутреннего строения: [[Windows-WebApiCore-ProjectMap]]

## Сборка

```bash
dotnet build Windows/BarkFluff.WebApi.Core/BarkFluff.WebApi.Core.csproj
```

## Архитектура: Manager-based Facade

`WebApi` — единая точка входа, делегирует специализированным менеджерам (`WebApiBase`):

```
WebApi (IDisposable, фасад)
├── WebApiClientManager      — gRPC каналы/клиенты + interceptors
├── WebApiTokenManager       — рефреш токенов + SafeCallAsync<T>
├── WebApiUserManager        — профиль, устройства, сессии, приватность, персонализация (постер, фоны)
├── WebApiAuthManager        — 2FA/OTP
├── WebApiRegistrationManager — регистрация
├── WebApiPasswordManager    — сброс/смена пароля
├── WebApiMessageManager     — сообщения, чаты, вложения
├── WebApiSearchManager      — поиск
├── WebApiFileManager        — загрузка/скачивание (singleton HttpClient)
├── WebApiServerManager      — информация о серверах
├── WebApiUpdateManager      — real-time streaming (IAsyncEnumerable)
├── WebApiOnlinerManager     — онлайн-статусы
├── WebApiFastAuthManager    — QR-вход (анонимный и авторизованный каналы)
├── WebApiChatFolderManager  — папки чатов
├── WebApiCallsManager       — сигнализация звонков LiveKit
├── WebApiPrivateChatManager — E2E-чаты через passphrase
└── WebApiSecretChatManager  — транспорт секретных чатов (без libsignal)
```

## Ключевые классы

| Класс | Описание |
|-------|----------|
| `WebApi` | Фасад gRPC-клиентов; `CallsAC` создаётся только если сервер объявил endpoint звонков, `FastAuthUserAC` используется для авторизованного подтверждения QR-входа |
| `WebApiBase` | Абстрактный базовый класс менеджеров; содержит общий `ReadStream<T>` для безопасного чтения и отмены server-streaming RPC |
| `GlobalParam` | Состояние (токены, URL, профиль), включая `SocketCalls`, `LivekitUrl`, `ServerDnsName`, `FederationEnabled`; AES-256-GCM; KDF PBKDF2-SHA512 × 600k (формат BFV3); чтение legacy BFV2 (PBKDF2-SHA256 × 100k) для миграции |
| `ErrorReturner` | `(bool IsSuccess, string? ErrorMessage, int ErrorCode, string? ErrorResourceKey)`; Identity rate limit, lockout и временная недоступность защиты возвращают локализационный ключ |
| `ForwardingLetter` | Исходящее сообщение: `Text`, `FilesId`, `ForwardedMessageId` (0 = обычная отправка). Одно и то же поле обслуживает и ответ, и пересылку — как в веб-клиенте |
| `MessageModel` | Сообщение чата, включая `IsEdited` / `EditedAt` |
| `AttachmentsModel` | Вложение; для типа `ForwardedMessage` заполнен `ForwardedMessage` |
| `ForwardedMessageModel` | Содержимое пересланного сообщения: `AuthorName`, `OriginalMessageId`, `Text`, `Attachments` (без вложенной пересылки — сервер рекурсию не присылает) |
| `ImageProcessor` | JPEG, WebP, resize через SixLabors.ImageSharp |

**События `WebApi`:**

| Событие | Когда | Действие клиента |
|---------|-------|------------------|
| `TokenInvalidated` | refresh-токен мёртв | перенаправить на авторизацию |
| `TokenRefreshed` | access-токен проактивно обновлён | пересоздать все gRPC-стримы |

## Паттерн добавления API-метода

```csharp
public async Task<ErrorReturner> MethodName(params..., GlobalParam globalParam)
{
    try
    {
        return await _webApi.TokenManager.SafeCallAsync(async () =>
        {
            await XxxAC!.XxxAsync(new Proto.Xxx.XxxRequest { ... });
            return new ErrorReturner(true);
        }, globalParam);
    }
    catch (BarkFluff.Shared.Exceptions.Xxx.SpecificException)
    {
        return new ErrorReturner(false, "Сообщение");
    }
}
```

`SafeCallAsync<T>` — автоматический рефреш токена при ошибке авторизации.

Первичный обмен refresh-токена после подтверждения регистрации выполняется ещё
без access-токена. `WebApiTokenManager` трактует отсутствующий access-токен как
пустую строку при проверке параллельного refresh и не должен считать такой вызов
уже завершённым: успешный результат обязан сопровождаться заполненным
`GlobalParam.AccessToken`.

## Interceptors

В `WebApiClientManager` подключаются interceptors из [[Shared/Auth]]: JWT, device ID, IP, OS, app version в metadata.

Все каналы создаются через общую фабрику `BuildInvoker(channel, Interceptor[])` — она проходит по массиву интерсепторов в порядке объявления и собирает `CallInvoker`. Для анонимного FastAuth используется короткий набор без JWT; `FastAuthUserAC` и опциональный `CallsAC` используют полный авторизованный набор.

## Безопасность загрузки файлов

- `WebApiFileManager.UploadFileAsync` использует `SanitizeFileName(name, ext)`: убирает управляющие символы, заменяет всё кроме `\w.-` на `_`, обрезает имя до 100 символов (с сохранением расширения), фолбэкает на `file{ext}` для полностью «съеденных» имён.
- `Debug.WriteLine` в файловом менеджере не печатает реальные пути, имена файлов, S3 upload URL и тела ошибок сервера — это PII/секреты.
- Email/username сравниваются через `ToLowerInvariant()` (а не `ToLower()`) — иначе в турецкой локали `İ → i̇` и сервер видит другой логин.

## Сжатие изображений

`ImageProcessor` для конвертации через ImageSharp использует `JpegEncoder { Quality=90, ColorType=YCbCrRatio420, Interleaved=true }` — субсэмплинг 4:2:0 экономит ~30% размера на типовых аватарках/фото без заметной потери качества.

## Авто-обновление токена (проактивный механизм)

`WebApiTokenManager` содержит фоновый `PeriodicTimer` (тик каждые 30 сек), который следит за временем жизни access-токена.
Когда до истечения остаётся **≤1 минуты** — автоматически вызывает `TokenUpdate`, переинициализирует gRPC-клиентов и стреляет событием `TokenRefreshed`.

**Правило для клиентского кода:**
1. При логине вызвать `webApi.StartAutoRefresh(globalParam)`
2. Подписаться на `webApi.TokenRefreshed` — пересоздать все активные стримы
3. При logout / Dispose окна вызвать `webApi.StopAutoRefresh()`

```csharp
_webApi.StartAutoRefresh(globalParam);
_webApi.TokenRefreshed += async (_, _) =>
{
    _streamsCts?.Cancel();
    _streamsCts = new CancellationTokenSource();
    await ReconnectAllStreamsAsync(_streamsCts.Token);
};
```

> `StopAutoRefresh()` вызывается автоматически в `WebApi.Dispose()`.

## Real-time Streaming

`WebApiUpdateManager`, `WebApiOnlinerManager` и `WebApiCallsManager` возвращают `IAsyncEnumerable` для серверного стриминга. Все они используют `WebApiBase.ReadStream<T>`: вызов освобождается при отмене или досрочном выходе из перечисления.

> ⚠️ Все стримы **необходимо пересоздавать** после получения события `TokenRefreshed` — старый стрим открыт со старым токеном и будет отклонён сервером.

## FastAuth QR-вход

`WebApiFastAuthManager` — наследует `WebApiBase` (`internal class WebApiFastAuthManager : WebApiBase`). Генерация и ожидание QR используют отдельный анонимный канал, а `ScanFastAuth`/`AcceptFastAuth`/`RejectFastAuth` — авторизованный `FastAuthUserAC`.

**Публичные методы `WebApi`:**
- `CreateFastAuthClient(gParam, deviceName, os, appName, appVersion, ip)` — создаёт анонимный gRPC канал к FastAuth с device-info interceptors (без JWT)
- `DisposeFastAuthClient()` — закрывает и обнуляет FastAuth канал/клиент
- `GenerateFastAuthToken(TokenFormat.Qr)` → `(ErrorReturner, GenerateFastAuthTokenResponse?)` — шаг 1: получить QR-код (PNG base64) и `fastAuthId`
- `SubscribeFastAuthResult(fastAuthId, ct)` → `IAsyncEnumerable<FastAuthResult>` — шаг 2: ожидать результата (Accepted / Rejected / Expired)

**Флоу (страница Login):**
1. `Login_Loaded` → `StartFastAuthSessionAsync()` (fire and forget)
2. `CreateFastAuthClient` + `GenerateFastAuthToken(Qr)` → декодируем base64 PNG → `QrCodeImage.Source`
3. `SubscribeFastAuthResult(fastAuthId, ct)` — stream loop
4. `Accepted` → set tokens → `CreateAC()` → `GetUserData()` → `OpenMessengerPage()`
5. `Rejected` / `Expired` → рестарт сессии (новый QR)
6. `Login_Unloaded` → отмена CTS + `DisposeFastAuthClient()`

**GlobalParam:** `SocketFastAuth`, `SocketCalls`, `LivekitUrl`, `ServerDnsName`, `FederationEnabled` заполняются из `GetServerInfoResponse`. `SocketCalls` может быть пуст: это нормальный сервер без сервиса звонков.

**Важно:** enum-значения протобуф в C#: `TokenFormat.Qr` (не `TokenFormatQr`), `FastAuthStatus.Accepted` (не `FastAuthStatusAccepted`).

## Proto

Из `Shared/BarkFluff.Proto/`, подключены как `GrpcServices="Client"` (или `"None"` для shared.proto). Включают `fast_auth_api.proto` и `calls_api.proto`.

## Новые клиентские контракты

- Сообщения: правка/удаление, закрепления, добавление участника, обновление группы; typing и соответствующие Updates-стримы.
- Users: mute, Firebase token, федеративный поиск, папки чатов и подтверждение FastAuth.
- Звонки: `WebApiCallsManager` предоставляет только сигнализацию (`livekit_url`, `access_token`, события и историю); медиа остаётся за LiveKit SDK приложения. `CallsAvailable` сообщает, доступен ли сервис.
- Приватные чаты: `WebApiPrivateChatManager` реализует Argon2id + AES-256-GCM passphrase-совместимую крипту. Ключ `byte[]` библиотека не сохраняет между вызовами.
- Секретные чаты: `WebApiSecretChatManager`, prekey-RPC `WebApiUserManager` и три Updates-стрима передают `byte[]` envelope/bundle без преобразования. Double Ratchet, X3DH и хранилище сессий **не реализованы**: это обязанность приложения с libsignal.

## Зависимости

- `Grpc.Net.Client 2.71.0`
- `Google.Protobuf 3.32.0`
- `Konscious.Security.Cryptography.Argon2 1.3.1` — Argon2id для приватных чатов
- `SixLabors.ImageSharp 3.1.12`
- [[Shared/Auth]], [[Shared/Exceptions]], [[Shared/SecurityUtilities]]
