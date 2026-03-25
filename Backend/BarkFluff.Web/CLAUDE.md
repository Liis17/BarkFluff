# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

BarkFluff.Web — HTTP/REST-шлюз (API Gateway) для веб-клиента BarkFluff. Проксирует HTTP-запросы к внутренним gRPC-микросервисам. Весь код находится в одном файле `Program.cs` (Minimal APIs, .NET 9.0).

## Build

```bash
dotnet build BarkFluff.Web.csproj
```

Для запуска в Docker (из корня репозитория):
```bash
docker-compose -f docker-compose-dev.yml up web
```

## Architecture

Сервис работает как HTTP→gRPC прокси-шлюз:
- Принимает REST-запросы от браузера (JSON over HTTP/1.1)
- Транслирует их в gRPC-вызовы к внутренним сервисам (Identity, Messages, Users, Files, Updates, Onliner)
- gRPC streaming (Updates, Onliner) транслируется в SSE (Server-Sent Events) для веб-клиента
- Статика раздаётся из `wwwroot/` (index.html, messenger.html)

Порт по умолчанию: **7016** (HTTP/1.1 + HTTP/2, настраивается через `RunSettings:Port`).

## REST API Endpoints

| Группа | Эндпоинты | gRPC-сервис |
|--------|-----------|-------------|
| Auth | `POST /api/auth/login`, `POST /api/auth/refresh` | Identity |
| Chats | `GET /api/chats`, `GET /api/chats/{id}/messages`, `GET /api/chats/{id}/info`, `GET /api/chats/personal/{userId}`, `POST /api/chats/send`, `POST /api/chats/mark-read`, `GET /api/chats/{id}/attachments` | Messages |
| Users | `GET /api/users/{id}`, `GET /api/users/search` | Users |
| Files | `GET /api/files/download-urls`, `GET /api/files/upload-url`, `POST /api/files/upload/{fileId}` | Files |
| Online | `POST /api/online/ping`, `GET /api/online/status` | Onliner |
| SSE | `GET /api/sse/updates?token=X`, `GET /api/sse/online?userIds=...&token=X` | Updates, Onliner |

## Authentication

- Login эндпоинт не требует токена, остальные требуют `Authorization: Bearer <token>`
- SSE-эндпоинты получают токен через query-параметр `token=` (т.к. EventSource API не поддерживает заголовки)
- Все gRPC-вызовы передают XAuth metadata (x-auth-token, x-device-id, x-device-name и др.) в base64

## Key Patterns

- gRPC-ошибки маппятся в HTTP-коды через `HandleGrpcError()`: Unauthenticated→401, PermissionDenied→403, NotFound→404
- Ошибки Identity имеют специальные error-коды в gRPC trailer `x-error-code` (OTP required, invalid credentials и т.д.)
- DTO-записи (records) для запросов и классы для ответов определены в конце `Program.cs`
- Маппинг gRPC→JSON: `MapChat()`, `MapMessage()`, `MapUser()` — статические методы в конце файла
- Файловый upload проксируется через HTTP POST к Files-сервису напрямую (не через gRPC)

## Dependencies

- `BarkFluff.GrpcServer` — Serilog, MetricsCollector, LoadConfiguration
- `BarkFluff.Shared.Auth` — gRPC client interceptors
- Proto-файлы из `Shared/BarkFluff.Proto/` подключены как gRPC Client (Identity, Messages, Users, Files, Updates, Onliner) + shared.proto как None
