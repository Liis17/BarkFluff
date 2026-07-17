# 08 — План перехода по микросервисам

Подробное описание изменений в каждом сервисе, shared-библиотеках и клиентах. Порядок секций ≈ порядок зависимостей (что нужно раньше). Объём: **S** — мелкие правки, **M** — заметная доработка, **L** — крупная, **XL** — новый сервис/большой рефакторинг.

---

## Shared-библиотеки

### `Shared/BarkFluff.Proto` — M

- Новый `federation_api.proto`: `FederationS2SApi` (Ping, GetServerKeys, GetUserProfile, DeliverEvents, FetchChatHistory, FetchFile, SubscribePresence, DeliverTyping), конверт `FederationEvent`, payload-типы, `FederatedFileRef`.
- Новый `federation_internal_api.proto`: `FederationInternalApi` (ResolveRemoteUser, FetchRemoteFile, FetchRemoteChatHistory, управление пирами, GetFederationStatus).
- `shared.proto`: тип `FederatedUserRef { string uuid; string username; string server_name; }`; версия протокола федерации.
- `users_api.proto`: `User.uuid` (новое поле); `ResolveFederatedUser`; privacy-поле `allow_federated_dm`.
- `messages_api.proto`: `oneof peer` (user_id | user_uuid) в SendMessage/GetPersonChatId; remote-участники в `Chat`/`ChatMember`; server-RPC импорта (`ImportFederatedChat/Message`, `ApplyFederatedEdit/Delete/Read`, `ExportChatEvents`, `CheckFileFederationAccess`); federated-поля в `Message` (federated_id, sender_ref).
- `onliner_api.proto`: `oneof` идентификатора в статусах/подписках/TypingEvent; server-RPC `UpsertRemoteStatus`, `InjectRemoteTyping`.
- `navigator_api.proto`: `ServerName`, `FederationEndpoint`, `SigningKeys`, `TlsSpki`, `GetServerByName`.
- `beacon_api.proto`: `server_name`, `federation_enabled` в `GetServerInfoResponse`.

Все изменения — **добавление полей/RPC**, без ломания существующих номеров (обратная совместимость клиентов обязательна).

### `Shared/BarkFluff.Shared.Identity` — S

- `ServiceId.Federation` (новый enum-член).

### `Shared/BarkFluff.Shared.Queue` — M

- Расширение `NewMessageEvent`, `MessageEditedEvent`, `MessageDeletedEvent`, `MessageReadEvent`: признак федеративного чата + список нод/UUID remote-участников + `FederatedId`/`LastChangeAt` (чтобы Federation не ходил в Messages за контекстом).
- Новые события при необходимости (например `FederatedChatRejectedEvent` для UX отказа privacy).

### `Backend/BarkFluff.GrpcServer` — S/M

- XAuth не меняется (S2S-подписи живут только в Federation).
- Опционально: общий хелпер канонизации подписи, если понадобится вне Federation — по умолчанию держать всё в Federation.

---

## Новый сервис: `BarkFluff.Federation` — XL

Полное описание — [04-federation-service.md](04-federation-service.md). Скелет задач:

1. Каркас сервиса по шаблону платформы (LoadConfiguration, Serilog, метрики, XAuth для internal API, Dockerfile.slim, workflow `build-backend-federation.yml`).
2. Ключи: генерация Ed25519 при первом старте, хранение, `GetServerKeys`, `/.well-known`-документ (генерация JSON + подпись).
3. XFed-интерсептор (проверка подписей входящих) + подписывающий client-interceptor исходящих + SPKI-пиннинг в `SocketsHttpHandler`.
4. KnownServers + discovery (well-known → Navigator → manual) + фоновой рефреш ключей.
5. Outbox: таблица, диспетчер, backoff, per-(destination,chat) упорядочивание, dead-letter, janitor.
6. Идемпотентность входящих (`ProcessedEvents`).
7. RabbitMQ-консюмеры внутренних событий → outbox.
8. Импорт входящих → gRPC в Messages/Users.
9. FetchFile / FetchRemoteFile стриминг.
10. Presence/typing мосты.
11. Метрики + rate limiting.

---

## `BarkFluff.Users` — L

| Задача | Детали |
|--------|--------|
| UUID локальных пользователей | Колонка `Users.Uuid` + уникальный индекс + backfill-миграция; отдача в proto; генерация при создании (включая ботов) |
| Таблица `RemoteUsers` | [01-addressing-identity.md](01-addressing-identity.md); storage + маппинг в общий `User`-ответ (клиент получает единый вид «пользователь» с признаком remote) |
| `ResolveFederatedUser` | Парсинг FID; локальный servername → обычный поиск; чужой → `Federation.ResolveRemoteUser` → upsert RemoteUsers |
| Приватность | Новое поле `Privacy.AllowFederatedDm` (bool, default **true**) + отдача/редактирование через `Get/UpdatePrivacySettings`; учёт в `GetUserProfile` для S2S (какие поля отдавать чужой ноде — переиспользовать `ProfileVisibleOnSite`/`AvatarVisibility`/`BioVisibility`) |
| S2S-профиль | Обработчик для Federation: `GetUserProfile(username/uuid)` с privacy-фильтрацией (аналог `GetUserByUsername`) |
| События профиля | `UserChangedUsername/Name/Avatar/Bio` уже публикуются — Federation их подхватит; добавить `UserDeactivated` при удалении |
| Поиск | `SearchUsers`: строка с `:` и валидным FID-паттерном → ветка резолва remote (единичный результат), не trigram |
| GDPR-экспорт | Дополнить экспорт списком федеративных чатов; удалённые данные на чужих нодах — best effort ([09](09-problems-open-questions.md)) |

Не трогаем: устройства, бейджи, папки чатов, prekeys (до фазы федеративного E2E), mute (работает по ChatId — федеративные чаты мьютятся как обычные).

## `BarkFluff.Messages` — L

Ядро изменений — [05-chat-replication.md](05-chat-replication.md):

| Задача | Детали |
|--------|--------|
| Схема БД | `ChatMembers.UserUuid NULL`; `Message.FederatedId uuid NULL` (уникальный индекс с ChatId), `SenderUuid NULL`, `LastChangeAt NOT NULL` (backfill `COALESCE(EditedAt, SentAt)`); `Chats`: признак федеративности + нормализованная UUID-пара для fed-DM; `FederatedReadStates` |
| Импорт-RPC | `ImportFederatedChat`, `ImportFederatedMessage`, `ApplyFederatedEdit/Delete/Read` — идемпотентные, LWW-проверка, лимиты как у обычных сообщений, проверка «origin-нода — участник чата» |
| Экспорт-RPC | `ExportChatEvents(chat_id, cursor)` для catch-up; `CheckFileFederationAccess(file_id, server)` для Files |
| SendMessage | `oneof peer` c uuid; создание fed-DM; отказ `FederatedDmRejected` от чужой ноды → понятная ошибка клиенту; `AddUser` в группу с remote → `FederatedGroupsNotSupported` |
| События | Расширенные поля federated-контекста в `NewMessageEvent` и родственных |
| Выдача | `ListChats`/`ListMessages`/`ListChatMembers`: remote-участники через `RemoteUsers` (батч-запрос Users по uuid); имена/аватары в Redis-кеш как сейчас |
| Membership | `CheckChatMembership`-ответ + federated-признак + ноды-участники (для Onliner) |
| Анти-дубль fed-DM | Уникальность по UUID-паре + протокол слияния при одновременном создании (отдельная задача, сложная) |
| Сортировка выдачи | Для федеративных чатов `ListMessages` сортирует по `SentAt` (+ tie-break), **не по локальному Id**: сообщение, импортированное catch-up'ом после даунтайма, получает bigserial в момент импорта и при сортировке по Id встало бы в конец чата. Проверить текущую сортировку |

## `BarkFluff.Updates` — S

Почти не меняется (главный выигрыш архитектуры): входящие федеративные сообщения превращаются в обычные `NewMessageEvent` на ноде получателя и идут по существующим стримам.

- Проверить сериализацию расширенных событий (новые поля в Queue-типах).
- `NewMessageEvent` для fed-чата содержит remote-отправителя (uuid) — прокинуть в стрим-payload (proto-поле).

## `BarkFluff.Onliner` — M

[07-presence-typing.md](07-presence-typing.md): uuid-ветка in-memory статусов, `UpsertRemoteStatus`/`InjectRemoteTyping`, федеративный признак в typing-потоке, исключение remote-статусов из DatabasePersistenceService, proto-`oneof`.

## `BarkFluff.Files` — M

[06-files.md](06-files.md): REST-маршрут `/download/fed/{server}/{fileId}` (проксирование через Federation), server-стрим для отдачи файлов чужим нодам (по запросу Federation), проверка привязки file→chat→server через Messages, privacy-проверка для аватаров, Range-запросы.

## `BarkFluff.Navigator` — M

[03-discovery.md](03-discovery.md): персистентность (PostgreSQL), `ServerName` как ключ, federation-поля, валидация регистрации через `/.well-known`, `GetServerByName`. Обратная совместимость `ListServers` для существующих клиентов.

## `BarkFluff.Beacon` — S

- `GetServerInfoResponse`: + `server_name` (клиент должен знать имя своей ноды: рендер FID, отличение «своих» от remote), + `federation_enabled`.
- Регистрация в Navigator — расширенным запросом (federation-поля берёт из Configuration/Federation).

## `BarkFluff.Configuration` — S

- Регистрация `ServiceId.Federation` + секция конфигурации ([04](04-federation-service.md)).
- Ключи `FederationService:Host/Token` для сервисов-клиентов (Users, Files, Onliner, AdminPanel).
- Единый источник `Federation:ServerName` — также нужен Beacon и AdminPanel.

## `BarkFluff.Identity` — S (важно: почти не участвует)

Аккаунты и сессии **не федерируются**: клиент авторизуется только на своей ноде, JWT локальны, remote-пользователи никогда не аутентифицируются у нас. Изменений в потоках Auth/2FA/сброса — нет.

- Единственное: убедиться, что `SessionRevoked`-механика не затрагивается, и завести service-токен для Federation internal API (стандартная процедура).

## `BarkFluff.CloudMessaging` — S

Пуши для fed-сообщений идут по обычному пути (копия чата локальна). Правка: имя отправителя для пуша — из RemoteUsers/FID (данные придут в расширенном событии). Mute-фильтр работает без изменений (ChatId общий).

## `BarkFluff.Notification` — нет изменений

Email-уведомления — только локальные события Identity.

## `BarkFluff.AdminPanel` — M

Новая страница «Федерация» (в `Pages/v2/`, MD3 — по действующему правилу):

- список KnownServers (статус, ключи, last seen, источник), ручное добавление пира, блок/разблок;
- состояние outbox: глубина, dead-letter, ручной retry/sync чата;
- ключи своей ноды: отпечатки, ротация, экспорт публичного ключа;
- метрики федерации (через существующий механизм ServiceMetrics/Seq).

gRPC-клиент `FederationInternalApi` + конфиг `FederationService:Host/Token`.

## `BarkFluff.Web` (gRPC-Web прокси + веб-мессенджер) — M (клиентская часть)

- Прокси: новых требований нет (federation — не браузерный трафик).
- Веб-клиент: как все клиенты — парсинг FID в поиске, отображение remote-пользователей/typing, uuid-peer в SendMessage (см. «Клиенты»).

## `Barkfluff.WebServer` — S

- Отдача `/.well-known/barkfluff` (если решим раздавать им, а не nginx'ом напрямую) — координация с Nginx-задачей.
- Публичная страница профиля (`GetUserByUsername`) — без изменений (только локальные пользователи).

## Nginx — M

- Субдомен `federation.{domain}`: gRPC (HTTP/2) → Federation:7030; TLS — серт ноды (self-signed допустим для S2S: чужие ноды проверяют по SPKI-пину). Для `/.well-known` на apex CA-валидный серт (Let's Encrypt) **обязателен для публичных нод** — это bootstrap-канал discovery ([02](02-trust-and-certs.md)); нода без него знакомится с новыми пирами только через ручное добавление.
- `location /.well-known/barkfluff` на apex-домене → статический файл/прокси на Federation.
- Rate limiting зоны для federation-эндпоинта.
- Долгоживущие стримы (`SubscribePresence` — часами, `FetchFile` — большие файлы): явные `grpc_read_timeout`/`grpc_send_timeout` и отключение буферизации проксирования — иначе nginx молча рвёт стримы.
- Обновить референсный конфиг + `docker-compose-dev.yml` (сервис federation + сети).

## `BarkFluff.Bots`, `BarkFluff.Calls`, `BarkFluff.FastAuth`, `BarkFluff.ClientStorage`, `BarkFluff.Developers` — нет изменений в MVP

- **Bots**: боты остаются локальными; федеративный доступ к ботам (`@bot:server`) — будущая фаза (проблемы: bot-токены не пересекают ноды, вебхуки).
- **Calls**: кросс-нодовые звонки требуют решения про LiveKit (общий SFU либо выбор SFU одной из нод + федеративный ring) — будущая фаза, зафиксировано в [09](09-problems-open-questions.md).
- **FastAuth** (QR-логин) — строго внутри ноды.

---

## Клиенты (Android V1, WPF, iOS/macOS, Linux Qt, Web) — M каждый, отдельная фаза

Минимальный набор для fed-MVP (детальные гайды писать при реализации, по образцу существующих ClientGuide-доков):

1. **Поиск**: распознавание паттерна `@user:server` → вызов `ResolveFederatedUser`; карточка remote-пользователя с FID.
2. **Идентификаторы**: работа с `oneof peer` (uuid для remote) в отправке сообщений и подписках; собственный `server_name` из Beacon для отличения своих.
3. **UI**: бейдж/подпись сервера у remote-собеседников (`@bob:chat.example.org`), typing от FID, placeholder «файл недоступен — сервер offline». Servername со смешанными скриптами отображать в punycode (homograph-защита, [01](01-addressing-identity.md)); `filename` из федеративного снапшота экранировать при сохранении и отображении (path traversal, инъекции в UI).
4. **Настройки приватности**: тумблер «Запретить сообщения с других серверов» (`AllowFederatedDm`).
5. Деградация: старый клиент против новой ноды не ломается (proto только расширяется); федеративные чаты у старого клиента отображаются с fallback-именем — проверить отдельно.

---

## Матрица зависимостей (что за чем)

```
Proto/Queue/ServiceId  →  Federation каркас + ключи + discovery
                       →  Navigator (персистентность, federation-поля)
Users (UUID, RemoteUsers, Resolve)  →  Messages (схема, импорт/экспорт)
                                    →  Federation (outbox ⇄ Messages)
Files (fed-download)   →  после базовой доставки сообщений
Onliner (presence/typing)  →  после Messages.CheckChatMembership+fed
Beacon/Configuration/Nginx — параллельно, малые
AdminPanel — после FederationInternalApi
Клиенты — последняя фаза MVP
```
