# 04 — Новый микросервис `BarkFluff.Federation`

Единственная точка входа и выхода федеративного трафика ноды. Никакой другой сервис наружу (в интернет к другим нодам) не ходит и снаружи не виден.

- **Порт:** 7030 (следующий свободный в схеме портов).
- **Внешний адрес:** через nginx, субдомен `federation.{domain}` (TLS, HTTP/2, gRPC), объявляется в `/.well-known/barkfluff`.
- **Стек:** стандартный для платформы — .NET 10, gRPC, MediatR (CQRS), EF Core + PostgreSQL (`FederationDb`), MassTransit (RabbitMQ), XAuth для внутреннего API, `LoadConfiguration(ServiceId.Federation)`.

## Зоны ответственности

1. **Trust**: генерация/хранение/ротация signing-ключей ноды, подпись исходящих, проверка входящих ([02-trust-and-certs.md](02-trust-and-certs.md)).
2. **Discovery + KnownServers**: резолв servername, реестр пиров, блоклист ([03-discovery.md](03-discovery.md)).
3. **Доставка**: приём входящих S2S-событий → раздача во внутренние сервисы; исходящий **outbox** с ретраями.
4. **Проксирование запросов**: резолв профилей, скачивание файлов с origin-сервера, catch-up истории.
5. **Presence/typing мосты** ([07-presence-typing.md](07-presence-typing.md)).

## Три API-поверхности

### 1. `FederationS2SApi` (`federation_api.proto`, GrpcServices=Server) — публичный, для чужих нод

Авторизация: подпись Ed25519 (интерсептор «XFed»), НЕ XAuth. Все методы принимают/отдают только UUID и FID — никаких long-ID.

| RPC | Назначение |
|-----|-----------|
| `Ping(PingRequest) → PingResponse` | Handshake: версия протокола, server_name, время (диагностика clock skew) |
| `GetServerKeys` | Текущие публичные ключи (второй канал к `/.well-known`) |
| `GetUserProfile(username \| uuid)` | Профиль пользователя ноды с учётом privacy; возвращает uuid, username, имя, bio, avatar_file_ref |
| `DeliverEvents(stream/batch FederationEvent) → DeliverEventsResponse` | Основной канал доставки: пачка событий (новые сообщения, edit, delete, read, профильные события). Идемпотентность по `event_id` (uuid). Ответ — per-event статус |
| `FetchChatHistory(chat_id, cursor)` | Catch-up: дозапросить пропущенные события чата после даунтайма ([05-chat-replication.md](05-chat-replication.md)). Обязательная проверка: запрашивающая нода — участник чата (знание/угадывание ChatId недостаточно — тот же принцип, что в FetchFile) |
| `FetchFile(file_ref) → stream chunks` | Отдача файла со своего S3 по запросу ноды-партнёра ([06-files.md](06-files.md)) |
| `SubscribePresence(user_uuids[]) → stream PresenceEvent` | Стрим статусов пользователей этой ноды для ноды-партнёра |
| `DeliverTyping(TypingFederationEvent)` | Ретрансляция typing (fire-and-forget семантика) |

`FederationEvent` — конверт:

```protobuf
message FederationEvent {
  string event_id = 1;        // uuid, идемпотентность
  string origin_server = 2;   // должен совпадать с x-bf-origin
  int64  origin_ts_ms = 3;    // UTC ms по часам origin — база для LWW
  bytes  origin_signature = 4; // Ed25519-подпись канонической сериализации события ключом
                               // origin: перепроверяемое авторство копий + те же гарантии
                               // для catch-up-истории, что и у прямой доставки
  string origin_key_id = 5;
  oneof payload {
    NewMessagePayload new_message = 10;
    MessageEditedPayload message_edited = 11;
    MessageDeletedPayload message_deleted = 12;
    MessagesReadPayload messages_read = 13;
    UserProfileChangedPayload profile_changed = 14;
    UserDeactivatedPayload user_deactivated = 15;
    ChatCreatedPayload chat_created = 16;
  }
}
```

### 2. `FederationInternalApi` (GrpcServices=Server, `TokenType.Service`) — для своих сервисов

| RPC | Кто зовёт | Назначение |
|-----|-----------|-----------|
| `ResolveRemoteUser(fid \| uuid)` | Users | Резолв + профиль remote-пользователя |
| `EnqueueOutbound(destination_server, FederationEvent)` | (обычно не нужен — см. RabbitMQ ниже) | Прямая постановка события в outbox |
| `FetchRemoteFile(server_name, file_ref)` | Files | Стриминг файла с origin-ноды |
| `FetchRemoteChatHistory(server_name, chat_id, cursor)` | Messages | Catch-up истории |
| `GetKnownServers / UpsertManualPeer / BlockServer / UnblockServer` | AdminPanel | Управление пирами |
| `GetFederationStatus` | AdminPanel / Beacon | Ключи, outbox-глубина, состояние пиров |

### 3. RabbitMQ (MassTransit)

**Потребляет** (превращает внутренние события в исходящие федеративные):

| Очередь | Событие | Действие |
|---------|---------|----------|
| `new-messages-federation-handler` | `NewMessageEvent` | Если чат федеративный (есть remote-участники) → сериализовать в `FederationEvent`, положить в outbox для каждой ноды-участника |
| `messages-edited-federation-handler` | `MessageEditedEvent` | то же |
| `messages-deleted-federation-handler` | `MessageDeletedEvent` | то же |
| `read-receipts-federation-handler` | `MessageReadEvent` | то же |
| `user-changed-name/username/avatar/bio-federation` | `UserChanged*` | Разослать нодам, где у пользователя есть активные чаты |
| `session-revoked-federation` | `SessionRevokedEvent` | стандартная инвалидация XAuth-токенов |

Чтобы Federation знал, федеративный ли чат, `NewMessageEvent` и родственные события **расширяются** флагом/списком remote-участников (проставляет Messages) — иначе Federation пришлось бы дёргать Messages на каждое событие.

**Публикует**: входящие федеративные события транслируются не через RabbitMQ, а прямыми gRPC-вызовами в Messages/Users (см. поток ниже) — так ошибка обработки возвращается синхронно и попадает в ретрай-политику outbox отправителя. (Альтернатива — publish в RabbitMQ и мгновенный ACK — отвергнута: теряется обратная связь о невалидных событиях.)

## Outbox — надёжная доставка

```
FederationOutbox
  Id             bigserial PK
  Destination    text          -- server_name
  ChatId         uuid NULL     -- для упорядочивания доставки per-(Destination, ChatId); NULL = вне-чатовое (профильные, 2.9)
  EventId        uuid
  EventType      text
  PayloadBytes   bytea
  CreatedAt      timestamptz
  Attempts       int
  NextAttemptAt  timestamptz
  Status         enum (Pending | Delivered | DeadLetter)
```

- BackgroundService-диспетчер: выбирает Pending с `NextAttemptAt <= now`, группирует по Destination, шлёт батчем `DeliverEvents`.
- Ретраи: экспоненциальный backoff (30s → 2m → 10m → 1h → 6h), гарантия **at-least-once**, порядок доставки в пределах (Destination, ChatId) сохраняется (не отправлять событие N+1 чата, пока N не доставлено; события разных чатов — независимо).
- Per-event статусы `DeliverEventsResponse` классифицируются: **retryable** (временная ошибка — сеть, перегрузка, недоступность внутреннего сервиса приёмника) → backoff-ретрай; **permanent** (событие отвергнуто как невалидное: `FederatedDmRejected`, ошибка валидации, нарушение правил) → сразу `DeadLetter`, а **очередь чата продолжает ехать** — иначе одно перманентно отвергаемое событие навсегда блокирует все последующие события чата (head-of-line blocking).
- После `MaxAttempts` (например, 7 суток недоступности) → `DeadLetter` + метрика + видимость в AdminPanel. Получатель при восстановлении сам дозапросит историю через `FetchChatHistory`.
- Delivered-записи чистятся фоновым janitor'ом (TTL, например 7 дней — полезно для отладки).

Приёмник хранит обработанные `event_id` (таблица/Redis с TTL ≥ окна ретраев) — повторная доставка отвечает `AlreadyProcessed`, не дублируя сообщение.

## Поток входящего события (пример: новое сообщение)

```
Нода A (outbox dispatcher)
  → gRPC DeliverEvents → nginx ноды B → Federation B
    1. XFed-интерсептор: подпись, timestamp, origin не в блоклисте
    2. event_id уже обработан? → AlreadyProcessed
    3. sender.server_name == origin? (нода говорит только за своих)
    4. gRPC → MessagesServerApi.ImportFederatedMessage(...)  [service-токен]
       - Messages: upsert чата-копии, вставка сообщения, LWW-проверка,
         публикация NewMessageEvent → Updates/CloudMessaging как обычно
    5. пометить event_id обработанным, ответить Ok
```

Локальный пользователь ноды B получает realtime и push **существующим механизмом** — в этом главный выигрыш выбранной модели «копия чата на каждой стороне»: вся внутренняя машинерия (Updates, CloudMessaging, счётчики непрочитанного) работает без изменений.

## БД Federation (сводно)

- `KnownServers`, `KnownServerKeys` — [03-discovery.md](03-discovery.md)
- `FederationOutbox` — выше
- `ProcessedEvents (EventId PK, ReceivedAt)` — идемпотентность входящих
- `SigningKeys (KeyId, PublicKey, PrivateKeyEncrypted?, CreatedAt, ExpiredAt)` — свои ключи

## Конфигурация (Configuration-сервис, `ServiceId.Federation`)

| Ключ | Описание |
|------|----------|
| `Federation:ServerName` | Домен ноды — глобальное имя в сети |
| `Federation:SigningKey` | Приватный ключ (или путь/ссылка) |
| `Federation:ExternalEndpoint` | Публичный адрес S2S (для `/.well-known`) |
| `Federation:Enabled` | Выключатель федерации целиком (нода-одиночка) |
| `Federation:DefaultPolicy` | `open` / `allowlist` — принимать всех или только ручных пиров |
| `FederationDb`, `Redis`, `RabbitMQ:*` | стандартно |
| `MessagesService:Host/Token`, `UsersService:Host/Token`, `FilesService:Host/Token`, `OnlinerService:Host/Token` | внутренние клиенты |
| `NavigatorUrl` | для discovery-фолбэка и (опц.) регистрации |

## Метрики (MetricsCollector, как у всех)

`s2s_requests_in/out`, `s2s_signature_failures`, `s2s_clock_skew_rejections`, `outbox_pending` (gauge), `outbox_delivered`, `outbox_deadletter`, `events_imported{type}`, `events_duplicate`, `discovery_lookups{source}`, `known_servers_active` (gauge), `remote_file_fetches`, `presence_streams_active` (gauge).

## Безопасность публичной поверхности

- Rate limiting на nginx (per-IP) + прикладной (per-origin-server) в Federation.
- Отдельный, более строгий лимит на `ChatCreated` per-origin-server — самое дорогое входящее действие (волна пушей + персистентные строки Chats/Messages/RemoteUsers; ровно сценарий спам-волн Matrix 2019). Например, N новых чатов/час с одной ноды; превышение → метрика + алерт в AdminPanel + однокнопочный блок ноды прямо из алерта.
- Лимиты размеров: батч `DeliverEvents` ≤ N событий / M байт; `FetchFile` — только файлы, реально фигурирующие в федеративных чатах с этой нодой (проверка привязки file_ref → chat → участие ноды).
- Federation-сервис не имеет доступа к БД других сервисов — только их server-API по service-токенам (стандартная изоляция платформы сохраняется).
