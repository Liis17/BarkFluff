# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

BarkFluff.Web — gRPC-Web reverse proxy + static file server для веб-клиента BarkFluff. Браузер общается с бэкенд-микросервисами напрямую через gRPC-Web протокол, а YARP проксирует трафик к gRPC-сервисам. Program.cs содержит только настройку YARP-маршрутов и middleware.

## Build

```bash
dotnet build BarkFluff.Web.csproj
```

Для запуска в Docker (из корня репозитория):
```bash
docker-compose -f docker-compose-dev.yml up web
```

## Architecture

Сервис выполняет три функции:
1. **Статика** — раздаёт `wwwroot/` (index.html, messenger.html, JS-модули)
2. **gRPC-Web → gRPC прокси** — `Grpc.AspNetCore.Web` конвертирует `application/grpc-web-text` (HTTP/1.1) в HTTP/2 gRPC, YARP проксирует к бэкенд-сервисам
3. **HTTP upload прокси** — единственный REST-эндпоинт `POST /api/files/upload/{uploadId}` проксируется к Files-сервису

Порт по умолчанию: **7016** (HTTP/1.1 + HTTP/2, настраивается через `RunSettings:Port`).

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

## Frontend JS Modules

Все JS-модули в `wwwroot/js/app/`, загружаются через `<script src>` в HTML-страницах.

**Общая инфраструктура** (используется обеими страницами):
- `device.js` — BF.device: deviceId, browserName, osName
- `tokens.js` — BF.tokens: TokenStore (localStorage/sessionStorage)
- `metadata.js` — BF.metadata: сборка gRPC metadata с base64-заголовками

**Страница логина** (index.html):
- `auth.js` — BF.auth: login, refreshToken, getValidAccessToken
- `login-page.js` — bootstrap: форма логина, OTP, проверка сессии

**Мессенджер** (messenger.html):
- `clients.js` — BF.clients: gRPC-Web клиенты, authCall с auto-refresh
- `utils.js` — BF.utils: форматирование, escapeHtml, parseJwt
- `api.js` — BF.api: высокоуровневые обёртки (listChats, sendMessage и др.)
- `files.js` — BF.files: кэш URL файлов, upload
- `messages.js` — BF.messages: рендеринг пузырей, вложений, аудиоплеер
- `realtime.js` — BF.realtime: server-streaming подписки (new_message, message_read, online_status, connection_status, tab_visible)
- `main.js` — bootstrap мессенджера: чаты, сообщения, поиск, профиль, title-badge, browser-notifications

**Proto bundle** (`wwwroot/js/proto/barkfluff.bundle.js`):
- Генерируется скриптом `scripts/generate-proto.ps1` (или `.sh`)
- Содержит gRPC-Web клиенты (`window.barkfluff`) и proto-типы (`window.proto`)
- Требует: protoc, protoc-gen-grpc-web, Node.js (esbuild)

## Authentication (gRPC-Web)

- Браузер отправляет XAuth metadata напрямую в gRPC-Web вызовах
- `x-auth-token` — JWT (plain text), остальные заголовки — base64-encoded
- Server-streaming (Updates, Onliner) использует режим `grpcwebtext`
- Login через отдельный IdentityApiClient без auth interceptor

## Real-time Subscriptions

Реалтайм обновления реализованы в `realtime.js` (BF.realtime) + обработчики в `main.js`.

**Потоки (server-streaming):**
| Stream | Service | RPC | Назначение |
|--------|---------|-----|------------|
| updatesStream | UpdatesApi | SubscribeNewMessages | Новые сообщения во всех чатах |
| readStream | UpdatesApi | SubscribeMessagesRead | Статусы прочтения (readBy) |
| onlineStream | OnlinerApi | SubscribeToOnlineStatus | Онлайн/оффлайн пользователей |

**События (BF.realtime.on):**
| Событие | Данные | Обработчик в main.js |
|---------|--------|---------------------|
| `new_message` | `{ chatId, message }` | handleNewMessage — добавляет в чат, переносит чат наверх, обновляет badge, показывает Notification |
| `message_read` | `{ chatId, messageId, readBy }` | handleMessageRead — обновляет ✓/✓✓, пересчитывает unread, обновляет title-badge |
| `online_status` | `{ userId, status, lastSeen }` | handleOnlineStatus — обновляет online-dot и статус в шапке чата |
| `connection_status` | `{ connected }` | Показывает/скрывает баннер «Подключение к серверу...» |
| `tab_visible` | `{}` | Перезагружает список чатов для синхронизации пропущенных обновлений |

**Механизмы:**
- Exponential backoff (2 с → 30 с) при переподключении каждого потока
- Page-visibility reconnection — при возврате в вкладку восстанавливает потоки и обновляет токен
- Keep-alive ping (SetOnlineStatus каждые 3 с)
- Tab title badge — `(N) BarkFluff — Мессенджер` с суммой непрочитанных
- Browser Notification API — уведомление для сообщений от других пользователей когда чат не активен или вкладка не в фокусе
- Scroll-based mark-as-read — видимые сообщения автоматически отмечаются прочитанными при прокрутке
- ChangeUsersInSubscription — динамическое обновление списка подписки на онлайн-статусы без переоткрытия потока

## Dependencies

- `Grpc.AspNetCore.Web` — gRPC-Web middleware
- `Yarp.ReverseProxy` — reverse proxy
- `BarkFluff.GrpcServer` — Serilog, MetricsCollector, LoadConfiguration
