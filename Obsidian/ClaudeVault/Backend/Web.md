# BarkFluff.Web

gRPC-Web reverse proxy + static file server для веб-клиента. Порт: **7016**.

Расположение: `Backend/BarkFluff.Web/`

📁 **Детальная карта файлов и классов:** [[Backend/Web-ProjectMap]]

## Сборка

```bash
dotnet build BarkFluff.Web.csproj
docker-compose -f docker-compose-dev.yml up web
```

## Архитектура

Три функции:
1. **Статика** — раздаёт `wwwroot/` (index.html, messenger.html, JS-модули)
2. **gRPC-Web прокси** — `Grpc.AspNetCore.Web` конвертирует `application/grpc-web-text` (HTTP/1.1) → HTTP/2 gRPC; YARP проксирует к бэкенд-сервисам
3. **HTTP upload прокси** — `POST /api/files/upload/{uploadId}` → Files-сервис

## YARP Routes

| Route | Backend | Protocol |
|-------|---------|----------|
| `/barkfluff.identity.IdentityApi/{**catch-all}` | Identity (7000) | gRPC/HTTP2 |
| `/barkfluff.messages.MessagesApi/{**catch-all}` | Messages (7007) | gRPC/HTTP2 |
| `/barkfluff.users.UsersApi/{**catch-all}` | Users (7001) | gRPC/HTTP2 |
| `/barkfluff.files.FilesApi/{**catch-all}` | Files (7005) | gRPC/HTTP2 |
| `/barkfluff.updates.UpdatesApi/{**catch-all}` | Updates (7015) | gRPC/HTTP2 |
| `/barkfluff.onliner.OnlinerApi/{**catch-all}` | Onliner (7009) | gRPC/HTTP2 |
| `/barkfluff.fast.auth.FastAuthApi/{**catch-all}` | FastAuth (7008) | gRPC/HTTP2 (server-streaming) |
| `/api/files/upload/{uploadId}` | Files (7006) | HTTP/1.1 |

## Frontend JS Modules (`wwwroot/js/app/`)

**Инфраструктура:**
- `device.js` — `BF.device`: deviceId, browserName, osName
- `tokens.js` — `BF.tokens`: TokenStore (localStorage/sessionStorage)
- `metadata.js` — `BF.metadata`: сборка gRPC metadata с base64-заголовками

**Страница логина** (index.html):
- `auth.js` — login, refreshToken, getValidAccessToken
- `login-page.js` — форма логина, OTP, проверка сессии, запуск/остановка QR-сессии при смене секции
- `fast-auth.js` — `BF.fastAuth`: QR fast-auth логин (анонимный `GenerateFastAuthToken` + server-streaming `SubscribeFastAuthResult`), автоперезапуск при EXPIRED/REJECTED, отсчёт TTL 5 минут

**Мессенджер** (messenger.html):
- `clients.js` — gRPC-Web клиенты, authCall с auto-refresh
- `utils.js` — форматирование, escapeHtml, parseJwt
- `api.js` — высокоуровневые обёртки (listChats, sendMessage и др.)
- `files.js` — кэш URL файлов, upload
- `messages.js` — рендеринг пузырей, вложений, аудиоплеер
- `realtime.js` — server-streaming подписки (new_message, message_read, online_status)
- `attach.js` — диалог прикрепления файлов (images/docs режим, превью)
- `settings.js` — многоэкранная панель настроек (профиль, 2FA, сессии, пароль)
- `main.js` — bootstrap мессенджера

**Дополнительные страницы:**
- `mobile.html` — мобильная версия интерфейса

**Proto bundle** (`wwwroot/js/proto/barkfluff.bundle.js`):
Генерируется через `scripts/generate-proto.ps1` (или `.sh`). Требует: protoc, protoc-gen-grpc-web, Node.js (esbuild).

## Аутентификация (gRPC-Web)

- `x-auth-token` — JWT (plain text)
- Остальные заголовки — base64-encoded
- Server-streaming (Updates, Onliner) — `grpcwebtext` режим

## Real-time Подписки (`realtime.js`)

| Stream | Service | RPC | Назначение |
|--------|---------|-----|------------|
| updatesStream | UpdatesApi | SubscribeNewMessages | Новые сообщения |
| readStream | UpdatesApi | SubscribeMessagesRead | Статусы прочтения |
| onlineStream | OnlinerApi | SubscribeToOnlineStatus | Онлайн/оффлайн |

Механизмы: exponential backoff (2с → 30с), page-visibility reconnection, keep-alive ping каждые 3с, tab title badge `(N)`, Browser Notification API, scroll-based mark-as-read.

## QR Fast-Auth (`fast-auth.js`)

QR-вход на странице логина — анонимный поток (без токена), повторяет шаблон [[Клиенты/Windows]] / [[Клиенты/MacOS]].

| Шаг | RPC | Тип | Auth |
|-----|-----|-----|------|
| 1. Получить QR-PNG | `FastAuthApi.GenerateFastAuthToken({format: QR})` | unary | анонимно |
| 2. Подписка на статус | `FastAuthApi.SubscribeFastAuthResult({fast_auth_id})` | server-streaming | анонимно |

Метаданные устройства передаются в gRPC headers (`x-device-name`, `x-os-name`, `x-app-name`, `x-app-version`, `x-device-id`, `x-ip-address`, base64).

Финальные статусы: `ACCEPTED` (получаем `access_token` + `refresh_token` → `BF.tokens.save` → редирект на `/messenger`), `REJECTED` (toast и автоперезапуск через 1с), `EXPIRED` (молчаливый перезапуск). На сетевой разрыв — exponential backoff (2с → 30с).

YARP-маршрут `fast-auth` входит в `streamingServices` set — `ActivityTimeout: 24h`.

## Зависимости

- `Grpc.AspNetCore.Web`
- `Yarp.ReverseProxy`
- [[Backend/GrpcServer]] — Serilog, MetricsCollector, LoadConfiguration
