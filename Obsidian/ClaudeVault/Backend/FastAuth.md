# BarkFluff.FastAuth

Быстрая и легковесная аутентификация устройств (QR-авторизация). Порт: **7008**.

Расположение: `Backend/BarkFluff.FastAuth/`

## Описание

Предоставляет gRPC API для генерации токена подключения устройства, который может быть использован для аутентификации через QR-код или другой быстрый метод.

## Сборка

```bash
dotnet build Backend/BarkFluff.FastAuth/BarkFluff.FastAuth.csproj
```

## Tech Stack

- ASP.NET Core
- gRPC
- MediatR
- QRCoder

## Зависимости

- [[Backend/Configuration]] — обнаружение других сервисов

## Proto

`fast_auth_api.proto` — Server
