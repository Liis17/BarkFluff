# Этап 1.4 — Discovery-цепочка, анти-SSRF, manual-пиры, фоновый рефреш

## Цель

Нода умеет находить пиров всеми тремя способами (well-known → Navigator → manual), наполняет KnownServers, защищена от SSRF, поддерживает блоклист и фоновое обновление ключей. Реализуются internal-RPC управления пирами (для AdminPanel, UI — 1.7).

## Контекст

- Алгоритм резолва, схема well-known, политика обновления ключей, анти-SSRF-требования: [../03-discovery.md](../03-discovery.md) — главный документ этапа, следуй ему дословно.
- CA-валидный HTTPS для bootstrap-фетча + обязательная кросс-сверка well-known ↔ Navigator: [../02-trust-and-certs.md](../02-trust-and-certs.md), «Слой 1».
- Реестр рисков: №35 (SSRF), №43 (ротация manual-пира) в [../09-problems-open-questions.md](../09-problems-open-questions.md).
- Для проверки источника 2 нужен Navigator с `GetServerByName` — этап [step-1.5-navigator-persistence.md](step-1.5-navigator-persistence.md); если он ещё не выполнен, сделай его раньше финальной проверки этого этапа.

## Изменение 1 — валидатор servername + анти-SSRF

`Services/ServernameValidator.cs` (или в стиле проекта) — единая точка, через которую проходит **любой** исходящий адрес (well-known-фетч и gRPC-endpoint):

- синтаксис: валидный DNS-hostname; punycode-нормализация к A-label lowercase (`IdnMapping`); не IP-литерал, не `localhost`;
- endpoint: только схемы `https`/`grpc` (и `http` — исключительно для manual-пиров, см. ниже);
- DNS-резолв → отклонить приватные/зарезервированные диапазоны: RFC 1918, `127.0.0.0/8`, `169.254.0.0/16`, `0.0.0.0/8`, multicast/broadcast, IPv6 `::1`, `fc00::/7`, `fe80::/10`;
- анти-rebinding: прошедший проверку IP пиннится — соединение идёт по проверенному IP (`SocketsHttpHandler.ConnectCallback`), не повторным резолвом;
- **исключение**: записи `Source = Manual` — проверки диапазонов и схем не применяются (сценарий «дружеские ноды в приватной сети»), синтаксис servername проверяется всегда.

Юнит-тесты — таблица кейсов: IP-литерал, localhost, hostname→10.x.x.x, punycode-хомограф (кириллическая «а» → A-label), `http://`-endpoint не-manual, валидный публичный хост.

## Изменение 2 — WellKnownClient

`GET https://{servername}/.well-known/barkfluff`:

- HttpClient **с обычной CA-валидацией** (это bootstrap-канал — никакого trust-all!), таймаут ~10s, лимит размера ответа (например 64 KB);
- проверки документа: `server_name` == запрошенному домену; JCS-канонизация без поля `signature` (переиспользуй канонизатор из 1.2) + Ed25519-проверка подписи ключом **из самого документа** (self-certifying: доверие фиксируется первым знакомством, канал защищён CA-TLS);
- парсинг в модель: endpoint, `tls_spki_sha256[]`, `signing_keys`, `protocol_versions`.

**Dev-флаг для стенда**: `Federation:Insecure:AllowUntrustedWellKnownTls` (дефолт false) — отключает CA-валидацию фетча; читается **только** при `ASPNETCORE_ENVIRONMENT=Development`, при активации — громкий warning-лог на старте. В прод-окружении флаг игнорируется. Нужен, потому что на стенде self-signed серты (1.6).

## Изменение 3 — NavigatorClient

gRPC-клиент `NavigatorApi` → `GetServerByName(server_name)`. Адрес Navigator — посмотри, как его получает Beacon (ключ `NavigatorUrl`), и заведи такой же ключ для Federation в populator, если для `ServiceId.Federation` он не раздаётся. Navigator без авторизации (публичный каталог) — как у Beacon.

## Изменение 4 — Resolver

`Services/ServerResolver.cs` — алгоритм из 03 дословно:

```
0. валидация + анти-SSRF (Изменение 1)
1. KnownServers[servername] свежий (LastKeyRefreshAt < суток) и Active → использовать
2. Source = Manual → использовать как есть
3. well-known → проверка → upsert (source=WellKnown)
4. фолбэк Navigator.GetServerByName → upsert (source=Navigator)
5. ошибка ServerNotFound (код через x-error-code — новое исключение)
```

- Upsert пишет `KnownServers` + `KnownServerKeys` (ключи с `expired_at` как в документе), `FirstSeenAt` при создании, `LastKeyRefreshAt = now`.
- **Кросс-сверка при первом контакте** (записи ещё не было): если доступны и well-known, и Navigator — сверить signing-ключи; расхождение → отказ в резолве + warning-лог + метрика `crosscheck_mismatches`. Один источник недоступен → работаем по доступному (сверка обязательна, только когда оба отвечают).
- Смена ключей у известной ноды: новый документ подписан старым доверенным ключом → применить; не подписан → warning-лог + метрика, запись не обновлять (для `Source = Manual` это правило из 03 — автообновление только по цепочке доверия; алерт админу = warning-лог + метрика, UI — Фаза 6).
- Блокированный (`Blocked`) сервер не резолвится и не обновляется.

## Изменение 5 — discovery-на-лету в XFed

В XFed-обработчике (1.3, шаг «ключ не найден»): неизвестный `origin` или `key_id` → вызвать Resolver → повторить проверку подписи один раз. Rate-limit триггера per-server (не чаще раза в 5 минут, in-memory) — защита от флуда случайными key_id ([../03-discovery.md](../03-discovery.md), «Политика обновления»).

## Изменение 6 — internal-RPC управления пирами

Реализовать в `FederationInternalApiService` (XAuth `TokenType.Service`):

- `GetKnownServers` — весь реестр с ключами (маппинг в `KnownServerInfo`).
- `UpsertManualPeer` — валидация servername (синтаксис; без проверки диапазонов), upsert с `Source = Manual`, `Status = Active`; ключи и SPKI из запроса как есть.
- `SetServerBlocked` — `Status = Blocked` / обратно `Active`. Блок действует на входящие (XFed, уже в 1.3) и исходящие (Resolver, этот этап).
- `GetFederationStatus` — server_name, enabled, свои ключи, `known_servers_active`; outbox-счётчики пока нули (Фаза 2).

## Изменение 7 — фоновый рефреш

`BackgroundService`: раз в сутки для `Active`-пиров с не-Manual источником — перерезолв (well-known, фолбэк Navigator) с правилом цепочки доверия из Изменения 4. N подряд неудач (например 3) → `Status = Unreachable`; Unreachable-пиры ретраятся с экспоненциальным backoff; успех → снова `Active`. Manual-пиры рефрешатся только по правилу «подписан старым ключом».

## Изменение 8 — метрики

`discovery_lookups{source}` (WellKnown/Navigator/Manual/Cache), `discovery_failures`, `known_servers_active` (gauge), `wellknown_signature_failures`, `crosscheck_mismatches`.

## Чего НЕ делать

- Outbox/доставка событий — Фаза 2.
- Регистрация *своей* ноды в Navigator — этап Beacon в Фазе 2+ (расширенная регистрация, [../08-service-migration.md](../08-service-migration.md), Beacon); здесь только чтение каталога.
- UI управления пирами — 1.7.

## Критерии готовности

1. Юнит-тесты валидатора (таблица из Изменения 1) и Resolver'а (моки трёх источников, порядок фолбэков, кросс-сверка, ServerNotFound) — зелёные.
2. Стенд (см. 1.3): резолв ноды-партнёра каждым из трёх способов:
   - manual: `UpsertManualPeer` через grpcurl → `fedping` проходит;
   - well-known: сид удалён, у node2 поднят well-known (порт 7031, dev-флаг TLS при необходимости) → резолв на лету при входящем Ping;
   - Navigator: well-known недоступен, нода зарегистрирована в Navigator (1.5) → резолв через `GetServerByName`.
3. Блоклист: `SetServerBlocked` → входящий Ping от заблокированной ноды — `PermissionDenied`; исходящий резолв — отказ.
4. SSRF-негатив: manual-пир с приватным адресом работает; **не**-manual servername, резолвящийся в 10.x/127.x — отказ до какого-либо сетевого запроса.
5. `GetKnownServers`/`GetFederationStatus` отдают реальное состояние.
6. Obsidian `Backend/Federation.md` дополнен (discovery, реестр, блоклист).
7. Коммит: `feat(rearch-phase1): 1.4 — discovery-цепочка, KnownServers, анти-SSRF`.
