# Этап 1.7 — AdminPanel: страница «Федерация»

## Цель

Админ ноды управляет федерацией из AdminPanel: видит свои ключи (и ротирует их), список пиров с состоянием, добавляет ручного пира, блокирует/разблокирует ноды.

## Контекст

- **Только `Pages/v2/`** (MD3) — действующее правило проекта: `Pages/Redesigned/` и плоские `Pages/*.html` мертвы, не трогать.
- Устройство AdminPanel: vanilla HTML+JS+Tailwind/MD3 (`Pages/v2/assets/md3.css`), страницы = статический html + minimal-API-эндпоинты в `Backend/Barkfluff.AdminPanel/Endpoints/`, gRPC-клиенты к сервисам регистрируются в `Program.cs` (`AddGrpcClient` + `JwtClientInterceptor`, конфиг `{Service}Service:Host/Token`). **Образцы: возьми страницу `v2/bots.html` (или другую свежую v2-страницу с таблицей + формой) и её endpoint-файл, повторяй их структуру и стиль.** Навигация — сайдбар в `v2/index.html`.
- Internal-API Federation: `federation_internal_api.proto` — `GetFederationStatus`, `GetKnownServers`, `UpsertManualPeer`, `SetServerBlocked` (реализованы в 1.4), `RotateSigningKey` (1.2).
- Что должна уметь страница — [../08-service-migration.md](../08-service-migration.md), раздел AdminPanel (outbox-разделы будут в Фазе 2 — сейчас счётчики нули).
- Ключи `FederationService:Host`/`FederationService:Token` уже раздаются populator'ом (этап 0.1).

## Изменение 1 — gRPC-клиент

- `Barkfluff.AdminPanel.csproj`: `<Protobuf Include="../../Shared/BarkFluff.Proto/federation_internal_api.proto" GrpcServices="Client" />` (+ `federation_api.proto` `GrpcServices="None"` — internal-proto импортирует его типы; если codegen потребует иначе — посмотри, как в AdminPanel подключён `shared.proto`, и повтори схему).
- `Program.cs`: `AddGrpcClient<FederationInternalApi.FederationInternalApiClient>` по образцу соседних клиентов (`FederationService:Host`, дефолт `http://federation:7030`, `JwtClientInterceptor` с `FederationService:Token`).

## Изменение 2 — эндпоинты

`Endpoints/FederationEndpoints.cs` — по образцу соседнего endpoint-файла (та же авторизация/сессии, что у остальных `/api/*`):

| Метод | Маршрут | gRPC | Примечание |
|-------|---------|------|------------|
| GET | `/api/federation/status` | `GetFederationStatus` | server_name, enabled, свои ключи, счётчики |
| GET | `/api/federation/peers` | `GetKnownServers` | |
| POST | `/api/federation/peers` | `UpsertManualPeer` | body: server_name, endpoint, keys[{key_id, public_key_base64}], tls_spki_sha256[] |
| POST | `/api/federation/peers/{server}/block` | `SetServerBlocked` | body: `{ "blocked": true/false }` |
| POST | `/api/federation/keys/rotate` | `RotateSigningKey` | |

Ошибки gRPC (Federation недоступен, невалидный ввод) → человекочитаемый JSON с кодом — как это делают соседние эндпоинты.

## Изменение 3 — страница

`Pages/v2/federation.html`, три секции (MD3-карточки, стиль соседних страниц):

1. **Статус ноды**: server_name (или заметный warning «федерация не сконфигурирована», если пусто), enabled, `known_servers_active`, outbox pending/deadletter (нули до Фазы 2 — подпись «появится в Фазе 2» не нужна, просто числа).
2. **Ключи ноды**: таблица key_id / отпечаток публичного ключа (base64, обрезанный с копированием по клику) / created / expired; кнопка «Ротация ключа» с confirm-диалогом (текст: старый ключ истечёт через N дней, пиры обновятся при следующем рефреше).
3. **Пиры**: таблица server_name / endpoint / source / status / last_seen (относительное время); действия — блок/разблок (confirm); над таблицей — кнопка «Добавить пир» → форма/диалог: server_name, endpoint, публичный ключ (key_id + base64), SPKI-отпечатки. Валидация формы минимальная (непустые server_name/endpoint) — содержательную делает бэкенд.

Адаптив — по свежим правилам v2 (гриды @600px в 1 колонку; проверка на 390px) — посмотри, как это сделано в последних правках соседних страниц.

## Изменение 4 — навигация

Пункт «Федерация» в сайдбар `v2/index.html` (и в мобильный drawer, если он объявлен отдельно) — рядом с родственными админ-разделами, иконка в стиле остальных пунктов.

## Чего НЕ делать

- Никаких правок в `Pages/Redesigned/` и плоских `Pages/*.html`.
- Outbox-операции (ручной retry, dead-letter-просмотр) — Фаза 2/6.
- Алерты/дашборд метрик федерации — Фаза 6.1.
- Не менять чужие страницы/эндпоинты.

## Критерии готовности

1. Страница открывается из сайдбара, статус и ключи отображаются (стенд из 1.3–1.6 или dev-стек с одной нодой).
2. Добавление manual-пира через UI → пир в таблице со `source=Manual`; на стенде `fedping` до него проходит.
3. Блок пира через UI → входящий S2S от него `PermissionDenied` (стенд); разблок возвращает связь.
4. Ротация из UI → в таблице ключей появляется новый key_id, у старого expired; well-known обновился.
5. Мобильная ширина 390px — секции в одну колонку, без горизонтального скролла.
6. Obsidian: `Backend/AdminPanel-ProjectMap.md` (+ `Backend/Federation.md` — пометка про UI) обновлены.
7. Коммит: `feat(rearch-phase1): 1.7 — AdminPanel страница «Федерация»`.
