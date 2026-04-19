# BarkFluff.Web

gRPC-Web reverse proxy + static file server для веб-клиента. Порт: **7016**.

Расположение: `Backend/BarkFluff.Web/`

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
| `/api/files/upload/{uploadId}` | Files (7006) | HTTP/1.1 |

## Frontend JS Modules (`wwwroot/js/app/`)

**Инфраструктура:**
- `device.js` — `BF.device`: deviceId, browserName, osName
- `tokens.js` — `BF.tokens`: TokenStore (localStorage/sessionStorage)
- `metadata.js` — `BF.metadata`: сборка gRPC metadata с base64-заголовками

**Страница логина** (index.html):
- `auth.js` — login, refreshToken, getValidAccessToken
- `login-page.js` — форма логина, OTP, проверка сессии

**Мессенджер** (messenger.html):
- `clients.js` — gRPC-Web клиенты, authCall с auto-refresh
- `utils.js` — форматирование, escapeHtml, parseJwt
- `api.js` — высокоуровневые обёртки (listChats, sendMessage и др.)
- `files.js` — кэш URL файлов, upload
- `messages.js` — рендеринг пузырей, вложений, аудиоплеер
- `realtime.js` — server-streaming подписки (new_message, message_read, online_status)
- `main.js` — bootstrap мессенджера

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

## Зависимости

- `Grpc.AspNetCore.Web`
- `Yarp.ReverseProxy`
- [[Backend/GrpcServer]] — Serilog, MetricsCollector, LoadConfiguration
