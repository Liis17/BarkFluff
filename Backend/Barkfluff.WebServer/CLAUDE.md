# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

`Barkfluff.WebServer` — публичный HTTP-сервер (.NET 10, ASP.NET Core MVC) на порту **64641**. Раздаёт HTML-страницы, статику и файлы для скачивания, предоставляет REST API для профилей пользователей и поддерживает чат поддержки через Telegram-бота.

## Build and Run

```bash
dotnet build Barkfluff.WebServer.csproj
dotnet run --project Barkfluff.WebServer.csproj

# Публикация (linux-x64, self-contained)
dotnet publish Barkfluff.WebServer.csproj -c Release -r linux-x64 --self-contained
```

Тестов в проекте нет.

## Architecture

### Controllers

| Controller | Маршрут | Описание |
|------------|---------|----------|
| `HomeController` | `GET /` | Отдаёт `html/barkfluff.html` |
| `InstallController` | `GET /install.ps1` | Отдаёт `files/install.ps1` |
| `DownloadController` | `GET /download/installer` | Отдаёт `files/Barkfluff.Updater.CLI.exe` |
| `FallbackController` | `GET /{**catchAll}` | Обрабатывает все остальные пути |
| `UserApiController` | `GET /api/user/{username}` | REST API профиля пользователя |
| `SupportChatController` | `POST /api/support/send`, `GET /api/support/messages/{chatId}` | Чат поддержки |
| `FaviconController` | — | Favicon |

`FallbackController` — последний по приоритету. Логика: `legal/*` → `LegalPageService`, `selfhosted` → страница selfhosted, иначе → `UserPageService` (страница пользователя).

### Services

- **`UserProfileService`** — запрашивает профили у Users gRPC-сервиса, кеширует 30 мин (не найденных — 5 мин). Использует `IMemoryCache`. Класс `UserProfileData` определён в том же файле.
- **`UserPageService`** — читает `html/userpage.html`, заменяет `%%username%%` на путь запроса.
- **`LegalPageService`** — отдаёт файлы из `html/legal/` по имени страницы.
- **`SupportChatService`** — in-memory хранилище сессий чата поддержки (`ConcurrentDictionary`), thread-safe.
- **`TelegramService`** — Telegram-бот для нотификаций чата поддержки. Входящие сообщения только от adminId. Ответ администратора — reply на сообщение бота, первая строка которого — GUID чата.

### External Dependencies

- **Users gRPC-сервис** (`users_api.proto`, `GrpcServices="Client"`) — `GetUserByUsernameAsync`. Адрес и Service JWT-токен захардкожены в `Program.cs`.
- **Telegram.Bot** (v22.8.1) — интеграция поддержки с Telegram.

### gRPC Client Setup

gRPC-клиент `UsersServerApiClient` создаётся вручную через `GrpcChannel.ForAddress` в `Program.cs` (не через `AddGrpcClient`). Авторизация — ручная передача `x-auth-token` в `Metadata` при каждом вызове (см. `UserProfileService`).

Proto-файлы подключены из `Shared/BarkFluff.Proto/`:
- `users_api.proto` → `GrpcServices="Client"`
- `shared.proto` → `GrpcServices="None"` (только типы)

### Static Files

Все HTML и статика хранятся в проекте с `CopyToOutputDirectory=Always`:
- `html/barkfluff.html` — главная страница
- `html/userpage.html` — шаблон страницы пользователя (плейсхолдер `%%username%%`)
- `html/selfhosted.html` — страница для self-hosted установок
- `html/legal/*.html` — юридические страницы (privacy-policy, terms-of-service, account-deletion, encryption)
- `files/install.ps1` — скрипт установки
- `files/Barkfluff.Updater.CLI.exe` — инсталлятор (не включён в git)

### Adding New Routes

Добавить контроллер в `Controllers/`, объявить `[ApiController]` + `[HttpGet/Post/...]`. Контроллеры регистрируются автоматически через `MapControllers()`. **Важно**: `FallbackController` перехватывает все необработанные пути — специфичные маршруты объявлять до него (ASP.NET Core routing расставляет приоритеты автоматически по специфичности шаблона).

## Configuration Notes

Адрес Users-сервиса (`https://users.barkfluff.com`) и токены захардкожены в `Program.cs`. При необходимости переноса в конфиг использовать `appsettings.json` + `IConfiguration`.
