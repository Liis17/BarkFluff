# BarkFluff.ClientStorage — Карта проекта

Полный список файлов микросервиса и их назначение.
Расположение: `Backend/BarkFluff.ClientStorage/`

→ Основная документация: [[Backend/ClientStorage]]

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа. Регистрирует DI (`S3StorageService`, `LocalFileCache`, `CacheWarmupService`, `OldVersionsCleanupService`, EF Core/SQLite), настраивает Kestrel (лимит 512 MB), `ForwardedHeaders`, применяет миграции БД при старте, инициализирует S3-бакет. |
| `appsettings.json` | Базовая конфигурация (Logging, AllowedHosts). |
| `appsettings.Development.json` | Конфигурация для среды разработки. |

---

## Controllers

| Файл | Назначение |
|------|-----------|
| `Controllers/ClientStorageController.cs` | Единственный контроллер. Обслуживает все REST-эндпоинты: GET `/get/barkfluff{platform}[/{channel}]` — скачивание дистрибутива (кеш → S3 fallback, Range-запросы), GET `.../version` — версия файла, GET `.../bitsurl` — метаданные + URL для Windows BITS, POST `/set/barkfluff{platform}[/{channel}]` — загрузка нового дистрибутива (SHA-256 inline, S3 upload, фоновое обновление кеша). |

---

## Domain

| Файл | Назначение |
|------|-----------|
| `Domain/ClientFile.cs` | EF Core-сущность. Хранит метаданные дистрибутива: `ClientType`, `ReleaseChannel`, `OriginalFileName`, `S3Key`, `ContentType`, `FileSize`, `Checksum` (SHA-256 hex), `UploadedAt`, `Version` (из заголовка `X-App-Version`). |
| `Domain/ClientType.cs` | Enum: `Windows`, `WinUI`, `Kotlin`, `MacOS`, `iOS` — поддерживаемые платформы. |
| `Domain/ReleaseChannel.cs` | Enum: `Release`, `Beta`, `Dev`, `Nightly` — каналы выпуска. |

---

## Infrastructure

| Файл | Назначение |
|------|-----------|
| `Infrastructure/S3StorageService.cs` | Singleton-сервис для работы с S3/MinIO (AWS SDK). Методы: `InitializeBucketAsync` (создаёт бакет если отсутствует), `UploadAsync` (загрузка потока в S3), `DownloadAsync` (стриминговое скачивание, поддержка Range-заголовка). Возвращает `S3DownloadResult : IDisposable` чтобы не буферизировать поток. |
| `Infrastructure/LocalFileCache.cs` | Singleton-сервис локального дискового кеша (`CACHE_DIR`, default `/app/cache`). Ключ кеша: `{clienttype}_{channel}` (например `windows_release`). `GetCachedFilePath` — проверить наличие, `UpdateAsync` — атомарное обновление через `.tmp`-файл и `File.Move`. |

> **Примечание:** Класса `HashingReadStream` в проекте нет. SHA-256 вычисляется инлайн в `UploadClient` через `System.Security.Cryptography.IncrementalHash` за один проход перед отправкой в S3.

---

## Services

| Файл | Назначение |
|------|-----------|
| `Services/CacheWarmupService.cs` | `IHostedService`. При старте контейнера асинхронно (фоново) перебирает все комбинации `ClientType × ReleaseChannel`, проверяет наличие файла в БД и кеше, при отсутствии кеша — скачивает из S3 и сохраняет. Пропускает комбинации без загруженного файла. |
| `Services/OldVersionsCleanupService.cs` | `IHostedService`. При старте удаляет записи и S3-объекты старше 7 дней, кроме последней версии каждой пары `ClientType × ReleaseChannel`; каждые 3 часа оставляет максимум 3 версии, ежедневно в 03:00 по Москве оставляет только последнюю версию по каждой паре. |

---

## Middleware

| Файл | Назначение |
|------|-----------|
| `Middleware/TokenAuthMiddleware.cs` | Защита `/set/*` эндпоинтов. Проверяет заголовок `Authorization: Bearer <UPLOAD_TOKEN>`. Возвращает `401 Unauthorized` при отсутствии или неверном токене. GET-эндпоинты пропускаются без проверки. |

---

## Persistence

| Файл | Назначение |
|------|-----------|
| `Persistence/ClientStorageContext.cs` | EF Core DbContext. Единственный `DbSet<ClientFile>`. |
| `Persistence/ClientStorageContextFactory.cs` | `IDesignTimeDbContextFactory` — для `dotnet ef migrations`. Использует `clientstorage.db` (локальный SQLite). |
| `Persistence/Migrations/20260314144031_InitialCreate.cs` | Миграция: создание таблицы `ClientFiles`. |
| `Persistence/Migrations/20260315163805_AddReleaseChannel.cs` | Миграция: добавление колонки `ReleaseChannel`. |
| `Persistence/Migrations/20260324185526_AddVersion.cs` | Миграция: добавление колонки `Version` (nullable string). |
| `Persistence/Migrations/ClientStorageContextModelSnapshot.cs` | EF-снимок модели (автогенерация). |

---

## Инфраструктура развёртывания

| Файл | Назначение |
|------|-----------|
| `Dockerfile.slim` | Образ на базе `mcr.microsoft.com/dotnet/aspnet`, используемый CI и production. |
| `docker-compose-dev.yml` | Локальная среда разработки (сервис + MinIO). |
| `docker-compose-master.yml` | Production-окружение. |
| `.env.example` | Пример переменных окружения (`S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_SERVICE_URL`, `S3_BUCKET_NAME`, `UPLOAD_TOKEN`, `CACHE_DIR`, `PUBLIC_BASE_URL`). |
| `BarkFluff.ClientStorage.http` | HTTP-файл для тестирования эндпоинтов в VS/Rider. |
| `Properties/launchSettings.json` | Профили запуска для локальной разработки. |

---

## Зависимости (NuGet)

- `AWSSDK.S3` — клиент для S3/MinIO
- `Microsoft.EntityFrameworkCore.Sqlite` — SQLite через EF Core
- `Microsoft.EntityFrameworkCore.Design` — `dotnet ef migrations`

---

## Поток данных

```
POST /set/{platform}          GET /get/{platform}
       │                              │
       ▼                              ▼
TokenAuthMiddleware         LocalFileCache.GetCachedFilePath
       │                         hit ─┘  miss
       ▼                                  │
ClientStorageController                   ▼
       │                          S3StorageService.DownloadAsync
       ├─ IncrementalHash (SHA-256)       │
       ├─ S3StorageService.UploadAsync    │
       ├─ ClientFile → SQLite            ◄┘
       └─ UpdateCacheInBackgroundAsync (сначала из локального temp-файла загрузки; при неудаче — скачивает из S3)
              │
              └─ S3 → LocalFileCache.UpdateAsync
```
