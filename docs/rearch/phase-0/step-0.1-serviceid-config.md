# Этап 0.1 — ServiceId.Federation и ключи конфигурации

## Цель

Зарегистрировать будущий сервис Federation в инфраструктуре платформы: enum `ServiceId`, дефолтные ключи конфигурации в Configuration-сервисе. **Сам сервис Federation не создаётся** — это Фаза 1. После этапа ни один существующий сервис не меняет поведение.

## Контекст

Каждый сервис платформы при старте вызывает `builder.LoadConfiguration(ServiceId.Xxx)` и получает свою конфигурацию из Configuration-сервиса (gRPC, порт 7003). Дефолтные значения пустых ключей заполняет `ConfigurationDefaultsPopulator` при старте Configuration. Полное описание будущего сервиса — [../04-federation-service.md](../04-federation-service.md).

## Изменение 1 — enum ServiceId

**Файл:** `Shared/BarkFluff.Shared.Identity/ServiceId.cs`

Текущее состояние: enum заканчивается на `Bots = 14`. Добавить в конец:

```csharp
    Federation = 15,
```

Ничего больше в файле не трогать.

## Изменение 2 — дефолты конфигурации Federation

**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs`

Изучи файл перед правкой: он при старте заполняет конфигурации, у которых `Value == ""`, дефолтами, сгруппированными по секциям и `ServiceId`. Действуй **строго по аналогии с существующим сервисом** — лучший образец: как заведены `Bots`/`Calls` (недавние сервисы с `RunSettings`, БД и service-токеном).

Добавить для `ServiceId.Federation`:

| Секция:Ключ | Дефолт | Комментарий |
|-------------|--------|-------------|
| `RunSettings:Host` | по аналогии с другими сервисами (например `http://0.0.0.0`) | |
| `RunSettings:Port` | `7030` | порт закреплён в [../04-federation-service.md](../04-federation-service.md) |
| `FederationDb` | connection string по шаблону остальных БД (host/db/user/pass как у `MessagesDb` и т.п., имя БД `federation`) | |
| `Federation:ServerName` | `""` (пустой — оператор ноды обязан задать сам) | DNS-домен ноды, глобальное имя в сети |
| `Federation:Enabled` | `false` | федерация выключена по умолчанию до Фазы 1+ |
| `Federation:ExternalEndpoint` | `""` | публичный S2S-адрес, заполнит оператор |

И **глобально доступные** ключи для будущих клиентов Federation-сервиса (по аналогии с `BotsService`/`UsersService` и т.д.):

| Секция:Ключ | Дефолт |
|-------------|--------|
| `FederationService:Host` | `http://localhost:7030` (либо docker-имя по образцу соседей — посмотри, как задан `BotsService:Host`) |
| `FederationService:Token` | автогенерация service-токена — тем же механизмом, каким populator генерирует токены остальных сервисов (TTL 10 лет) |

Важно:
- Если populator устроен как список/словарь дефолтов — просто дополни его; не меняй логику заполнения.
- Ключи `Federation:SigningKey` **не заводить** — генерация ключей это Фаза 1 (сервис сгенерирует и запишет сам).
- Секции `RabbitMQ`, `Redis`, `Seq` для Federation отдельно не нужны, если populator раздаёт их как общие (`ServiceId.Unknown`) — проверь и не дублируй.

## Изменение 3 — проверка ServiceId в БД Configuration

`GetConfiguration` фильтрует записи по `ServiceId == запрошенный || ServiceId == Unknown`. Убедись, что `ServiceId` хранится как int (новое значение 15 не требует миграции) — если где-то есть маппинг/валидация enum по списку, дополни. Если в Configuration есть миграции-seed для сервисов — **не** добавляй seed-миграцию: populator покрывает задачу.

## Чего НЕ делать

- Не создавать проект `BarkFluff.Federation` (Фаза 1).
- Не добавлять nginx-конфиги, docker-compose записи, CI workflow (Фаза 1).
- Не трогать другие enum'ы (`TokenType` не расширяется — Federation использует обычный Service-токен для internal API).

## Критерии готовности

1. `dotnet build Shared/BarkFluff.Shared.Identity/BarkFluff.Shared.Identity.csproj` — успех.
2. `dotnet build Backend/BarkFluff.Configuration/BarkFluff.Configuration.csproj` — успех.
3. Запуск Configuration локально (или в dev-compose): в логах/БД видно, что ключи `Federation*` и `FederationService*` создались с дефолтами; существующие ключи не перезаписаны.
4. Любой существующий сервис (например Users) стартует как раньше — его конфигурация не изменилась.
5. Obsidian: `Obsidian/ClaudeVault/Backend/Configuration.md` — дополнить список секций populator'а новыми ключами; `Obsidian/ClaudeVault/Shared/Identity.md` — упомянуть `Federation = 15`.
6. Коммит: `feat(rearch-phase0): 0.1 — ServiceId.Federation + конфиг-дефолты Federation`.
