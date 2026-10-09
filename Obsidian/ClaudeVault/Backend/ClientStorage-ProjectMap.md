# BarkFluff.ClientStorage — карта проекта

Основное описание и HTTP-контракт: [[Backend/ClientStorage]]. Исходники: Backend/BarkFluff.ClientStorage/.

- Program.cs — SQLite migration, S3 bucket initialization, middleware и HTTP настройки. Сервис автономный: не вызывает Settings, XAuth или gRPC.
- Controllers/ClientStorageController.cs — публичные download/version routes и token-защищённые upload routes.
- Domain/ — ClientType, ReleaseChannel, ClientFile.
- Infrastructure/S3StorageService.cs — S3 API; LocalFileCache.cs — один локальный файл на пару platform/channel.
- Services/ — прогрев кеша и очистка старых S3-объектов.
- Persistence/ — EF Core SQLite и миграции; БД по умолчанию в контейнере /app/data/clientstorage.db.
- Middleware/TokenAuthMiddleware.cs — проверка UPLOAD_TOKEN для /set/*.
- Deployment: Docker/nightly/clientstorage/docker-compose.yml; nginx routes: Docker/nightly/nginx/sites/storage.conf; files-media.conf относится к Files.

Платформы и каналы определяются перечислениями в Domain.
