# Этап 0.4 — Proto-контракты федерации

## Цель

Завести все proto-контракты федерации и расширить существующие. **Только контракты**: новые RPC нигде не реализуются (gRPC-base отдаёт `Unimplemented` — это нормально до Фаз 1–4). Существующие клиенты не ломаются: только добавление полей/RPC, никаких изменений существующих номеров/типов/имён.

Все файлы — в `Shared/BarkFluff.Proto/`. После правок проверь `BarkFluff.Proto.csproj`: если он включает все .proto (посмотри, как подключены существующие) — добавь туда новые два файла тем же способом, чтобы codegen валидировал синтаксис при сборке.

Контекст контрактов: [../04-federation-service.md](../04-federation-service.md) (API-поверхности), [../05-chat-replication.md](../05-chat-replication.md) (события), [../06-files.md](../06-files.md) (FederatedFileRef).

---

## 1. Новый файл `federation_api.proto` — S2S API (нода ↔ нода)

Создать целиком (это черновик v1; при конфликте имён с существующими типами — префиксуй, не меняй чужое):

```protobuf
syntax = "proto3";

option csharp_namespace = "BarkFluff.Proto.Federation";

import "google/protobuf/timestamp.proto";

package barkfluff.federation;

// S2S API федерации. Авторизация — НЕ XAuth: Ed25519-подпись запросов
// (заголовки x-bf-origin/x-bf-destination/x-bf-timestamp/x-bf-key-id/x-bf-signature).
// Реализация — Фаза 1+. Все идентификаторы пользователей — только UUID/FID,
// long-ID через границу сервера не ходят.
service FederationS2SApi {

  // Handshake: версия протокола, имя сервера, серверное время (диагностика clock skew)
  rpc Ping(PingRequest) returns (PingResponse);

  // Текущие публичные signing-ключи ноды (второй канал к /.well-known/barkfluff)
  rpc GetServerKeys(GetServerKeysRequest) returns (GetServerKeysResponse);

  // Профиль пользователя этой ноды (с учётом privacy)
  rpc GetUserProfile(GetUserProfileRequest) returns (GetUserProfileResponse);

  // Основной канал доставки: батч федеративных событий. Идемпотентность по event_id
  rpc DeliverEvents(DeliverEventsRequest) returns (DeliverEventsResponse);

  // Catch-up: события чата после указанного курсора (для догона после даунтайма)
  rpc FetchChatHistory(FetchChatHistoryRequest) returns (FetchChatHistoryResponse);

  // Отдача файла со своего S3 ноде-партнёру (стриминг чанками)
  rpc FetchFile(FetchFileRequest) returns (stream FetchFileChunk);

  // Стрим онлайн-статусов пользователей этой ноды (агрегированный, один на пару нод)
  rpc SubscribePresence(SubscribePresenceRequest) returns (stream PresenceEvent);

  // Typing-индикатор (fire-and-forget, без ретраев)
  rpc DeliverTyping(DeliverTypingRequest) returns (DeliverTypingResponse);
}

message PingRequest {
  string origin_server = 1;
  repeated int32 protocol_versions = 2; // поддерживаемые версии протокола
}

message PingResponse {
  string server_name = 1;
  repeated int32 protocol_versions = 2;
  google.protobuf.Timestamp server_time = 3; // для диагностики рассинхрона часов
  repeated string capabilities = 4;          // напр. "presence", "typing"
}

message GetServerKeysRequest { }

message SigningKey {
  string key_id = 1;                          // напр. "ed25519:1"
  bytes public_key = 2;                       // raw 32 байта Ed25519
  google.protobuf.Timestamp expired_at = 3;   // пусто = активен
}

message GetServerKeysResponse {
  string server_name = 1;
  repeated SigningKey keys = 2;
}

message GetUserProfileRequest {
  oneof user {
    string username = 1; // резолв по имени (FID без @ и :server)
    string uuid = 2;     // резолв по UUID
  }
}

message GetUserProfileResponse {
  bool found = 1;
  string uuid = 2;
  string username = 3;
  string first_name = 4;
  string last_name = 5;
  string bio = 6;                       // пусто, если скрыто privacy
  FederatedFileRef avatar = 7;          // пусто, если скрыто privacy
}

// Ссылка на файл, живущий на origin-сервере (файлы не реплицируются)
message FederatedFileRef {
  string origin_server = 1;
  string file_id = 2;
  string filename = 3;
  int64 size_bytes = 4;
  int32 attachment_type = 5;   // значения barkfluff.shared.MessageAttachmentType
  string preview_file_id = 6;
  int32 image_width = 7;
  int32 image_height = 8;
}

// ---------- События ----------

message FederationEvent {
  string event_id = 1;                       // uuid, идемпотентность на приёмнике
  string origin_server = 2;                  // обязан совпадать с x-bf-origin
  int64 origin_ts_ms = 3;                    // unix ms UTC по часам origin — база LWW
  oneof payload {
    ChatCreatedPayload chat_created = 10;
    NewMessagePayload new_message = 11;
    MessageEditedPayload message_edited = 12;
    MessageDeletedPayload message_deleted = 13;
    MessagesReadPayload messages_read = 14;
    UserProfileChangedPayload profile_changed = 15;
    UserDeactivatedPayload user_deactivated = 16;
  }
}

message FederatedUser {
  string uuid = 1;
  string username = 2;
  string server_name = 3;
}

message ChatCreatedPayload {
  string chat_id = 1;                  // Guid — общий для копий на обеих нодах
  FederatedUser initiator = 2;
  FederatedUser invitee = 3;           // пользователь ноды-получателя
}

message NewMessagePayload {
  string chat_id = 1;
  string federated_message_id = 2;     // uuid
  FederatedUser sender = 3;
  string text = 4;
  repeated FederatedFileRef attachments = 5;
  google.protobuf.Timestamp sent_at = 6;
}

message MessageEditedPayload {
  string chat_id = 1;
  string federated_message_id = 2;
  string new_text = 3;
  repeated FederatedFileRef attachments = 4;
}

message MessageDeletedPayload {
  string chat_id = 1;
  string federated_message_id = 2;
}

message MessagesReadPayload {
  string chat_id = 1;
  string reader_uuid = 2;
  string up_to_federated_message_id = 3; // «прочитано до» включительно
}

message UserProfileChangedPayload {
  FederatedUser user = 1;
  string first_name = 2;
  string last_name = 3;
  string bio = 4;
  FederatedFileRef avatar = 5;
}

message UserDeactivatedPayload {
  string user_uuid = 1;
}

message DeliverEventsRequest {
  repeated FederationEvent events = 1; // лимит размера батча — прикладной, Фаза 2
}

message EventResult {
  string event_id = 1;
  EventStatus status = 2;
  string error_code = 3;               // напр. "FederatedDmRejected"
}

enum EventStatus {
  EVENT_STATUS_UNKNOWN = 0;
  EVENT_STATUS_OK = 1;
  EVENT_STATUS_ALREADY_PROCESSED = 2;
  EVENT_STATUS_REJECTED = 3;           // перманентный отказ — не ретраить
  EVENT_STATUS_RETRY = 4;              // временная ошибка — ретраить
}

message DeliverEventsResponse {
  repeated EventResult results = 1;
}

message FetchChatHistoryRequest {
  string chat_id = 1;
  int64 since_ts_ms = 2;               // отдать события с LastChangeAt > since
  int32 limit = 3;
}

message FetchChatHistoryResponse {
  repeated FederationEvent events = 1; // актуальное состояние, не журнал правок
  bool has_more = 2;
}

message FetchFileRequest {
  string file_id = 1;
  int64 range_from = 2;                // 0 = с начала
  int64 range_to = 3;                  // 0 = до конца
}

message FetchFileChunk {
  bytes data = 1;
  int64 total_size = 2;                // в первом чанке
  string content_type = 3;             // в первом чанке
}

message SubscribePresenceRequest {
  repeated string user_uuids = 1;      // пользователи ЭТОЙ ноды, интересующие подписчика
}

message PresenceEvent {
  string user_uuid = 1;
  PresenceStatus status = 2;
  google.protobuf.Timestamp last_seen = 3;
}

enum PresenceStatus {
  PRESENCE_STATUS_UNKNOWN = 0;         // = скрыт privacy
  PRESENCE_STATUS_ONLINE = 1;
  PRESENCE_STATUS_OFFLINE = 2;
}

message DeliverTypingRequest {
  string chat_id = 1;
  string sender_uuid = 2;
  int32 action = 3;                    // значения barkfluff.onliner.TypingAction
}

message DeliverTypingResponse { }
```

## 2. Новый файл `federation_internal_api.proto` — internal API (свои сервисы → Federation)

```protobuf
syntax = "proto3";

option csharp_namespace = "BarkFluff.Proto.FederationInternal";

import "google/protobuf/timestamp.proto";
import "federation_api.proto";

package barkfluff.federation_internal;

// Внутренний API Federation-сервиса. Авторизация — XAuth, TokenType.Service.
// Реализация — Фаза 1+.
service FederationInternalApi {

  // Резолв remote-пользователя по FID или UUID (discovery + S2S GetUserProfile)
  rpc ResolveRemoteUser(ResolveRemoteUserRequest) returns (ResolveRemoteUserResponse);

  // Стриминг файла с origin-ноды (для Files)
  rpc FetchRemoteFile(FetchRemoteFileRequest) returns (stream barkfluff.federation.FetchFileChunk);

  // Catch-up истории чата с ноды-партнёра (для Messages)
  rpc FetchRemoteChatHistory(FetchRemoteChatHistoryRequest) returns (barkfluff.federation.FetchChatHistoryResponse);

  // ---- управление пирами (AdminPanel) ----
  rpc GetKnownServers(GetKnownServersRequest) returns (GetKnownServersResponse);
  rpc UpsertManualPeer(UpsertManualPeerRequest) returns (UpsertManualPeerResponse);
  rpc SetServerBlocked(SetServerBlockedRequest) returns (SetServerBlockedResponse);
  rpc GetFederationStatus(GetFederationStatusRequest) returns (GetFederationStatusResponse);
}

message ResolveRemoteUserRequest {
  oneof user {
    string fid = 1;   // "@username:servername" или "username:servername"
    string uuid = 2;  // известный UUID (+ server_name обязателен)
  }
  string server_name = 3; // обязателен при uuid-резолве
}

message ResolveRemoteUserResponse {
  bool found = 1;
  barkfluff.federation.GetUserProfileResponse profile = 2;
  string server_name = 3;
}

message FetchRemoteFileRequest {
  string server_name = 1;
  string file_id = 2;
  int64 range_from = 3;
  int64 range_to = 4;
}

message FetchRemoteChatHistoryRequest {
  string server_name = 1;
  string chat_id = 2;
  int64 since_ts_ms = 3;
  int32 limit = 4;
}

message KnownServerInfo {
  string server_name = 1;
  string federation_endpoint = 2;
  string source = 3;                             // WellKnown | Navigator | Manual
  string status = 4;                             // Active | Blocked | Unreachable
  google.protobuf.Timestamp first_seen_at = 5;
  google.protobuf.Timestamp last_seen_at = 6;
  repeated barkfluff.federation.SigningKey keys = 7;
}

message GetKnownServersRequest { }

message GetKnownServersResponse {
  repeated KnownServerInfo servers = 1;
}

message UpsertManualPeerRequest {
  string server_name = 1;
  string federation_endpoint = 2;
  repeated barkfluff.federation.SigningKey keys = 3;
  repeated string tls_spki_sha256 = 4;
}

message UpsertManualPeerResponse { }

message SetServerBlockedRequest {
  string server_name = 1;
  bool blocked = 2;
}

message SetServerBlockedResponse { }

message GetFederationStatusRequest { }

message GetFederationStatusResponse {
  string server_name = 1;
  bool enabled = 2;
  repeated barkfluff.federation.SigningKey own_keys = 3;
  int64 outbox_pending = 4;
  int64 outbox_deadletter = 5;
  int32 known_servers_active = 6;
}
```

Проверь, что `import "federation_api.proto"` резолвится при codegen (та же папка; при проблемах с ProtoRoot — посмотри, как другие proto импортируют `shared.proto`, и повтори схему).

## 3. `shared.proto` — расширения

Message `Message` (сейчас поля 1–8, последнее `edited_at = 8`) — добавить:

```protobuf
  string federated_id = 9; // Глобальный ID сообщения в федеративном чате (uuid; пусто для локальных)

  string sender_uuid = 10; // UUID автора (пусто до включения федерации)
```

Больше в shared.proto ничего не добавлять (FederatedUser/FederatedFileRef живут в federation_api.proto — shared.proto не должен зависеть от федерации).

## 4. `users_api.proto` — расширения

1. В сервис `UsersApi` добавить RPC (рядом с SearchUsers):

```protobuf
  // Резолв федеративного адреса @username:servername (Фаза 2; до неё — Unimplemented)
  rpc ResolveFederatedUser(ResolveFederatedUserRequest) returns (ResolveFederatedUserResponse);
```

и messages:

```protobuf
message ResolveFederatedUserRequest {
  string fid = 1; // "@username:servername" (допускается без @)
}

message ResolveFederatedUserResponse {
  bool found = 1;
  string uuid = 2;
  string username = 3;
  string server_name = 4;
  string first_name = 5;
  string last_name = 6;
  string bio = 7;
  string avatar_url = 8; // проксируемый URL своей ноды (пусто, если скрыт/нет)
}
```

2. Найди message настроек приватности (используется в `GetPrivacySettings`/`UpdatePrivacySettings`; вероятное имя `PrivacySettings`). Добавь поле со следующим свободным номером:

```protobuf
  bool deny_federated_dm = N; // Запретить входящие сообщения с других серверов (default false = разрешено)
```

Важно: именно **deny** (инверсия), а не allow — proto3-default `false` обязан означать текущее поведение «федеративные DM разрешены». В доменной модели Фазы 2 поле может называться как угодно — маппинг инверсией. Поле только в proto; домен/БД Privacy — Фаза 2.

3. Поле `User.uuid = 13` уже добавлено этапом 0.2 — проверь, не дублируй.

## 5. `messages_api.proto` — расширения

1. `SendMessageRequest.source_id` (oneof: `chat_id = 1`, `user_id = 2`) — добавить в **существующий** oneof:

```protobuf
    string user_uuid = 4; // UUID remote-получателя (федеративный DM; Фаза 2)
```

(поле 3 занято `message`; добавление нового поля в существующий oneof обратно-совместимо).

2. `GetPersonChatIdRequest` (сейчас единственное поле `user_id = 1`) — добавить:

```protobuf
  string user_uuid = 2; // UUID remote-пользователя; заполнять ЛИБО user_id, ЛИБО user_uuid
```

3. `ChatMember` (поля `user_id = 1`, `joined_at = 4`; номера 2–3 — проверь, нет ли `reserved`, и не используй их) — добавить:

```protobuf
  string user_uuid = 5; // UUID участника (пусто для локальных до Фазы 2)

  string server_name = 6; // Домен ноды участника (пусто = локальный)
```

4. В сервис `MessagesServerApi` добавить RPC федеративного импорта/экспорта (реализация — Фаза 2, до неё Unimplemented). Все request/response-типы объяви в этом же файле; поля выведи из payload'ов `federation_api.proto` (ImportFederatedChat ← ChatCreatedPayload и т.д.) — Messages не должен импортировать federation_api.proto, продублируй нужные поля плоско:

```protobuf
  rpc ImportFederatedChat(ImportFederatedChatRequest) returns (ImportFederatedChatResponse);
  rpc ImportFederatedMessage(ImportFederatedMessageRequest) returns (ImportFederatedMessageResponse);
  rpc ApplyFederatedEdit(ApplyFederatedEditRequest) returns (ApplyFederatedEditResponse);
  rpc ApplyFederatedDelete(ApplyFederatedDeleteRequest) returns (ApplyFederatedDeleteResponse);
  rpc ApplyFederatedRead(ApplyFederatedReadRequest) returns (ApplyFederatedReadResponse);
  rpc ExportChatEvents(ExportChatEventsRequest) returns (ExportChatEventsResponse);
  rpc CheckFileFederationAccess(CheckFileFederationAccessRequest) returns (CheckFileFederationAccessResponse);
```

Состав полей: смотри payload-структуры в разделе 1 (chat_id, federated_message_id, sender uuid/username/server, text, вложения как плоский набор полей FederatedFileRef, origin_ts_ms — обязателен везде для LWW). `CheckFileFederationAccess(file_id, requesting_server) → allowed`.

## 6. `onliner_api.proto` — расширения

Существующие поля НЕ переносить в oneof (риск ломки клиентского codegen) — только параллельные поля:

```protobuf
// SubscribeToOnlineStatusRequest / ChangeUsersInSubscriptionRequest:
  repeated string user_uuids = 2; // remote-пользователи (UUID); объединяется с user_ids

// UserOnlineStatus:
  string user_uuid = 4; // заполнен для remote-пользователя (тогда user_id = 0)

// TypingEvent:
  string user_uuid = 4; // кто печатает, если remote (тогда user_id = 0)
```

И новый сервис в том же файле (реализация — Фаза 4):

```protobuf
// Внутренний API (TokenType.Service): мост федеративного presence/typing.
service OnlinerServerApi {

  // Federation вливает статус remote-пользователя в in-memory кеш
  rpc UpsertRemoteStatus(UpsertRemoteStatusRequest) returns (UpsertRemoteStatusResponse);

  // Federation вливает typing remote-пользователя для ретрансляции подписчикам чата
  rpc InjectRemoteTyping(InjectRemoteTypingRequest) returns (InjectRemoteTypingResponse);
}

message UpsertRemoteStatusRequest {
  string user_uuid = 1;
  StatusTypeId status = 2;
  google.protobuf.Timestamp last_seen = 3;
}

message UpsertRemoteStatusResponse { }

message InjectRemoteTypingRequest {
  string chat_id = 1;
  string user_uuid = 2;
  TypingAction action = 3;
}

message InjectRemoteTypingResponse { }
```

## 7. `navigator_api.proto` — расширения

`ServerInfo` (поля 1–7) — добавить:

```protobuf
  string server_name = 8;                      // DNS-домен ноды (глобальное имя в федерации)
  string federation_endpoint = 9;              // публичный S2S-адрес
  repeated NavigatorSigningKey signing_keys = 10;
  repeated string tls_spki_sha256 = 11;        // SPKI-отпечатки TLS-серта (для self-signed)
  repeated int32 federation_protocol_versions = 12;
```

Новый message (имя с префиксом, чтобы не конфликтовать с federation_api при совместном импорте где-либо):

```protobuf
message NavigatorSigningKey {
  string key_id = 1;
  bytes public_key = 2;
  google.protobuf.Timestamp expired_at = 3;
}
```

Новый RPC в `NavigatorApi` (реализация — Фаза 1):

```protobuf
  rpc GetServerByName(GetServerByNameRequest) returns (GetServerByNameResponse);
```

```protobuf
message GetServerByNameRequest {
  string server_name = 1;
}

message GetServerByNameResponse {
  bool found = 1;
  ServerInfo server = 2;
}
```

## 8. `beacon_api.proto` — расширения

`GetServerInfoResponse` (поля 1–15, последнее `bots = 15`) — добавить:

```protobuf
  string server_name = 16; // DNS-домен этой ноды (пусто, если федерация не настроена)

  bool federation_enabled = 17; // Включена ли федерация на ноде
```

Реализация отдачи (чтение `Federation:ServerName`/`Federation:Enabled` из Configuration в Beacon-хендлере) — **можно** сделать сразу в этом этапе: она тривиальна (два поля из конфигурации, по образцу `public_name`), не требует Federation-сервиса и полезна для проверки. Если делаешь — Beacon возвращает пустую строку/false при незаполненных ключах.

## Чего НЕ делать

- Не реализовывать ни один новый RPC (кроме опциональных двух полей Beacon, п.8).
- Не подключать `federation_api.proto`/`federation_internal_api.proto` в csproj сервисов (сервиса Federation нет; подключение — Фаза 1). Только в `BarkFluff.Proto.csproj`, если он агрегирует все proto.
- Не синхронизировать клиентские копии proto (`Android/core/src/main/proto/` и аналоги в WPF/Swift/Qt/Web) — клиенты обновляются в Фазе 5. Бэкенд-изменения обратно-совместимы, старые клиентские копии работают.
- Не менять существующие поля/номера/имена. Не использовать зарезервированные номера (проверяй `reserved` в каждом редактируемом message).

## Критерии готовности

1. Полная сборка бэкенда: `dotnet build BarkFluff.sln` (либо поочерёдно все Backend/*.csproj, включающие изменённые proto: Users, Messages, Identity, Onliner, Updates, Beacon, Navigator, AdminPanel, Web, Bots, CloudMessaging, FastAuth, Files) — успех, codegen проходит.
2. `BarkFluff.Web` (Node/protoc-генерация в Docker-stage) — если правился включённый им proto, прогнать его сборку или зафиксировать в коммите, что проверка отложена до CI.
3. Существующий клиент (можно WPF/Android с текущими копиями proto) ходит на изменённый бэкенд: логин, список чатов, отправка сообщения — без регрессий.
4. grpcurl describe (или сгенерированный C#-код) показывает новые сервисы `FederationS2SApi`, `FederationInternalApi`, `OnlinerServerApi` и новые RPC/поля.
5. Obsidian: `Shared/Proto.md` — добавить оба новых файла + перечислить расширения; краткие пометки в файлах затронутых сервисов.
6. Коммит: `feat(rearch-phase0): 0.4 — proto федерации (federation_api, internal, расширения)`.
