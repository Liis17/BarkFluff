# Аудит проекта: BarkFluff.Proto

> **Дата:** 2025-05-06
> **Ревьюер:** GitHub Copilot (BarkfluffAgent)
> **Область:** `Shared\BarkFluff.Proto\` — все `.proto`-файлы контрактов gRPC-сервисов
> **Версия proto:** proto3
> **Target Framework:** net9.0

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Оптимизация](#-оптимизация)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Токены доступа передаются в теле gRPC-сообщений (FastAuth)

**Проблема:**
В `FastAuthResult` поля `access_token` и `refresh_token` передаются как обычные `string`-поля внутри стримингового сообщения. Это означает, что токены видны в любом gRPC-логе, трейсе, middleware-слое перехвата или при дебаге трафика — даже если транспорт зашифрован (TLS).

**Файл:** `Shared\BarkFluff.Proto\fast_auth_api.proto` : строки 57–65

```protobuf
message FastAuthResult {
  FastAuthStatus status = 1;
  // ⚠️ Токены передаются в body stream — видны в логах и трейсах
  string access_token = 2;                        // <-- секрет в plain поле
  google.protobuf.Timestamp access_token_expires_at = 3;
  string refresh_token = 4;                       // <-- секрет в plain поле
  google.protobuf.Timestamp refresh_token_expires_at = 5;
}
```

**Варианты решения:**

1. Обернуть токены в отдельный `oneof`-блок `TokenPayload`, чтобы гарантировать, что они присутствуют **только** при статусе `ACCEPTED`, и явно документировать, что эти поля не должны логироваться.
2. Добавить маркер `[Sensitive]` / документационный комментарий `// SENSITIVE: do not log` для всех серверных логгеров.
3. Вместо передачи самих токенов в стриме — передавать одноразовый `exchange_code`, который клиент меняет на токены отдельным вызовом `CreateToken`.

```protobuf
message FastAuthResult {
  FastAuthStatus status = 1;

  // Заполняется ТОЛЬКО при статусе ACCEPTED.
  // SENSITIVE: поля не должны попадать в логи и трейсы.
  oneof payload {
    AcceptedTokenPayload accepted = 6;
  }
}

message AcceptedTokenPayload {
  string access_token = 1;                        // SENSITIVE
  google.protobuf.Timestamp access_token_expires_at = 2;
  string refresh_token = 3;                       // SENSITIVE
  google.protobuf.Timestamp refresh_token_expires_at = 4;
}
```

---

### SEC-02 — `GenerateTestTokenRequest` находится в production-контракте

**Проблема:**
В `identity_api.proto` объявлено сообщение `GenerateTestTokenRequest` / `GenerateTestTokenResponse`. Это тестовый RPC, позволяющий получить токен для **любого** `user_id` без авторизации. Если сервер не закрывает этот метод на уровне middleware/authorization policy — это критическая уязвимость повышения привилегий.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 213–219

```protobuf
message GenerateTestTokenRequest {
  int64 user_id = 1; // ⚠️ любой user_id → любой токен без аутентификации
}

message GenerateTestTokenResponse {
  string token = 1;
}
```

**Варианты решения:**

1. **Удалить** сообщение и реализацию целиком в production-сборке.
2. Вынести в отдельный `developers_api.proto` или `test_api.proto` и защитить на уровне сервиса флагом `IsDevelopmentEnvironment`.
3. Добавить в `IdentityServerApi` с обязательным `xAuth`-токеном и включать только через feature-flag.

```protobuf
// В developers_api.proto — только при ASPNETCORE_ENVIRONMENT = Development
service DevelopersIdentityApi {
  // Только в dev/staging! Генерирует токен для произвольного user_id.
  // Защищён: требует xAuth Dev-токен + Environment = Development.
  rpc GenerateTestToken(GenerateTestTokenRequest) returns (GenerateTestTokenResponse);
}
```

---

### SEC-03 — `ChangeUserPasswordResponse` возвращает `password_hash` клиенту

**Проблема:**
Метод сервера `ChangeUserPassword` (в `UsersServerApi`) возвращает `password_hash` в ответе. Хеш пароля — это чувствительные данные. Возвращать их по gRPC означает, что хеш может попасть в логи вызывающего сервиса, трейсы, или быть сохранён в промежуточном звене.

**Файл:** `Shared\BarkFluff.Proto\users_api.proto` : строки 200–204

```protobuf
message ChangeUserPasswordResponse {
  string password_hash = 1; // ⚠️ хеш пароля не должен покидать сервис Users
}
```

**Варианты решения:**

1. Убрать `password_hash` из ответа. Никакой вызывающей стороне не нужен хеш — это внутренняя деталь реализации сервиса Users.
2. Если вызывающему сервису нужно подтверждение — вернуть `bool success`.

```protobuf
message ChangeUserPasswordResponse {
  // password_hash удалён — внутренняя деталь реализации Users-сервиса
  bool success = 1;
}
```

---

### SEC-04 — `beacon_api.proto` раскрывает внутреннюю топологию сети (host + port всех микросервисов)

**Проблема:**
`GetServerInfoResponse` возвращает `ServiceEndpoint` (host + port) для **всех** внутренних микросервисов. Этот endpoint доступен анонимно (`BeaconApi` не имеет авторизации). Атакующий может получить полную карту внутренней инфраструктуры.

**Файл:** `Shared\BarkFluff.Proto\beacon_api.proto` : строки 20–40

```protobuf
message GetServerInfoResponse {
  // ⚠️ Все поля ниже раскрывают внутренние хосты и порты сервисов
  Service identity = 4;   // содержит host + port
  Service users = 5;
  Service files = 6;
  Service messages = 7;
  Service updates = 8;
  Service onliner = 9;
  Service fast_auth = 10;
}

message Service {
  string name = 1;
  ServiceEndpoint endpoint = 2; // host + port — ⚠️ внутренние адреса
  bool tls_enabled = 3;
  ServiceStatus status = 4;
}
```

**Варианты решения:**

1. Не возвращать `ServiceEndpoint` в публичном Beacon API. Клиенту нужны только `name`, `status`, `tls_enabled`.
2. `ServiceEndpoint` оставить только в серверном/admin API, защищённом xAuth.
3. Ввести два варианта `Service` — публичный и внутренний.

```protobuf
// Публичное представление сервиса — без топологии
message ServicePublicInfo {
  string name = 1;
  bool tls_enabled = 2;
  ServiceStatus status = 3;
  // endpoint — не раскрывается публично
}

message GetServerInfoResponse {
  string name = 1;
  string description = 2;
  ServerColor color = 3;
  ServicePublicInfo identity = 4;
  ServicePublicInfo users = 5;
  ServicePublicInfo files = 6;
  ServicePublicInfo messages = 7;
  ServicePublicInfo updates = 8;
  ServicePublicInfo onliner = 9;
  ServicePublicInfo fast_auth = 10;
}
```

---

### SEC-05 — `ip_address` приходит из клиентских заголовков в `FastAuth` и `CreateSessionForUserServer`

**Проблема:**
В `CreateSessionForUserServerRequest` поле `ip_address` передаётся как аргумент вызывающего сервиса. Если Identity не валидирует и не перезаписывает IP из gRPC peer — возможна подмена IP для обхода geo-блокировок или rate-limit политик.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 74–82

```protobuf
message CreateSessionForUserServerRequest {
  int64 user_id = 1;
  string device_id = 2;
  string device_name = 3;
  string operation_system = 4;
  string app_name = 5;
  string ip_address = 6; // ⚠️ доверяем вызывающему — можно подделать
}
```

**Варианты решения:**

1. В реализации Identity: всегда брать IP из `ServerCallContext.Peer`, а не из поля сообщения.
2. Поле `ip_address` в proto — оставить как опциональное для случаев, когда Identity сам не может определить IP (например, WebApi → Identity цепочка), но документировать это явно.
3. Добавить комментарий-контракт: сервер обязан валидировать или игнорировать это поле.

```protobuf
message CreateSessionForUserServerRequest {
  int64 user_id = 1;
  string device_id = 2;
  string device_name = 3;
  string operation_system = 4;
  string app_name = 5;
  // Передаётся вызывающим сервисом (WebApi), т.к. на уровне gRPC peer
  // виден адрес WebApi, а не оригинального клиента.
  // Identity обязан валидировать формат (IPv4/IPv6), но не должен слепо доверять значению.
  string ip_address = 6;
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — `GetUserAllMessages` возвращает все сообщения разом без пагинации/стриминга

**Проблема:**
Метод экспорта `MessagesServerApi.GetUserAllMessages` возвращает **все** сообщения пользователя и **все** чаты за одну унарную gRPC-операцию. При большом числе сообщений (тысячи/десятки тысяч) это приводит к огромным сообщениям gRPC (дефолтный лимит 4 MB), Out of Memory на сервере при сборке ответа и длительному ожиданию клиента.

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 214–222

```protobuf
// ⚠️ Унарный вызов, возвращает всё одним куском — OOM-риск
rpc GetUserAllMessages(GetUserAllMessagesRequest) returns(GetUserAllMessagesResponse);

message GetUserAllMessagesResponse {
  repeated ExportMessage messages = 1; // может быть 100k+ сообщений
  repeated ExportChat chats = 2;
}
```

**Варианты решения:**

1. Заменить на server-side streaming — сервер стримит батчами по N сообщений.
2. Добавить пагинацию: `page_token` / `offset` + `limit`.

```protobuf
// Вариант 1 — server-side streaming (рекомендуется для экспорта)
rpc GetUserAllMessages(GetUserAllMessagesRequest) returns (stream ExportBatch);

message ExportBatch {
  repeated ExportMessage messages = 1; // батч по 100–500 сообщений
  repeated ExportChat chats = 2;       // возвращаются один раз в первом батче
  bool is_last = 3;                    // признак последнего батча
}

// Вариант 2 — пагинация
message GetUserAllMessagesRequest {
  int64 user_id = 1;
  int32 page_size = 2;    // макс 500
  string page_token = 3;  // cursor-токен следующей страницы
}

message GetUserAllMessagesResponse {
  repeated ExportMessage messages = 1;
  repeated ExportChat chats = 2;
  string next_page_token = 3; // пустой — данных больше нет
}
```

---

### OPT-02 — `MarkAsRead` принимает `repeated int64 message_ids` без ограничения размера

**Проблема:**
`MarkAsReadRequest` содержит `repeated int64 message_ids` без явного максимума. Клиент может послать десятки тысяч ID в одном запросе, что создаёт нагрузку на разбор сообщения и обработку в БД (IN-запрос с огромным числом ID).

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 43–46

```protobuf
message MarkAsReadRequest {
  repeated int64 message_ids = 1; // ⚠️ нет ограничения — можно прислать 100k ID
}
```

**Варианты решения:**

1. Добавить комментарий-контракт о максимуме (например, 500) и валидировать на сервере.
2. Рассмотреть семантику «отметить все до message_id X» как альтернативу списку ID.

```protobuf
message MarkAsReadRequest {
  // Идентификаторы сообщений для пометки как прочитанных.
  // Максимум 500 за один вызов.
  repeated int64 message_ids = 1;

  // Альтернатива: отметить все сообщения в чате до этого ID включительно.
  // Если задан — message_ids игнорируется.
  optional int64 read_up_to_message_id = 2;
  optional string chat_id = 3;
}
```

---

### OPT-03 — `SubscribeToOnlineStatus` — нет механизма дедупликации подписок

**Проблема:**
`SubscribeToOnlineStatusRequest` принимает массив `user_ids`, но если клиент переподключится и снова подпишется на те же IDs — сервер не знает о предыдущей подписке. При этом `ChangeUsersInSubscription` существует отдельно, что означает, что первоначальный список никак не связан с изменениями. Нет гарантии порядка инициализации стрима и первого вызова `ChangeUsersInSubscription`.

**Файл:** `Shared\BarkFluff.Proto\onliner_api.proto` : строки 44–58

```protobuf
// ⚠️ Стрим открывается с начальным списком, но далее управляется отдельным RPC
rpc SubscribeToOnlineStatus(SubscribeToOnlineStatusRequest) returns (stream UserOnlineStatus);
rpc ChangeUsersInSubscription(ChangeUsersInSubscriptionRequest) returns (ChangeUsersInSubscriptionResponse);
```

**Варианты решения:**

1. Сделать `SubscribeToOnlineStatus` **bidirectional streaming**: клиент может слать обновления списка прямо в стриме, сервер отвечает событиями — всё в одном соединении.
2. Документировать явно: первый вызов `ChangeUsersInSubscription` должен быть после установки стрима, порядок гарантирован.

```protobuf
// Вариант — bidi streaming: управление подпиской внутри стрима
rpc ManageOnlineSubscription(stream OnlineSubscriptionCommand) returns (stream UserOnlineStatus);

message OnlineSubscriptionCommand {
  oneof command {
    SetUserIdsCommand set_users = 1;   // заменить весь список
    AddUserIdsCommand add_users = 2;   // добавить пользователей
    RemoveUserIdsCommand remove_users = 3; // удалить пользователей
  }
}

message SetUserIdsCommand { repeated int64 user_ids = 1; }
message AddUserIdsCommand { repeated int64 user_ids = 1; }
message RemoveUserIdsCommand { repeated int64 user_ids = 1; }
```

---

### OPT-04 — `ListMessages` содержит устаревшее поле `count` вместе с новыми `offset_before`/`offset_after`

**Проблема:**
В `ListMessagesRequest` есть поле `count` с пометкой `deprecated` в комментарии, но оно не помечено как `[deprecated = true]` в proto. Это означает, что кодогенератор не предупреждает клиентов об устаревании, а оба механизма пагинации могут работать одновременно с непредсказуемым приоритетом.

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 108–120

```protobuf
message ListMessagesRequest {
  int64 from_message_id = 1;
  int32 count = 2;          // deprecated, use offset_before — но НЕ помечено [deprecated=true]
  string chat_id = 3;
  int32 offset_before = 4;
  int32 offset_after = 5;
}
```

**Варианты решения:**

Пометить поле официальным атрибутом deprecated в proto3 (через опцию или резервирование) и задокументировать поведение приоритета.

```protobuf
message ListMessagesRequest {
  int64 from_message_id = 1;

  // Устарело. Используйте offset_before.
  // При наличии offset_before это поле игнорируется.
  int32 count = 2 [deprecated = true];

  string chat_id = 3;

  // Количество сообщений ПЕРЕД from_message_id (старше), макс 50.
  int32 offset_before = 4;

  // Количество сообщений ПОСЛЕ from_message_id (новее), макс 50.
  int32 offset_after = 5;
}
```

---

### OPT-05 — `GetFilesData` / `GetTempDownloadUrl` — нет лимита на количество файлов в запросе

**Проблема:**
`GetFilesDataRequest` и `GetTempDownloadUrlRequest` содержат `repeated string file_ids` без ограничения. Один запрос с тысячами файловых ID создаёт нагрузку на S3-хранилище и базу данных (N+1 или огромный batch-запрос).

**Файл:** `Shared\BarkFluff.Proto\files_api.proto` : строки 55–68

```protobuf
message GetTempDownloadUrlRequest {
  repeated string file_ids = 1; // ⚠️ нет лимита — можно запросить тысячи URL
}

message GetFilesDataRequest {
  repeated string file_ids = 1; // ⚠️ нет лимита
}
```

**Варианты решения:**

Добавить комментарий-контракт с максимумом и валидацию на сервере.

```protobuf
message GetTempDownloadUrlRequest {
  // Идентификаторы файлов. Максимум 100 за один вызов.
  repeated string file_ids = 1;
}

message GetFilesDataRequest {
  // Идентификаторы файлов. Максимум 100 за один вызов.
  repeated string file_ids = 1;
}
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — `ProfileFieldVisibility.ALL = 0` — нарушение proto3 семантики default value

**Проблема:**
В proto3 нулевое значение enum является **дефолтным** и означает «не задано / неизвестно». Использование `ALL = 0` означает, что **неинициализированное поле** будет автоматически трактоваться как «видно всем». Если клиент забудет явно установить `online_visibility` — он получит `ALL` вместо ожидаемого значения. Это создаёт потенциальную утечку приватности.

**Файл:** `Shared\BarkFluff.Proto\users_api.proto` : строки 574–578

```protobuf
enum ProfileFieldVisibility {
  ALL = 0;     // ⚠️ дефолтное значение proto3 = видно всем — небезопасно
  FRIENDS = 1;
  NONE = 2;
}
```

**Варианты решения:**

Переименовать `0` в `PROFILE_FIELD_VISIBILITY_UNKNOWN` и сдвинуть значения. На уровне сервера применять безопасный дефолт при `UNKNOWN`.

```protobuf
enum ProfileFieldVisibility {
  PROFILE_FIELD_VISIBILITY_UNKNOWN = 0; // дефолт proto3 — сервер применяет безопасный fallback
  ALL = 1;
  FRIENDS = 2;
  NONE = 3;
}

// На уровне сервера:
// if (visibility == UNKNOWN) visibility = NONE; // безопасный дефолт
```

---

### BUG-02 — `OtpTypeId` / `ServiceStatus` / `StatusTypeId` не имеют namespace-префикса в именах значений enum

**Проблема:**
В proto3 имена значений enum находятся в глобальном namespace пакета. Если два enum имеют одинаковые имена значений — возникает конфликт при компиляции. `OtpTypeId` использует `Unknown`, `Authenticator`, `Email` — без префикса. `ServiceStatus` использует `Unknown`, `Healthy` и т.д. При импорте в один файл или при генерации кода это может вызвать конфликты.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 148–151  
**Файл:** `Shared\BarkFluff.Proto\beacon_api.proto` : строки 55–61

```protobuf
// identity_api.proto
enum OtpTypeId {
  Unknown = 0;       // ⚠️ конфликтует с ServiceStatus.Unknown
  Authenticator = 1;
  Email = 2;
}

// beacon_api.proto
enum ServiceStatus {
  Unknown = 0;       // ⚠️ конфликт имён в глобальном пространстве
  Healthy = 1;
  ...
}
```

**Варианты решения:**

Следовать официальному соглашению proto3: значения enum должны иметь префикс имени enum в SCREAMING_SNAKE_CASE.

```protobuf
enum OtpTypeId {
  OTP_TYPE_ID_UNKNOWN = 0;
  OTP_TYPE_ID_AUTHENTICATOR = 1;
  OTP_TYPE_ID_EMAIL = 2;
}

enum ServiceStatus {
  SERVICE_STATUS_UNKNOWN = 0;
  SERVICE_STATUS_HEALTHY = 1;
  SERVICE_STATUS_DEGRADED = 2;
  SERVICE_STATUS_UNHEALTHY = 3;
  SERVICE_STATUS_OFFLINE = 4;
}
```

---

### BUG-03 — `ServerColor` и `ServiceEndpoint` продублированы в `beacon_api.proto` и `navigator_api.proto`

**Проблема:**
Сообщения `ServerColor` и `ServiceEndpoint` объявлены в **двух** разных proto-файлах с разными пакетами (`barkfluff.beacon` и `barkfluff.navigator`). При изменении структуры в одном файле — второй не обновится автоматически. Это приведёт к рассинхрону контрактов.

**Файл 1:** `Shared\BarkFluff.Proto\beacon_api.proto` : строки 42–48  
**Файл 2:** `Shared\BarkFluff.Proto\navigator_api.proto` : строки 22–31

```protobuf
// beacon_api.proto — barkfluff.beacon
message ServerColor {
  string lite_hex = 1;
  string main_hex = 2;
  string hard_hex = 3;
}

// navigator_api.proto — barkfluff.navigator — ⚠️ ДУБЛИКАТ
message ServerColor {
  string lite_hex = 1;
  string main_hex = 2;
  string hard_hex = 3;
}
```

**Варианты решения:**

Вынести общие структуры в `shared.proto` и импортировать из обоих файлов.

```protobuf
// shared.proto — добавить
message ServerColor {
  string lite_hex = 1;
  string main_hex = 2;
  string hard_hex = 3;
}

message ServiceEndpoint {
  string host = 1;
  int32 port = 2;
}

// beacon_api.proto — использовать из shared
import "shared.proto";
// barkfluff.shared.ServerColor вместо локального определения

// navigator_api.proto — аналогично
import "shared.proto";
```

---

### BUG-04 — `ExportAttachment.type` использует `int32` вместо enum

**Проблема:**
В `ExportAttachment` поле `type` объявлено как `int32` с комментарием о значениях. При этом в `MessageAttachment` (в shared.proto) используется `MessageAttachmentType` enum. Это нарушение консистентности — при десериализации экспорта клиент не получает типизацию, а числовое значение может быть некорректным.

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 230–245

```protobuf
message ExportAttachment {
  int64 id = 1;
  int32 type = 2; // ⚠️ тип как int32 вместо MessageAttachmentType enum
  string file_id = 3;
  ...
}
```

**Варианты решения:**

Использовать enum из `shared.proto`.

```protobuf
import "shared.proto";

message ExportAttachment {
  int64 id = 1;
  barkfluff.shared.MessageAttachmentType type = 2; // типизированно, консистентно
  string file_id = 3;
  string preview_url = 4;
  int64 attachment_size = 5;
  string preview_file_id = 6;
  string file_name = 7;
}
```

---

### BUG-05 — `ConfirmAccountResponse` возвращает только `refresh_token`, без `access_token`

**Проблема:**
После подтверждения аккаунта клиент получает только `refresh_token`. Для использования API ему нужно немедленно сделать ещё один вызов `CreateToken`, чтобы получить `access_token`. Это лишний round-trip после регистрации. При этом `AuthResponse` (логин) и `ConfirmResetPasswordResponse` (сброс пароля) возвращают оба токена.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 238–243

```protobuf
message ConfirmAccountResponse {
  Token refresh_token = 1; // ⚠️ нет access_token — лишний round-trip для клиента
}
```

**Варианты решения:**

Возвращать оба токена — по аналогии с `AuthResponse`.

```protobuf
message ConfirmAccountResponse {
  Token access_token = 1;  // сразу готов к использованию
  Token refresh_token = 2;
}
```

---

### BUG-06 — `CreateGroupChatRequest` не имеет ограничений на `user_ids` и `title`

**Проблема:**
`CreateGroupChatRequest` принимает `repeated int64 user_ids` и `string title` без явных ограничений в контракте. Нет минимума участников (можно создать «группу» из 0 человек), нет максимума (10000 участников?), нет ограничения длины заголовка.

**Файл:** `Shared\BarkFluff.Proto\messages_api.proto` : строки 55–64

```protobuf
message CreateGroupChatRequest {
  repeated int64 user_ids = 1; // ⚠️ нет min/max — можно 0 или 100k участников
  string title = 2;            // ⚠️ нет ограничения длины
  string picture_file_id = 3;
}
```

**Варианты решения:**

Добавить документационные комментарии-контракты и валидацию на сервере.

```protobuf
message CreateGroupChatRequest {
  // Идентификаторы участников (без текущего пользователя).
  // Минимум 1, максимум 999 пользователей.
  repeated int64 user_ids = 1;

  // Название группы. Обязательно. Длина: 1–128 символов.
  string title = 2;

  // FileId обложки чата. Необязательно.
  string picture_file_id = 3;
}
```

---

## 🔵 Прочее / Качество кода

---

### MISC-01 — `SendEmailOtpCodeRequest` / `SendEmailOtpCodeResponse` — пустые "заглушки" без RPC

**Проблема:**
В `identity_api.proto` объявлены сообщения `SendEmailOtpCodeRequest` и `SendEmailOtpCodeResponse`, но они не используются ни в одном RPC-методе сервиса. Это "мёртвый код" в контракте, который вводит читателей в заблуждение.

**Файл:** `Shared\BarkFluff.Proto\identity_api.proto` : строки 128–133

```protobuf
// ⚠️ Не используются ни в одном RPC
message SendEmailOtpCodeRequest { }
message SendEmailOtpCodeResponse { }
```

**Варианты решения:**

Либо добавить RPC в `IdentityApi`, либо удалить неиспользуемые сообщения.

```protobuf
// Вариант А — добавить RPC:
service IdentityApi {
  // ... существующие методы ...

  // Отправить OTP-код на email (для верификации или 2FA)
  rpc SendEmailOtpCode(SendEmailOtpCodeRequest) returns(SendEmailOtpCodeResponse);
}

// Вариант Б — удалить оба сообщения если функционал не планируется
```

---

### MISC-02 — `NavigatorApi.RegisterServer` не имеет аутентификации в контракте

**Проблема:**
`RegisterServer` позволяет зарегистрировать произвольный сервер в Navigator. Контракт не указывает способ авторизации — любой желающий теоретически может зарегистрировать поддельный сервер. Комментария об авторизации нет.

**Файл:** `Shared\BarkFluff.Proto\navigator_api.proto` : строки 12–14

```protobuf
service NavigatorApi {
  rpc ListServers(ListServersRequest) returns (ListServersResponse);
  rpc RegisterServer(RegisterServerRequest) returns (RegisterServerResponse); // ⚠️ нет указания на авторизацию
}
```

**Варианты решения:**

Добавить комментарий о требуемом типе авторизации и/или разделить API на публичный и серверный.

```protobuf
service NavigatorApi {
  // Публичный список серверов — без авторизации.
  rpc ListServers(ListServersRequest) returns (ListServersResponse);
}

service NavigatorServerApi {
  // Регистрация сервера. Authorization: xAuth Service-токен.
  rpc RegisterServer(RegisterServerRequest) returns (RegisterServerResponse);
}
```

---

### MISC-03 — `ConfigurationApi` полностью открыт — нет разделения на публичный и серверный

**Проблема:**
`ConfigurationApi` содержит методы `UpdateConfiguration`, `AddReservedName`, `DeleteReservedName` и т.д. — это административные операции. Тем не менее, они находятся в одном сервисе без явного разделения на `ConfigurationApi` (read-only) и `ConfigurationServerApi` (write). Нет комментария об авторизации.

**Файл:** `Shared\BarkFluff.Proto\configuration_api.proto` : строки 9–23

```protobuf
service ConfigurationApi {
  rpc GetConfiguration(GetConfigurationRequest) returns(GetConfigurationResponse);
  rpc UpdateConfiguration(...) returns(...); // ⚠️ мутирующая операция в публичном API
  rpc AddReservedName(...) returns(...);     // ⚠️ административная операция
  rpc DeleteReservedName(...) returns(...);  // ⚠️ административная операция
}
```

**Варианты решения:**

Разделить на публичный (только чтение) и серверный (admin) сервисы.

```protobuf
// Только для внутреннего использования микросервисами (read)
service ConfigurationApi {
  // Authorization: xAuth Service-токен
  rpc GetConfiguration(GetConfigurationRequest) returns(GetConfigurationResponse);
  rpc GetReservedNames(GetReservedNamesRequest) returns(GetReservedNamesResponse);
}

// Только для Admin Panel (read + write)
service ConfigurationAdminApi {
  // Authorization: Admin JWT
  rpc UpdateConfiguration(UpdateConfigurationRequest) returns(UpdateConfigurationResponse);
  rpc AddReservedName(AddReservedNameRequest) returns(AddReservedNameResponse);
  rpc UpdateReservedName(UpdateReservedNameRequest) returns(UpdateReservedNameResponse);
  rpc DeleteReservedName(DeleteReservedNameRequest) returns(DeleteReservedNameResponse);
}
```

---

### MISC-04 — `UpdatesApi` не имеет механизма reconnect / resume стрима

**Проблема:**
`SubscribeNewMessages` и `SubscribeMessagesRead` — server-side streaming без `last_event_id` или cursor. При разрыве соединения клиент не знает, с какого события возобновить получение — возможна потеря сообщений между разрывом и переподключением.

**Файл:** `Shared\BarkFluff.Proto\updates_api.proto` : строки 10–17

```protobuf
service UpdatesApi {
  // ⚠️ Нет last_event_id — при reconnect клиент теряет события
  rpc SubscribeNewMessages(SubscribeNewMessagesRequest) returns (stream NewMessageEvent);
  rpc SubscribeMessagesRead(SubscribeMessagesReadRequest) returns(stream MessageReadEvent);
}

message SubscribeNewMessagesRequest { } // ⚠️ пустой — нет cursor
```

**Варианты решения:**

Добавить `last_message_id` / `since_timestamp` в request для возобновления с последнего известного события.

```protobuf
message SubscribeNewMessagesRequest {
  // Последний известный клиенту message_id.
  // Сервер отправит все события ПОСЛЕ этого ID при переподключении.
  // 0 = начать с текущего момента (новые события only).
  int64 last_known_message_id = 1;
}

message SubscribeMessagesReadRequest {
  // Timestamp последнего известного события чтения.
  // Позволяет восстановить пропущенные события после reconnect.
  google.protobuf.Timestamp since = 1;
}
```

---

### MISC-05 — `PageRequest` использует `offset`-пагинацию вместо cursor-based

**Проблема:**
`PageRequest` (shared.proto) использует `offset + size` — классическую offset-пагинацию. При большом числе записей `OFFSET N` в SQL работает медленно (сканируется N строк), а при вставке новых данных между страницами пользователь может получить дубликаты или пропустить записи.

**Файл:** `Shared\BarkFluff.Proto\shared.proto` : строки 9–16

```protobuf
message PageRequest {
  int32 offset = 1; // ⚠️ offset-пагинация — медленна на больших данных
  int32 size = 2;
}
```

**Варианты решения:**

Для высоконагруженных эндпоинтов (чаты, сообщения, пользователи) перейти на cursor-based пагинацию. `PageRequest` оставить для совместимости, но добавить `cursor`-вариант.

```protobuf
// Cursor-based пагинация (рекомендуется для больших коллекций)
message CursorPageRequest {
  // Непрозрачный курсор следующей страницы (из предыдущего ответа).
  // Пустой = первая страница.
  string cursor = 1;
  int32 size = 2; // макс 50
}

message CursorPageResponse {
  string next_cursor = 1; // пустой — данных больше нет
  int32 total_count = 2;  // только если подсчёт не дорог
}
```

---

*Документ сгенерирован на основе полного статического анализа всех `.proto`-файлов проекта `BarkFluff.Proto`.*
*Всего выявлено: **5 проблем безопасности**, **5 проблем оптимизации**, **6 багов/недоработок**, **5 прочих замечаний качества кода**.*
