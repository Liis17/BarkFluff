# Этап 1.1 — Каркас сервиса BarkFluff.Federation

## Цель

Создать микросервис `BarkFluff.Federation` по шаблону платформы: проект, bootstrap, пустая БД, Dockerfile.slim, CI-workflow, запись в docker-compose-dev. Сервис стартует в dev-стеке, отвечает на `Ping`, метрики видны в Seq. **Никакой федеративной логики**: ни ключей, ни подписей, ни discovery.

## Контекст

- Полное описание сервиса — [../04-federation-service.md](../04-federation-service.md): порт 7030, единственная точка входа/выхода федеративного трафика.
- Конфигурация уже заведена этапом 0.1 (коммит `f8f792ad`): каталог Settings знает `ServiceId.Federation` (контейнер `federation`, порт 7030, `FederationDb`), ключи `Federation:Enabled` (false), `FederationService:Host/Token`. Ничего в каталог добавлять не нужно.
- **Образец сервиса** — `Backend/BarkFluff.Onliner/` (компактный, свежий): смотри его `Program.cs`, `.csproj`, структуру папок и повторяй стиль. Всё, что ниже названо «по образцу Onliner», означает: открой соответствующее место в Onliner и сделай так же.

## Изменение 1 — проект

`Backend/BarkFluff.Federation/BarkFluff.Federation.csproj`:

- `TargetFramework` net10.0, версии пакетов — те же, что у Onliner (EF Core + Npgsql, `Grpc.Net.ClientFactory`); **MassTransit не подключать** — консюмеры появятся в Фазе 2.
- ProjectReference: `Backend/BarkFluff.GrpcServer` (+ те shared-библиотеки, которые референсит Onliner, кроме `BarkFluff.Shared.Queue` — она не нужна до Фазы 2).
- Protobuf:

```xml
<Protobuf Include="../../Shared/BarkFluff.Proto/federation_api.proto" GrpcServices="Server" />
<Protobuf Include="../../Shared/BarkFluff.Proto/federation_internal_api.proto" GrpcServices="Server" />
```

(`federation_internal_api.proto` импортирует `federation_api.proto` — если codegen не найдёт импорт, посмотри, как решён такой же импорт `shared.proto` у соседей, и повтори.)

Папки: `Domain/`, `Host/`, `Persistence/`, `Services/` — по мере надобности, как у Onliner. Solution: добавить проект в `BarkFluff.sln` тем же способом, каким включены остальные Backend-проекты.

## Изменение 2 — Program.cs

Bootstrap строго по последовательности Onliner:

1. `builder.LoadConfiguration(ServiceId.Federation)`
2. `builder.AddBarkFluffSerilog("BarkFluff.Federation")`
3. `builder.SetRunningAddress(builder.Configuration)`
4. `AddGrpc()` с `ServerExceptionInterceptor` (как у Onliner)
5. `builder.Services.AddBarkFluffMetrics("BarkFluff.Federation")`
6. `AddGrpcReflection()`
7. `AddDbContext<FederationContext>` — `UseNpgsql(конфиг "FederationDb")` + `EnableRetryOnFailure(3)`
8. `builder.Services.AddXAuth(builder.Configuration)` — для internal API (`TokenType.Service`)
9. `ctx.Database.Migrate()` на старте (scope, как у соседей)
10. `app.UseXAuth()`, `MapGrpcService<...>` для обоих API, reflection в Development
11. Gauge `service_started_unix` — по образцу того, как это делает недавний сервис (посмотри конец Program.cs у Bots).

## Изменение 3 — gRPC-хосты

`Host/FederationS2SApiService.cs` — наследник сгенерированного `FederationS2SApi.FederationS2SApiBase`. Реализовать **только** `Ping`:

- `server_name` — из конфигурации `Federation:ServerName` (может быть пустым — отдавать пустую строку, не падать);
- `protocol_versions` — `[1]`;
- `server_time` — `Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)`;
- `capabilities` — пусто.

Остальные RPC не переопределять (base отдаёт `Unimplemented` — это ожидаемо до следующих этапов). Внимание: в 1.1 `Ping` временно доступен без подписи — XFed появится в 1.3 и закроет его.

`Host/FederationInternalApiService.cs` — наследник internal-base, ни один метод не переопределять (весь API — `Unimplemented` до 1.2/1.4). Требование XAuth-политики `TokenType.Service` на классе — по образцу того, как internal-API защищены у соседей (например `UsersServerApi`-хост в Users).

## Изменение 4 — FederationContext

`Persistence/FederationContext.cs` — пустой DbContext (без DbSet) + миграция `InitialCreate` (создаст только `__EFMigrationsHistory`). Смысл: проверить связку конфиг → БД → Migrate на старте до того, как в 1.2/1.3 появятся таблицы. Про баг `dotnet ef` — правило 5 в [README.md](README.md).

## Изменение 5 — Dockerfile.slim

`Backend/BarkFluff.Federation/Dockerfile.slim` — точная копия соседского (Onliner) с заменой имени dll:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --chown=1654:1654 publish/ .
USER $APP_UID
ENTRYPOINT ["dotnet", "BarkFluff.Federation.dll"]
```

(Перед записью сверь с актуальным `Backend/BarkFluff.Onliner/Dockerfile.slim` — если он отличается от сниппета, прав образец.)

## Изменение 6 — CI workflow

`.github/workflows/build-backend-federation.yml` — клон `build-backend-onliner.yml` с заменами:

- paths-фильтр: `Backend/BarkFluff.Federation/**`, `Backend/BarkFluff.GrpcServer/**`, `rebuild.trigger`;
- имя проекта/dll/образа: `federation` (образ по той же схеме `barkfluff-federation-dev`);
- всё остальное (check-dotnet, Telegram-approve на master, docker-version action, registry `docker.barkfluff.com`) — без изменений.

## Изменение 7 — docker-compose-dev.yml

`docker/backend/docker-compose-dev-backend.yml` (это основной деплой-файл проекта) — добавить сервис по образцу `onliner`:

```yaml
federation:
  image: docker.barkfluff.com/barkfluff-federation-dev:latest
  container_name: federation
  restart: always
  environment:
    <<: *common-variables
  networks:
    - barkfluff-network
  depends_on:
    - configuration
```

(Сверь анкор `common-variables` и имя сети с фактическим файлом.) Порты наружу не публиковать — S2S пойдёт через nginx (этап 1.6).

## Чего НЕ делать

- Ключи, подписи, well-known — 1.2.
- Интерсепторы XFed, SPKI — 1.3.
- KnownServers, discovery — 1.4 (в 1.3 появятся только таблицы).
- MassTransit/RabbitMQ, outbox — Фаза 2.
- Nginx-конфиги — 1.6.

## Критерии готовности

1. `dotnet build Backend/BarkFluff.Federation/BarkFluff.Federation.csproj` — успех; `dotnet build BarkFluff.sln` — успех (остальные проекты не задеты).
2. Локальный/dev-запуск: сервис стартует, `Database.Migrate()` создаёт БД `federation`, в Seq появляются логи и `ServiceMetrics` от `BarkFluff.Federation`.
3. `grpcurl` (reflection): `FederationS2SApi` и `FederationInternalApi` видны; `Ping` отвечает (server_time заполнен); любой другой RPC — `Unimplemented`; internal-RPC без service-токена — `Unauthenticated`/`PermissionDenied`.
4. Существующие сервисы стартуют как раньше.
5. Obsidian: создать `Obsidian/ClaudeVault/Backend/Federation.md` (порт, назначение, ссылка на `docs/rearch/`, текущее состояние «каркас, Ping») + ссылка в `Index.md`.
6. Коммит: `feat(rearch-phase1): 1.1 — каркас сервиса Federation`.
