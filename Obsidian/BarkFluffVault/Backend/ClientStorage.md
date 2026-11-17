# BarkFluff.ClientStorage

Автономный HTTP-сервис дистрибутивов: S3/MinIO хранит объекты, SQLite — метаданные, локальный диск кеширует актуальный файл для каждой пары платформа/канал. Не использует Settings, XAuth, RabbitMQ или gRPC. Структура: [[Backend/ClientStorage-ProjectMap]].

## Контракт

Для платформ Windows, Kotlin, macOS, iOS и WinUI доступны GET /get/barkfluff{platform}[/{channel}] и соответствующий /version. Каналы: release, beta, dev, nightly. Ответ версии и файла содержит metadata/checksum; загрузка поддерживает HTTP Range. BITS url есть только у Windows.

POST /set/barkfluff{platform}[/{channel}] принимает multipart file и требует Authorization: Bearer <UPLOAD_TOKEN>; также читает X-App-Version. Максимальный размер — 512 MiB. UPLOAD_TOKEN обязателен при старте. Пароль сравнивается constant-time.

## Хранение и runtime

БД по умолчанию в контейнере /app/data/clientstorage.db; кеш — /app/cache или CACHE_DIR. При запуске применяются EF migrations, создаётся S3 bucket; CacheWarmupService прогревает текущие версии, OldVersionsCleanupService удаляет старые объекты. Загрузки пишутся сначала в S3 и затем обновляют метаданные и локальный кеш.

Program.cs явно очищает KnownNetworks/KnownProxies для forwarded headers. Это означает, что сервис доверяет переданным proxy headers; размещайте его за контролируемым reverse proxy. Kestrel/формы ограничивают request body до 512 MiB и отключают minimum data rates для медленных файловых передач.

Runtime port задаётся ASPNETCORE_URLS; Docker/nightly/clientstorage/docker-compose.yml требует CLIENTSTORAGE_PORT. Local launch profiles используют 5099/7115. Nginx uploads используют отдельные 512m лимиты: [[Backend/Nginx]].

Сборка: dotnet build Backend/BarkFluff.ClientStorage/BarkFluff.ClientStorage.csproj.
