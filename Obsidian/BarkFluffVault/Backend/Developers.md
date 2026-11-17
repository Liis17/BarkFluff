# Barkfluff.Developers

Портал документации, protobuf-метаданных и error codes. Исходники: Backend/Barkfluff.Developers/. Сборка: dotnet build Backend/Barkfluff.Developers/Barkfluff.Developers.csproj.

## Listener и контракт

- gRPC/gRPC-Web API слушает RunSettings:Port (default 7020), статика SPA — отдельный HTTP/1 listener RunSettings:Http1Port (default 7021). appsettings содержит оба значения.
- DevelopersApi предоставляет GetDocumentationSections, GetDocumentationSection, GetProtoFiles, GetProtoFileContent и GetErrorCodes (Shared/BarkFluff.Proto/developers_api.proto).
- Каждый RPC требует User JWT по DevelopersReader policy. Service token не подходит. gRPC Reflection включается только в Development.
- Proto-файлы отдаются через явный allowlist из PublishedProtoManifest.cs: shared, beacon, identity, users, messages, files, updates, onliner, fast_auth, navigator. Internal/configuration и прочие proto не входят.
- CORS разрешает настроенные origins и только POST/OPTIONS для gRPC-Web.

Developers использует PostgreSQL. На старте применяет миграции, добавляет отсутствующие записи документации/metadata/error codes и останавливает запуск, если published proto или metadata отсутствуют. SeedData, manifest и initializer — источники начального содержимого и проверок.

Общие health routes маппятся отдельно от защищённого API.
