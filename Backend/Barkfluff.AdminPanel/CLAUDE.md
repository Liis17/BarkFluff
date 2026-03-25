# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

AdminPanel — веб-дашборд администратора для платформы BarkFluff. ASP.NET Minimal APIs (.NET 10), vanilla HTML+JS+Tailwind фронтенд, LiteDB для хранения токенов и кеша метрик.


## Build & Run

```bash
dotnet build Barkfluff.AdminPanel.csproj
dotnet run --project Barkfluff.AdminPanel.csproj
```

Сервис слушает на `http://0.0.0.0:51888`. При старте загружает конфигурацию из Configuration service (`builder.LoadConfiguration`), затем переопределяет env-переменными.

## Architecture

### Auth Flow (Telegram-based)

Вход без пароля — через подтверждение в Telegram:

1. Пользователь вводит username на Login.html → `POST /api/auth/request`
2. `AuthService` находит админа по username в `TelegramSettings.ParsedAdmins`
3. `TelegramBotService` отправляет запрос подтверждения в Telegram
4. Фронтенд поллит `GET /api/auth/status/{requestId}`
5. После approve — `TokenService` создаёт токен в LiteDB, устанавливает cookie `auth_token`

`TokenAuthMiddleware` проверяет cookie на каждый запрос. Публичные эндпоинты: `/api/auth/request`, `/api/auth/status`.

### Конфигурация админов

`Telegram:Admins` — строка формата `"userId1:username1,userId2:username2"`. Парсится в `AdminUser.Parse()` (определён в `Program.cs`).

### Data Layer — LiteDB (не EF Core)

- `TokenDbContext` — хранение auth-токенов (`db/tokens.db`)
- `MetricsCacheDbContext` — кеш метрик из Seq: HourlyStats, HourlyTraffic, HourlyServiceMetrics (`db/metrics_cache.db`)

Оба контекста — Singleton, пути настраиваются через `LiteDb:Path` и `MetricsCache:Path`.

### gRPC Clients

Подключается к бэкенд-сервисам как gRPC-клиент (не сервер):
- `UsersServerApi` — управление пользователями, бейджи
- `FilesServerApi` — работа с файлами/S3
- `IdentityServerApi` — авторизация
- `ConfigurationApi` — конфигурация сервисов

Config keys: `{Service}Service:Host` и `{Service}Service:Token` (UsersService, FilesService, IdentityService, ConfigurationService).

### Frontend

Статические HTML-файлы в `Pages/`, копируются в output (`CopyToOutputDirectory=Always`). Шаблонизация минимальная — `{{SERVER_STARTED_AT_UTC}}` заменяется в `ServeHtmlFile()`. Маршруты страниц определены явно в `Program.cs`.

### Services

- `DockerService` — управление Docker-контейнерами
- `SeqService` — проксирование логов из Seq (HttpClient)
- `S3BrowserService` — браузер S3/Minio (AWSSDK.S3)
- `MetricsCollectorService` — фоновый сбор метрик (IHostedService)
- `TelegramBotService` — Telegram-бот для авторизации и уведомлений (IHostedService + Singleton)

### Endpoint Groups

Каждый файл в `Endpoints/` — extension method `Map{Name}Endpoints()` для минимальных API. Добавление нового эндпоинта: создать метод в существующем файле или новый файл + вызов в `Program.cs`.

## Proto Files

Подключены как Client: `users_api.proto`, `files_api.proto`, `identity_api.proto`. `shared.proto` подключён как `GrpcServices="None"` (только типы).

## Key Dependencies

- `LiteDB 5.0.21` — embedded NoSQL (вместо PostgreSQL/EF Core)
- `Telegram.Bot 22.0.2` — Telegram Bot API
- `AWSSDK.S3` — S3-совместимое хранилище (Minio)
- `BarkFluff.GrpcServer` — LoadConfiguration, общая инфраструктура
- `BarkFluff.Shared.Auth` — JwtClientInterceptor для gRPC-клиентов
