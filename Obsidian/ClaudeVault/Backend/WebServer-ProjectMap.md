# Barkfluff.WebServer — Карта проекта

Расположение: `Backend/Barkfluff.WebServer/`
Связанная документация: [[Backend/WebServer]]

---

## Controllers

| Файл | Маршрут(ы) | Описание |
|------|-----------|----------|
| `Controllers/HomeController.cs` | `GET /` | Главная страница — читает и отдаёт `html/barkfluff.html` |
| `Controllers/FallbackController.cs` | `GET /{**catchAll}` | Перехватывает все необработанные пути: `legal/*` → LegalPageService; `selfhosted` → LegalPageService; иначе → UserPageService; ничего не подошло → `html/404.html` с кодом 404 |
| `Controllers/UserApiController.cs` | `GET /api/user/{username}` | REST API публичного профиля пользователя (делегирует в UserProfileService) |
| `Controllers/SupportChatController.cs` | `POST /api/support/send`, `GET /api/support/messages/{chatId}` | Чат поддержки: отправка сообщений и получение истории |
| `Controllers/VersionApiController.cs` | `GET /api/versions` | Возвращает актуальные версии клиентов (Android/Windows/macOS, release+beta) |
| `Controllers/InstallController.cs` | `GET /install.ps1`, `GET /installbeta.ps1`, `GET /install.sh`, `GET /installbeta.sh` | Скрипты установки клиента (Windows PowerShell и Linux Bash, релиз и бета) |
| `Controllers/DownloadController.cs` | `GET /download/installer` | Отдаёт `Barkfluff.Updater.CLI.exe` — инсталлятор Windows |
| `Controllers/AssetsController.cs` | `GET /assets/{filename}` | Раздаёт ассеты по whitelist `_allowedFiles`: изображения (png/jpg для магазинов и превью) и `cookie-notice.js` |
| `Controllers/FaviconController.cs` | `GET /favicon.ico` | Отдаёт `files/favicon.ico` |

---

## Services

| Файл | Описание |
|------|----------|
| `Services/UserProfileService.cs` | Запрашивает публичный профиль через gRPC (UsersServerApi). Кеш: найденные — 30 мин, не найденные — 5 мин (`IMemoryCache`). Передаёт `x-auth-token` в Metadata. |
| `Services/UserPageService.cs` | Читает `html/userpage.html`, заменяет `%%username%%` (валидация `^[a-zA-Z0-9_]{3,32}$` + `HtmlEncoder`, не подошло → пустая строка → 404). Спецобработка: `li_is` → `html/UniqueUsers/paws.page.html` (анимированные лапки-следы). |
| `Services/LegalPageService.cs` | Читает HTML из `html/legal/` по имени страницы; обрабатывает `selfhosted.html`. |
| `Services/SupportChatService.cs` | In-memory хранилище сессий чата (`ConcurrentDictionary`). Добавляет сообщения, возвращает историю с пагинацией. |
| `Services/TelegramService.cs` | Telegram-бот поддержки. Отправляет сообщения пользователей администратору. Ответ администратора — reply, первая строка — GUID чата. |
| `Services/VersionStore.cs` | Thread-safe in-memory хранилище версий клиентов (Android/Windows/macOS, release+beta). |
| `Services/VersionPollingService.cs` | `BackgroundService`. Каждые 10 минут опрашивает `storage.barkfluff.com` — получает версии всех клиентов, пишет в `VersionStore`. |

---

## Конфигурация

| Файл | Описание |
|------|----------|
| `appsettings.json` | Базовые настройки ASP.NET Core |
| `appsettings.Development.json` | Настройки для среды разработки |
| `.env.example` | Пример переменных окружения (`WEBSERVER_PORT`, `UsersService:Host`, `UsersService:Token`, `Telegram:BotToken`) |
| `Program.cs` | Точка входа: настройка Kestrel, DI-регистрация сервисов, gRPC-канал к Users-сервису, запуск TelegramService |

---

## Статика (html/)

| Файл/Папка | Описание |
|-----------|----------|
| `html/barkfluff.html` | Главная страница сайта |
| `html/404.html` | Страница «не найдено» (отдаётся `FallbackController` с кодом 404) |
| `html/userpage.html` | Шаблон публичной страницы пользователя (`%%username%%`) |
| `html/UniqueUsers/paws.page.html` | Специальная страница для пользователя `li_is` с анимированными лапками-следами (SVG + CSS keyframes) |
| `html/selfhosted.html` | Страница для self-hosted режима |
| `html/legal/privacy-policy.html` | Политика конфиденциальности |
| `html/legal/terms-of-service.html` | Условия использования |
| `html/legal/account-deletion.html` | Удаление аккаунта |
| `html/legal/encryption.html` | Описание шифрования |
| `html/new/` | **WIP** — редизайн главной страницы (Barkfluff Redesign.html, home.css, profile.html, profile.css, styles.css) |

---

## Файлы (files/)

| Файл | Описание |
|------|----------|
| `files/install.ps1` | Скрипт установки Windows (release) |
| `files/installbeta.ps1` | Скрипт установки Windows (beta) |
| `files/install.sh` | Скрипт установки Linux (release) |
| `files/installbeta.sh` | Скрипт установки Linux (beta) |
| `files/favicon.ico` | Иконка сайта |
| `files/linkpreview.png` | OG-изображение для превью ссылок |
| `files/cookie-notice.js` | Баннер об использовании cookie, подключается во все страницы `html/` |
| `files/barkfluff.windows.png` | Скриншот Windows-клиента |
| `files/barkfluff.web.png` | Скриншот Web-клиента |
| `files/barkfluff.android.jpg` | Скриншот Android-клиента |
| `files/Barkfluff.Updater.CLI.exe` | Инсталлятор (не в git, добавляется при деплое) |

---

## Docker и деплой

| Файл | Описание |
|------|----------|
| `Dockerfile.slim` | Образ для CI и production |
| `docker-compose-dev.yml` | Локальная разработка |
| `docker-compose-master.yml` | Продакшен |
| `Properties/PublishProfiles/FolderProfile.pubxml` | Профиль публикации в папку |

---

## Proto

| Файл | Роль |
|------|------|
| `users_api.proto` | Client — получение публичного профиля пользователя |
| `shared.proto` | None — общие типы |
