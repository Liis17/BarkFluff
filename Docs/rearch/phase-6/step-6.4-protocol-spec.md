# Этап 6.4 — Спецификация протокола федерации на Developers-портале

## Цель

Опубликовать протокол так, чтобы **стороннюю ноду можно было реализовать не на .NET**: proto-контракты + всё, что из них не выводится (канонизация подписей, порядок проверок, семантика статусов и LWW). Критерий роадмапа: спецификация опубликована.

## Контекст

- Обязательство публиковать протокол — [../09-problems-open-questions.md](../09-problems-open-questions.md) №30; требования к детерминированной сериализации для сторонних реализаций — [../02-trust-and-certs.md](../02-trust-and-certs.md) (раздел «Подпись FederationEvent») и [../11-plan-review.md](../11-plan-review.md) И-1/И-2.
- Портал: `Backend/Barkfluff.Developers` (gRPC-Web, порт 7020) + фронтенд `Frontend/Developers` (React + Vite). Содержимое сеется при старте: `Infrastructure/SeedData.cs` — секции документации (`overview`, `quickstart`, `implementation`, `auth-headers`, `connection-flow`, `error-codes`) и `ProtoMetadata` (10 записей: shared, beacon, identity, users, messages, files, updates, onliner, fastauth, navigator).
- `.csproj` копирует в output **все** `Shared/BarkFluff.Proto/*.proto` (`Content Include="..\..\Shared\BarkFluff.Proto\*.proto"`), поэтому `federation_api.proto` и `federation_internal_api.proto` уже доступны `ProtoFileProvider` — нужны только записи метаданных.
- Коды ошибок портал собирает рефлексией по наследникам `BaseGrpcException` (`ErrorCodeSeeder`) — федеративные исключения (`Shared/BarkFluff.Shared.Exceptions/Federation/`, `FederatedDmRejectedException`) попадут туда автоматически; проверить, что попали.

## Изменение 1 — proto-метаданные федерации

В `Infrastructure/SeedData.cs` добавить две записи `ProtoMetadata` по образцу существующих (`Slug`, `DisplayName`, `Order`, `RpcDescriptions`):

- `federation_api.proto` — «Federation S2S — межсерверный протокол»: описать каждый RPC (`Ping`, `GetServerKeys`, `GetUserProfile`, `DeliverEvents`, `FetchChatHistory`, `SyncChatStates` — если добавлен в 2.6, `FetchFile`, `SubscribePresence`, `DeliverTyping`) и назначение конвертa `FederationEvent` с его payload'ами.
- `federation_internal_api.proto` — «Federation Internal — API внутри ноды»: пометить явно, что это **внутренний** контракт (XAuth, `TokenType.Service`), сторонним реализациям не нужен, публикуется для полноты.

Проверить, как `SeedData` ведёт себя при повторном старте (идемпотентность/обновление) — новые записи не должны дублироваться и не должны затирать правки; если сид работает «только на пустой БД», отметить это в отчёте и в документации этапа.

## Изменение 2 — секция «Федерация: спецификация протокола»

Новая секция документации в `SeedData` (`Key = "federation-protocol"`, следующий `Order`). Содержание — то, чего **нет** в .proto:

1. **Модель.** Нода, `servername`, FID, home server, origin; инвариант «нода говорит только за свой домен»; что `long user_id` через границу не ходит.
2. **Транспорт и доверие.** gRPC поверх TLS; self-signed допустим, подлинность даёт Ed25519; SPKI-пиннинг; почему не mTLS.
3. **Заголовки и подпись запроса** (дословно и проверяемо): `x-bf-origin`, `x-bf-destination`, `x-bf-timestamp`, `x-bf-key-id`, `x-bf-signature`; каноническая строка `{origin}\n{destination}\n{timestamp}\n{grpc-method-full-name}\n{hex(sha256(request-bytes))}`; **`request-bytes` — полученные wire-байты сообщения, без пере-сериализации**; окно времени (дефолт 300 с); `GetServerKeys` — единственный неподписанный RPC (bootstrap).
4. **Порядок проверок на приёме** (нумерованный список, как в реализации): заголовки → destination → окно времени → ключ пира → подпись → блоклист; какие ошибки и коды возвращаются (`Unauthenticated`, `ClockSkewDetected`, `PermissionDenied`).
5. **Подпись события.** Канонизация `FederationEvent`: отправитель сериализует с **пустыми** `origin_signature`/`origin_key_id`, подписывает wire-байты, затем заполняет поля; получатель очищает оба поля, пере-сериализует, проверяет. Требование к реализациям: детерминированная сериализация protobuf (поля по возрастанию номеров, `repeated` в порядке массива, `map` детерминированно).
6. **Discovery.** Формат `/.well-known/barkfluff` (пример документа), **подпись по JCS (RFC 8785) документа без поля `signature`**, требование совпадения `server_name` с доменом запроса, приоритет источников (well-known → Navigator → ручной пир), правила ротации ключей и континуитета доверия.
7. **Доставка событий.** `DeliverEvents` — батч, идемпотентность по `event_id`; таблица `EventStatus`: `OK`, `ALREADY_PROCESSED`, `REJECTED` (перманентно — не ретраить), `RETRY` (временно — backoff); рекомендованный backoff и окно ретраев; упорядочивание per-(destination, chat) и отсутствие head-of-line blocking для перманентных отказов.
8. **Семантика чатов.** Копия чата на каждой ноде; LWW по времени последнего изменения с tie-break `(origin_ts_ms, origin_server, event_id)`; **удаление терминально**; clamp меток из будущего; read-receipts «прочитано до»; pin не федерируется; удаление чата — локальное действие.
9. **Валидация импорта** (обязательная для совместимой реализации): origin — участник чата; автор события принадлежит origin; edit/delete — только автором сообщения; лимиты контента как у локальных; отказ на remote-uuid, совпадающий с локальным пользователем.
10. **Файлы.** Файлы не реплицируются; `FederatedFileRef` как снапшот метаданных; `FetchFile` с Range; авторизация на уровне ноды (файл фигурирует во вложении общего чата); обрыв стрима при превышении заявленного размера.
11. **Presence/typing.** Агрегированный `SubscribePresence` (один стрим на пару нод), обязательная проверка наличия общего чата, privacy фильтрует origin, `PRESENCE_STATUS_UNKNOWN` = скрыт/нет данных; typing — fire-and-forget с coalescing и rate limit; capability-флаги в `Ping`.
12. **Версионирование и ограничения MVP**: `protocol_versions` в discovery и `Ping`; чего в протоколе v1 нет (группы, E2E, боты, звонки, миграция аккаунтов).

Формат — как у существующих секций (Markdown в `Content`); объём — на уровне «плотно, без воды»: это спецификация, а не туториал.

## Изменение 3 — фронтенд портала

- Проверить, что новая секция и proto-файлы отображаются существующим UI (`Frontend/Developers`) без правок. Понадобилась правка (например, жёсткий список секций/proto в коде) — сделать минимальную, по образцу соседних записей; редизайн портала не входит в этап.
- Убедиться, что содержимое `federation_api.proto` реально отдаётся `GetProtoFileContent` (файл копируется в `output/Proto/`).

## Изменение 4 — сверка кодов ошибок

Проверить, что федеративные `ErrorCode` (в т.ч. литеральный `"FederatedDmRejected"`) попадают в `error_codes` через `ErrorCodeSeeder`, и что их описания понятны стороннему разработчику; при необходимости уточнить `ErrorMessage`/описание в самих классах исключений — **только текст**, не коды (коды — часть контракта).

## Чего НЕ делать

- Не менять proto-контракты «ради красоты спецификации»: спецификация описывает реализацию, а не наоборот. Нашёл расхождение proto ↔ реализация — останови этап и сообщи.
- Не выносить спецификацию на отдельный сайт/репозиторий.
- Не публиковать внутренние детали инфраструктуры (имена контейнеров, адреса, service-токены).
- Не переписывать разделы `02`/`03`/`04`/`05` — спецификация ссылается на них как на источник, но пишется самостоятельным связным текстом для внешнего читателя.

## Критерии готовности

1. Портал собирается и стартует (`dotnet build Backend/Barkfluff.Developers/Barkfluff.Developers.csproj`); сид добавляет секцию `federation-protocol` и две записи proto-метаданных; повторный старт не плодит дубликатов.
2. Каждое техническое утверждение спецификации сверено с кодом: каноническая строка — с `Services/XFedCanonicalString.cs`, канонизация события — с `Services/EventSigner.cs`, well-known/JCS — с `Services/WellKnownDocumentService.cs`, классификация статусов — с `Host/FederationS2SApiService.cs` и `BackgroundServices/OutboxDispatcher.cs`. Список сверок — в отчёте.
3. `GetProtoFileContent("federation_api.proto")` отдаёт актуальное содержимое (проверить тестом или ручным вызовом хендлера).
4. Федеративные коды ошибок присутствуют в `error_codes`.
5. **[делает разработчик]** Портал открыт в браузере: секция читается, proto-файлы отображаются, ссылки внутри секции работают.
6. Obsidian: `Backend/Developers.md` — упоминание новой секции и proto-метаданных; `Backend/Federation.md` — ссылка «спецификация протокола опубликована на Developers-портале».
7. Коммит: `feat(rearch-phase6): 6.4 — спецификация протокола федерации на Developers-портале`.
