# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`BarkFluff.Shared.Identity` — разделяемая библиотека (.NET 9.0), содержащая общие типы для идентификации сервисов и пользователей. Используется всеми микросервисами BarkFluff.

## Build

```bash
dotnet build BarkFluff.Shared.Identity.csproj
```

## Contents

Три файла, три типа:

- **`ServiceId.cs`** — enum с ID каждого микросервиса (Identity=1, Users=2, Beacon=3, ...). При добавлении нового сервиса — добавить сюда значение.
- **`TokenType.cs`** — enum типов JWT-токенов: `User`, `Service`, `FastAuth`.
- **`IdentityClaims.cs`** — строковые константы для JWT claims и gRPC metadata: `x-user-id`, `x-token-type`, `x-service-id`.

## Usage Context

Эта библиотека используется в:
- **`BarkFluff.GrpcServer`** — для XAuth авторизации (политики на основе `TokenType`)
- **`BarkFluff.Identity`** — для генерации JWT с этими claims
- **Все микросервисы** — через `builder.LoadConfiguration(ServiceId.XxxName)`

## Adding a New Service

1. Добавить значение в `ServiceId` enum
2. Зарегистрировать сервис в БД Configuration service
3. Использовать `builder.LoadConfiguration(ServiceId.NewService)` в `Program.cs` нового сервиса
