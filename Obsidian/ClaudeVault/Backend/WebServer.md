# Barkfluff.WebServer

Публичный HTTP-сервер. Порт: **64641** (.NET 10, ASP.NET Core MVC).
Раздаёт HTML-страницы, статику и файлы, предоставляет REST API профилей и чат поддержки через Telegram-бота.

Расположение: `Backend/Barkfluff.WebServer/`

## Сборка

```bash
dotnet build Barkfluff.WebServer.csproj
dotnet publish Barkfluff.WebServer.csproj -c Release -r linux-x64 --self-contained
```

## Controllers

| Controller | Маршрут | Описание |
|------------|---------|----------|
| `HomeController` | `GET /` | Отдаёт `html/barkfluff.html` |
| `InstallController` | `GET /install.ps1` | Скрипт установки |
| `DownloadController` | `GET /download/installer` | `Barkfluff.Updater.CLI.exe` |
| `FallbackController` | `GET /{**catchAll}` | `legal/*` → LegalPageService; `selfhosted` → selfhosted.html; иначе → UserPageService |
| `UserApiController` | `GET /api/user/{username}` | REST API профиля |
| `SupportChatController` | `POST /api/support/send`, `GET /api/support/messages/{chatId}` | Чат поддержки |

**Важно**: `FallbackController` перехватывает все необработанные пути — специфичные маршруты ASP.NET Core расставляет в приоритет автоматически.

## Services

- **`UserProfileService`** — gRPC запросы профилей, кеш 30 мин (не найденных — 5 мин, `IMemoryCache`)
- **`UserPageService`** — читает `html/userpage.html`, заменяет `%%username%%`
- **`LegalPageService`** — файлы из `html/legal/` по имени страницы
- **`SupportChatService`** — in-memory сессии чата (`ConcurrentDictionary`)
- **`TelegramService`** — Telegram-бот для уведомлений чата поддержки. Ответ администратора — reply, первая строка — GUID чата

## gRPC Client

`UsersServerApiClient` создаётся вручную через `GrpcChannel.ForAddress` в `Program.cs` (не через `AddGrpcClient`). `x-auth-token` передаётся вручную в `Metadata` при каждом вызове.

Адрес Users-сервиса (`https://users.barkfluff.com`) и токены **захардкожены** в `Program.cs`.

## Статика

- `html/barkfluff.html` — главная страница
- `html/userpage.html` — шаблон страницы пользователя
- `html/selfhosted.html` — self-hosted
- `html/legal/*.html` — юридические страницы
- `files/install.ps1` — скрипт установки
- `files/Barkfluff.Updater.CLI.exe` — инсталлятор (не в git)

## Proto

- `users_api.proto` — Client
- `shared.proto` — None

## Зависимости

- `Telegram.Bot 22.8.1`
