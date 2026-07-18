# BarkFluff.Federation

Сервис межсерверной федерации (S2S). Порт: **7030** (.NET 10). Единственная точка входа/выхода федеративного трафика ноды.

Контекст решений — [[../../../docs/rearch/04-federation-service|docs/rearch/04-federation-service.md]] и остальные доки `docs/rearch/`; планы реализации по этапам — `docs/rearch/phase-1/`.

Расположение: `Backend/BarkFluff.Federation/`

## Текущее состояние: каркас (этап 1.1)

Реализован только `Ping` (S2S API, без подписи — временно, до этапа 1.3 XFed). Остальные RPC обоих API отвечают `Unimplemented`. Никакой федеративной логики: ни ключей, ни подписи, ни discovery — появятся в следующих этапах Фазы 1.

Федерация по умолчанию выключена (`Federation:Enabled = false`); при пустом `Federation:ServerName` сервис стартует нормально, S2S-функции честно отвечают, что нода не сконфигурирована (там, где это уже реализовано).

## Сборка

```bash
dotnet build Backend/BarkFluff.Federation/BarkFluff.Federation.csproj
```

Миграции (`FederationContext`) применяются автоматически при старте (`Database.Migrate()`). В 1.1 БД пустая — только `__EFMigrationsHistory`.

## gRPC API

- `FederationS2SApi` (`federation_api.proto`) — S2S-трафик, авторизация вне XAuth (Ed25519-подпись, XFed — этап 1.3). Реализован только `Ping`.
- `FederationInternalApi` (`federation_internal_api.proto`) — внутренний API (для AdminPanel и других сервисов ноды), XAuth `TokenType.Service`. В 1.1 ни один метод не реализован.

## Конфигурация

- `FederationDb` — PostgreSQL connection string.
- `Federation:Enabled` — bool, дефолт `false`.
- `Federation:ServerName` / `Federation:ExternalEndpoint` — пустые по умолчанию, оператор ноды задаёт сам.
- `FederationService:Host/Token` — ключи для клиентов сервиса Federation (populator, этап 0.1).

## Планы дальнейших этапов

См. `docs/rearch/phase-1/README.md` — 1.2 (Ed25519-ключи, well-known), 1.3 (XFed-подписи, SPKI-пиннинг), 1.4 (discovery, KnownServers), 1.6 (nginx), 1.7 (AdminPanel).
