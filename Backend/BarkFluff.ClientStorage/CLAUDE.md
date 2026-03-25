# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

BarkFluff.ClientStorage — автономный микросервис для хранения и раздачи клиентских приложений BarkFluff (Windows, Kotlin/Android). REST API на ASP.NET 9.0, файлы хранятся в S3-совместимом хранилище (Minio), метаданные — в SQLite.

**Не входит** в основную микросервисную инфраструктуру BarkFluff (нет gRPC, нет MassTransit, нет XAuth, нет Configuration service). Работает полностью изолированно.

## Build & Run

```bash
# Сборка
dotnet build BarkFluff.ClientStorage.csproj

# Локальный запуск (требуются env-переменные, см. ниже)
dotnet run

# Docker (из корня репозитория)
docker-compose -f Backend/BarkFluff.ClientStorage/docker-compose-dev.yml up -d
```

### Миграции EF Core (SQLite)

```bash
# Применяются автоматически при старте (Program.cs → db.Database.Migrate())
# Ручное создание новой миграции:
dotnet ef migrations add <Name> --project BarkFluff.ClientStorage.csproj
```

Design-time factory: `Persistence/ClientStorageContextFactory.cs` (БД файл: `clientstorage.db`).

## Required Environment Variables

| Variable | Description |
|----------|-------------|
| `S3_ACCESS_KEY` | Ключ доступа к S3/Minio |
| `S3_SECRET_KEY` | Секретный ключ S3/Minio |
| `S3_SERVICE_URL` | URL S3 endpoint (default: `http://localhost:9000`) |
| `S3_BUCKET_NAME` | Имя бакета (default: `client-storage`) |
| `UPLOAD_TOKEN` | **Обязательный** Bearer-токен для POST-эндпоинтов (`/set/*`) |

## Architecture

Простой REST-сервис без CQRS/MediatR:

- **Controllers/** — единственный `ClientStorageController` с GET (скачивание) и POST (загрузка) эндпоинтами
- **Domain/** — `ClientFile` (сущность), `ClientType` (Windows/Kotlin), `ReleaseChannel` (Release/Beta)
- **Infrastructure/** — `S3StorageService` (AWS SDK, S3-совместимый клиент)
- **Middleware/** — `TokenAuthMiddleware` — Bearer-токен авторизация только для `/set/*` маршрутов; `/get/*` — публичные
- **Persistence/** — EF Core + SQLite, `ClientStorageContext`

## API Endpoints

Download (публичные):
- `GET /get/barkfluff{windows|kotlin}[/{channel}]` — скачать клиент
- `GET /get/barkfluff{windows|kotlin}[/{channel}]/version` — получить версию

Upload (требуют `Authorization: Bearer <UPLOAD_TOKEN>`, заголовок `X-App-Version` для версии):
- `POST /set/barkfluff{windows|kotlin}[/{channel}]` — загрузить клиент (multipart form, поле `file`)

Каналы: `release` (default), `beta`. Лимит: 512 MB.

## Key Implementation Details

- Файлы загружаются во временный файл → SHA256 checksum → S3 с GUID-ключом → запись в SQLite
- При скачивании берётся последний файл по `UploadedAt` для данного `ClientType` + `ReleaseChannel`
- S3 бакет создаётся автоматически при старте (`InitializeBucketAsync`)
- Kestrel `MaxRequestBodySize` = 512 MB
