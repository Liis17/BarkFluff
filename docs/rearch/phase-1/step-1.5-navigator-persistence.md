# Этап 1.5 — Navigator: PostgreSQL, federation-поля, валидация, GetServerByName

## Цель

Navigator переживает рестарт (PostgreSQL вместо in-memory), понимает federation-поля `ServerInfo`, валидирует регистрацию через well-known заявленного домена и отдаёт точечный `GetServerByName`. Существующий `ListServers` и легаси-регистрация (без federation-полей) работают без изменений.

## Контекст

- Роль Navigator в федерации и таблица изменений: [../03-discovery.md](../03-discovery.md), «Источник 2». Ключевой инвариант: Navigator — вторичный канал; сеть обязана работать при лежащем Navigator.
- Текущее состояние сервиса: `Backend/BarkFluff.Navigator/` — маленький, вне платформенного шаблона (нет `LoadConfiguration`/Serilog/метрик; Kestrel через env `NAVIGATOR_PORT`). Файлы: `Program.cs`, `Persistence/ServersStorage.cs` (in-memory `ConcurrentDictionary`, фильтр активности ~10 мин, троттлинг регистрации ~2 мин), `Domain/ServerInfo.cs`. **Прочитай все три перед началом.**
- Proto-расширения уже сделаны этапом 0.4: `navigator_api.proto` — поля `server_name`, `federation_endpoint`, `signing_keys`, `tls_spki_sha256`, `federation_protocol_versions` в `ServerInfo` + RPC `GetServerByName`.

**Зафиксированное решение**: Navigator остаётся вне платформенного шаблона (публичная инфраструктура вне ноды). БД — через переменную окружения `NAVIGATOR_DB` (connection string), фолбэк — ключ `NavigatorDb` в appsettings. Никакого `LoadConfiguration`.

## Изменение 1 — пакеты

Проверь `BarkFluff.Navigator.csproj`: если EF Core + Npgsql не подключены — добавь версии, как у Onliner. ([../03-discovery.md](../03-discovery.md) утверждает, что пакет уже есть — проверь, не верь на слово.)

## Изменение 2 — NavigatorContext + миграция

Таблица `Servers`: все текущие поля `ServerInfo` (Id, Name, BeaconHost, BeaconPort, Description, ServerPublicName, Location, ColorLiteHex/MainHex/HardHex, CreatedAt, AddedBy) + `LastSeenAt timestamptz` (заменяет in-memory `lastSeen`) + federation-поля:

- `ServerName text NULL` + уникальный частичный индекс (`WHERE ServerName IS NOT NULL`);
- `FederationEndpoint text NULL`;
- `TlsSpkiSha256 text[] NULL`;
- `FederationProtocolVersions int[] NULL`;
- `SigningKeys jsonb NULL` — список `{key_id, public_key_base64, expired_at}` (отдельная таблица не нужна: Navigator ключи только хранит и отдаёт, не индексирует по ним).

`Database.Migrate()` на старте (Navigator его сейчас не делает — добавь по образцу других сервисов). Про баг `dotnet ef` — правило 5 в [README.md](README.md).

## Изменение 3 — ServersStorage → EF

Переписать `ServersStorage` на `NavigatorContext` (регистрация как scoped/факторка — согласуй с текущей singleton-регистрацией в `Program.cs`, не ломая gRPC-хост):

- `GetServers()` — как раньше: только записи с `LastSeenAt` в пределах активного периода (конфиг `ServerRegistration:ActivePeriodMinutes` сохраняется);
- `RegisterServer()` — upsert. **Ключ идентичности**: `ServerName`, если задан; иначе легаси-составной `Name+BeaconHost+BeaconPort` (текущее поведение). Троттлинг регистраций оставить in-memory (как сейчас) — потеря троттлинг-состояния при рестарте безвредна;
- `GetByServerName(name)` — для нового RPC (lowercase-сравнение; хранить `ServerName` в lowercase A-label — канонизация как в [../01-addressing-identity.md](../01-addressing-identity.md)).

## Изменение 4 — валидация federation-регистрации

Если в `RegisterServerRequest.ServerInfo` заполнен `server_name` — регистрация принимается **только** после проверки:

1. Валидация servername + анти-SSRF (публичный hostname, не IP/localhost, punycode-нормализация; после DNS-резолва — отказ приватным диапазонам). Navigator публичен и не имеет общего кода с Federation — продублируй минимальную проверку локально (это осознанное дублирование, отметь комментарием со ссылкой на 03).
2. `GET https://{server_name}/.well-known/barkfluff` по **CA-валидному** HTTPS (без trust-all), таймаут/лимит размера.
3. `server_name` в документе совпадает; signing-ключи из документа содержат все ключи, заявленные в регистрации (сверка по `key_id` + байтам) — иначе отказ.

Фетч не удался / документ невалиден → регистрация с federation-полями отклоняется (существующий механизм ошибок Navigator; посмотри, как он сейчас отвечает на троттлинг, и сделай аналогично). Легаси-регистрация без `server_name` — как раньше, без валидации.

## Изменение 5 — GetServerByName

Реализовать RPC: lowercase-нормализация входа → `GetByServerName` → `found=false`, если нет записи или запись протухла (за пределами активного периода — Navigator не должен отдавать мёртвые ноды как живые; вопрос TTL реши так же, как в `GetServers`).

## Изменение 6 — деплой

- Compose-файлы, где живёт Navigator (проверь `Backend/docker-compose-dev.yml` и prod-компоузы, например `Backend/nginx/docker-compose-msk.yml`): добавить env `NAVIGATOR_DB` и `depends_on` postgres там, где Navigator задеплоен рядом с postgres; если Navigator деплоится отдельно — зафиксируй в README сервиса (или Obsidian), что оператору нужен PostgreSQL и переменная `NAVIGATOR_DB`.
- БД `navigator` создаётся миграцией при старте (connection string указывает на неё; пользователь должен иметь право CREATE — как у остальных сервисов dev-стека).

## Чего НЕ делать

- Не переводить Navigator на `LoadConfiguration`/Serilog/метрики — вне скоупа (осознанно, см. README фазы).
- Не менять контракт `ListServers` (клиенты выбора сервера уже ходят в него).
- Не делать мульти-Navigator (`NavigatorUrl`-список) — №32 в [../09-problems-open-questions.md](../09-problems-open-questions.md), отложено.
- Модерация каталога, UI — вне скоупа.

## Критерии готовности

1. Рестарт Navigator не теряет реестр (зарегистрировать → рестарт контейнера → `ListServers` отдаёт).
2. Легаси-регистрация (запрос без `server_name`) и `ListServers` работают ровно как до этапа — проверить существующим клиентом/скриптом.
3. Регистрация с `server_name`, чей well-known недоступен или ключи не совпадают, — отклоняется; с валидным well-known (нода из стенда 1.3/1.4 + dev: Navigator придётся дать доступ к well-known ноды — на стенде это HTTP-порт 7031 напрямую или nginx из 1.6; для CA-проверки на стенде допусти env-флаг `NAVIGATOR_INSECURE_WELLKNOWN=1`, действующий только вне production-окружения, по аналогии с dev-флагом Federation) — проходит, federation-поля сохранены.
4. `GetServerByName` возвращает зарегистрированную ноду; неизвестную — `found=false`.
5. `dotnet build` Navigator + затронутых проектов — успех.
6. Obsidian: `Obsidian/ClaudeVault/Backend/Navigator.md` обновить (PostgreSQL, federation-поля, валидация, новый RPC, env `NAVIGATOR_DB`).
7. Коммит: `feat(rearch-phase1): 1.5 — Navigator: PostgreSQL + federation-поля + GetServerByName`.
