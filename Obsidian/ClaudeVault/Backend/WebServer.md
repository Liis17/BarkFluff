# Barkfluff.WebServer

Публичный HTTP-сервер. Порт: **64641** (.NET 10, ASP.NET Core MVC).
Раздаёт HTML-страницы, статику и файлы, предоставляет REST API профилей, версий клиентов и чат поддержки через Telegram-бота.

Расположение: `Backend/Barkfluff.WebServer/`

> 📂 Подробная карта всех файлов и классов: [[Backend/WebServer-ProjectMap]]

## Сборка

```bash
dotnet build Barkfluff.WebServer.csproj
dotnet publish Barkfluff.WebServer.csproj -c Release -r linux-x64 --self-contained
```

## Controllers

| Controller | Маршрут | Описание |
|------------|---------|----------|
| `HomeController` | `GET /` | Отдаёт `html/barkfluff.html` |
| `InstallController` | `GET /install.ps1`, `GET /installbeta.ps1`, `GET /install.sh`, `GET /installbeta.sh` | Скрипты установки (Windows и Linux, release и beta) |
| `DownloadController` | `GET /download/installer` | `Barkfluff.Updater.CLI.exe` |
| `FallbackController` | `GET /{**catchAll}` | `legal/*` → LegalPageService; `selfhosted` → LegalPageService; иначе → UserPageService |
| `UserApiController` | `GET /api/user/{username}` | REST API профиля |
| `VersionApiController` | `GET /api/versions` | Версии клиентов (Android/Windows/macOS, release+beta) |
| `SupportChatController` | `POST /api/support/send`, `GET /api/support/messages/{chatId}` | Чат поддержки |
| `AssetsController` | `GET /assets/{filename}` | Ассеты (изображения для магазинов и превью) |
| `FaviconController` | `GET /favicon.ico` | Иконка сайта |

**Важно**: `FallbackController` перехватывает все необработанные пути — специфичные маршруты ASP.NET Core расставляет в приоритет автоматически.

## Services

- **`UserProfileService`** — gRPC запросы профилей, кеш 30 мин (не найденных — 5 мин, `IMemoryCache`). Возвращает `UserProfileData` включая `ProfilePosterUrl`.
- **`UserPageService`** — читает `html/userpage.html`, заменяет `%%username%%`. Специальная обработка: если username == `li_is` (без учёта регистра), отдаётся `html/UniqueUsers/paws.page.html` с анимированными лапками на фоне.

> **Кнопка «Написать в браузере» (`#webChatBtn`)** в обоих шаблонах (`userpage.html` + `paws.page.html`): при клике пишет cookie `bf_open_chat=<username>` (`domain=.barkfluff.com`, `max-age=300`, `SameSite=Lax`) и редиректит на `https://web.barkfluff.com`. Веб-мессенджер [[Backend/Web]] после загрузки читает эту cookie и открывает чат с пользователем (см. `maybeOpenChatFromCookie` в `main.js`). Логика повторяет deep-link Android (`bf://user-username=<username>`).
- **`LegalPageService`** — файлы из `html/legal/` по имени страницы; также обрабатывает `selfhosted.html`
- **`SupportChatService`** — in-memory сессии чата (`ConcurrentDictionary`)
- **`TelegramService`** — Telegram-бот для уведомлений чата поддержки. Ответ администратора — reply, первая строка — GUID чата
- **`VersionStore`** — thread-safe in-memory хранилище актуальных версий клиентов (Android/Windows/macOS, release+beta)
- **`VersionPollingService`** — `BackgroundService`, каждые 10 минут опрашивает `storage.barkfluff.com` и обновляет `VersionStore`

## REST API — `/api/user/{username}`

Возвращает публичный профиль пользователя:

```json
{
  "found": true,
  "firstName": "...",
  "lastName": "...",
  "username": "...",
  "bio": "...",
  "profilePicture": "https://...",
  "profilePosterUrl": "https://..."   // пусто если постер не задан
}
```

`profilePosterUrl` — URL постера/обложки профиля (горизонтальное изображение 3:1, отображается за аватаром в `.pc-cover`).

## gRPC Client

`UsersServerApiClient` создаётся вручную через `GrpcChannel.ForAddress` в `Program.cs` (не через `AddGrpcClient`). `x-auth-token` передаётся вручную в `Metadata` при каждом вызове.

Адрес Users-сервиса и токен читаются из конфигурации (`UsersService:Host` / `UsersService:Token`) в `Program.cs` — без hardcoded-значений.

## Статика

- `html/barkfluff.html` — главная страница
- `html/userpage.html` — шаблон страницы пользователя
- `html/UniqueUsers/paws.page.html` — **специальная** страница для пользователя `li_is`: как userpage, но с анимированными полупрозрачными лапками-следами (SVG, CSS keyframes) на заднем плане
- `html/selfhosted.html` — инструкция по развёртыванию своей ноды, см. ниже
- `html/legal/*.html` — юридические страницы **для сайта** (RU+EN в одном файле через `<article data-lang>`, переключатель на клиенте)
- `html/legal/*.md` — те же документы **для клиентов**, см. ниже
- `html/new/` — **WIP** редизайн главной страницы (Barkfluff Redesign.html, profile.html, стили)
- `files/install.ps1`, `files/installbeta.ps1` — скрипты установки Windows
- `files/install.sh`, `files/installbeta.sh` — скрипты установки Linux
- `files/Barkfluff.Updater.CLI.exe` — инсталлятор (не в git)

## Юридические документы — два формата

`html/legal/` содержит каждый документ в двух видах, и они обслуживают разных потребителей:

| Формат | Кто использует | Локализация |
|--------|----------------|-------------|
| `*.html` (kebab-case: `privacy-policy.html`) | сайт через `LegalPageService` | RU+EN в одном файле, `<article data-lang>` + `localStorage.bf_lang` |
| `*.md` (`PRIVACY_POLICY.<lang>.md`) | мобильные клиенты, попадают в сборку | отдельный файл на локаль |

Markdown-версии:

- `TERMS_OF_SERVICE.{ru,en,de,es,zh-CN}.md`, `PRIVACY_POLICY.{ru,en,de,es,zh-CN}.md`
- `ACCOUNT_DELETION.md`, `ENCRYPTION.md` — только RU, в онбординге клиентов не показываются
- **`.ru.md` — оригинал**, остальные локали переведены с него; во всех не-русских версиях стоит оговорка о преимущественной силе русской версии
- структура заголовков, нумерация разделов и таблицы совпадают 1:1 между локалями — это нужно, чтобы сверять их при обновлении документа
- дата «Последнее обновление» в шапке используется клиентами как **версия редакции**: изменилась дата — согласие запрашивается заново
- в `.csproj` включены маской `html\legal\*.md`, новая локаль не требует правки проекта

⚠️ При правке документа надо обновить **и** `.html` (сайт), **и** соответствующие `.md` (клиенты) — автоматической синхронизации между форматами нет.

Android-клиент забирает `.md` на этапе сборки gradle-таском `copyLegalDocs`, см. [[Клиенты/Android]].

### Модель ответственности (редакция от 29.07.2026)

Документы переписаны под федеративную схему ([[Backend/Federation]]):

- **оператор персональных данных — администратор ноды**, на которой создан аккаунт; разработчик публикует ПО, чужие ноды не хостит, доступа к их данным не имеет и оператором не является;
- тексты написаны от лица ноды **barkfluff.com**; администратор другой ноды публикует свою редакцию;
- контакты разделены: `privacy@`/`support@`/`legal@` — администратор ноды, `security@` — разработчик (уязвимости и протокол);
- в Privacy добавлен раздел «Федеративный обмен данными» (что уходит на другую ноду и когда), в ToS — «Роли и зона ответственности» + «Что означает федерация для вас», в Account Deletion — «Границы удаления в федерации», в Encryption — «Защита канала между нодами» и сводка «кто что видит».

Утверждения, которые надо пересматривать при развитии федерации, — они сверены с кодом на дату редакции:

| Утверждение в документах | Основание |
|---|---|
| Федеративные чаты только 1-к-1, групп нет | roadmap: федеративные группы — «Дальше» |
| Федеративные сообщения **не** E2E; private/secret — только внутри ноды | roadmap: федеративные E2E — «Дальше» |
| Удаление аккаунта по федерации **не** распространяется | этап 2.9 не сделан: `ProfileChanged`/`UserDeactivated` → `EventStatus.Retry` в `FederationS2SApiService` |
| Файлы не реплицируются, отдаются потоком по запросу | этап 3.2, `FetchFile` |
| Федерация выключена по умолчанию | `Federation:Enabled = false` |
| Можно запретить входящие федеративные ЛС | `DenyFederatedDm` в [[Backend/Users]] |

## Страница self-hosted (редакция от 29.07.2026)

`html/selfhosted.html` — руководство администратора ноды. Раздаётся `LegalPageService` по пути `/selfhosted`, RU-only (в отличие от `legal/*.html` — переключателя языка нет).

Содержимое выведено из кода, а не из общих слов; при изменениях в этих местах страницу надо править:

| Блок страницы | Источник |
|---|---|
| `docker-compose.yml` | `docker/backend/docker-compose-dev-backend.yml`, образы без `-dev` (реальное имя релизного образа — `image:` в `.github/workflows/build-backend-*.yml`) |
| `.env` | `docker/backend/sample-backend.env` |
| Таблица субдоменов | `server_name` в `docker/nginx/*.conf` + `SubdomainNames` в `ConfigurationDefaultsPopulator` |
| «Остаются пустыми» (`Email`, `ServerProps`, `ServerColor`) | seed-миграции Configuration; `ResolveDefault` эти секции не обрабатывает |
| «Автозаполнение поставило заглушку» | `ResolveDefault`: `ExternalEndpoint:Host` → `*.example.com`, `S3Buckets:*` → `minioadmin`, `LiveKit` → `devkey`/`devsecret_change_me…`, `NavigatorUrl` → `http://navigator:7010` |
| Федерация | `Federation:ServerName`/`ExternalEndpoint`/`TlsSpkiSha256` пустые по замыслу, `Enabled=false` — см. [[Backend/Federation]] |

Отличия релизного compose на странице от dev-варианта: образы без `-dev`; порт postgres не публикуется; `seq`/`minio` без проброса портов; `minio` раскомментирован; у `livekit` 7880 не публикуется (за nginx); убраны сервисы и переменные, специфичные для инфраструктуры barkfluff.com (`developers`, `Mail__*`, `RemoteServers__*`, `TELEGRAM_PROXY_*`).

Расхождение в `sample-backend.env`: `CONFIGURATION_SERVICE_HOST=http://configuration:7010`, хотя Configuration слушает **7003** (`ConfigurationService:Host` в миграции `20260308000000` и дефолт в `WebApplicationBuilderExtensions`). На странице указан 7003.

## Proto

- `users_api.proto` — Client
- `shared.proto` — None

## Зависимости

- `Telegram.Bot 22.10.0.1`
# Метрики

WebServer отправляет в [[Backend/GrpcServer]] HTTP-запросы и необработанные HTTP-ошибки, успешные скачивания installer и сохранённые обращения в support-чат (`support_requests`). Эти показатели доступны в [[Backend/AdminPanel]].
