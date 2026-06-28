# BarkFluff

Распределённая платформа обмена сообщениями в реальном времени.

## Tech Stack

| Слой | Технологии |
|------|-----------|
| **Backend** | .NET 10, gRPC (HTTP/2), MassTransit + RabbitMQ, PostgreSQL + EF Core, Redis, Minio (S3), Docker |
| **macOS / iOS** | Swift, SwiftUI, grpc-swift 2.0 |
| **Windows** | WPF (.NET 10), Code-behind + Reactive wrappers |
| **Android** | Kotlin 2.0, gRPC-OkHttp, ViewBinding |
| **Linux** | C++ Qt 6, CMake, gRPC |
| **Web** | React 19 + TypeScript, Material 3 Expressive |

## Архитектура

Каждый микросервис — отдельный gRPC-сервер. При старте он загружает конфигурацию из сервиса **Configuration**, затем регистрируется в **Navigator**. Клиенты получают адреса всех сервисов через **Beacon** (единая точка входа). Асинхронные события между сервисами — через RabbitMQ (MassTransit).

Аутентификация — кастомная система **XAuth**: JWT в заголовке `x-auth-token`, обязательные device-заголовки (`x-device-id`, `x-device-name`, `x-ip`, `x-os` и др.) в Base64.

### Микросервисы

| Сервис | Порт | Описание |
|--------|------|----------|
| Configuration | 7003 | Централизованная конфигурация, реестр сервисов |
| Beacon | 7002 | Точка входа клиентов, отдаёт адреса всех сервисов |
| Navigator | 7010 | Реестр серверов BarkFluff |
| Identity | 7000 | Auth, JWT, 2FA, сессии, сброс пароля |
| Users | 7001 | Профили, контакты, устройства, бейджи |
| Messages | 7007 | Чаты, сообщения, read receipts |
| Files | 7005 / 7006 | Файлы и медиа (gRPC + REST/HTTP1.1), Minio S3 |
| Updates | 7015 | Real-time стриминг событий через gRPC |
| Onliner | 7009 | Трекинг онлайн-статусов |
| Notification | 7004 | Email-уведомления (RabbitMQ consumer) |
| FastAuth | 7008 | QR-авторизация устройств |
| Calls | 7025 | Аудио/видео звонки на LiveKit SFU |
| AdminPanel | 51888 | Веб-дашборд администратора |
| WebServer | 64641 | Публичный HTTP-сервер, раздача статики |
| Web | 7016 | gRPC-Web прокси + статика |
| CloudMessaging | — | Push-уведомления Firebase (Background Worker) |
| ClientStorage | — | Хранилище дистрибутивов клиентов |
| Developers | 7020 | Портал документации для разработчиков |

### Инфраструктура

| Сервис | Порт |
|--------|------|
| PostgreSQL | 5432 |
| RabbitMQ | 5672 / 15672 (UI) |
| Redis | 6379 |
| Minio (S3) | 9000 / 9001 (UI) |
| Seq (логи) | 8880 (UI) |

## Структура репозитория

```
BarkFluff/
├── Backend/          # Все микросервисы и shared-библиотеки
│   ├── BarkFluff.{Service}/
│   ├── BarkFluff.GrpcServer/   # Общая инфраструктура (XAuth, Serilog, Metrics)
│   └── Shared/                  # Proto, Auth, Exceptions, Identity, Queue
├── Android/          # Kotlin-клиент
├── Windows/          # WPF-клиент + DBEditor
├── macOS/            # SwiftUI macOS 26
├── iOS/              # SwiftUI iOS 26
├── Linux/            # Qt6 / C++20
├── Web/              # React 19 веб-клиент
└── Obsidian/ClaudeVault/  # База знаний проекта
```

## Быстрый старт

### Запуск backend (Docker)

```bash
cd Backend
docker-compose -f docker-compose-dev.yml up -d
docker-compose -f docker-compose-dev.yml ps
```

Порядок зависимостей управляется через `depends_on`. Configuration запускается первым — все остальные сервисы ждут его готовности.

### Сборка отдельного сервиса

```bash
dotnet build Backend/BarkFluff.{Service}/BarkFluff.{Service}.csproj
```

Требуется **.NET 10.0 SDK**. Подробности — [`Backend/DOTNET_SDK_REQUIREMENTS.md`](Backend/DOTNET_SDK_REQUIREMENTS.md).

### Android

```bash
cd Android/Barkfluff.Client.Android
./gradlew assembleDebug
```

### Windows (WPF)

```bash
dotnet build Windows/BarkFluff.Client.WPF/BarkFluff.Client.WPF.csproj
```

## Документация

| Файл | Содержимое |
|------|-----------|
| [`Backend/PORTS_CONFIGURATION.md`](Backend/PORTS_CONFIGURATION.md) | Порты всех сервисов и переменные окружения |
| [`Backend/DOCKER_SETUP.md`](Backend/DOCKER_SETUP.md) | Docker Compose, пример `.env`, команды отладки |
| [`Backend/METRICS.md`](Backend/METRICS.md) | Реестр метрик (Seq / ServiceMetrics) |
| [`Backend/DOTNET_SDK_REQUIREMENTS.md`](Backend/DOTNET_SDK_REQUIREMENTS.md) | Требования к .NET SDK, установка на Linux |
| [`Backend/SECURITY_AUDIT_SUMMARY.md`](Backend/SECURITY_AUDIT_SUMMARY.md) | Результаты аудита безопасности (март 2026) |
| `Obsidian/ClaudeVault/Index.md` | База знаний: архитектура, сервисы, клиенты |

## CI/CD

Каждый микросервис имеет отдельный workflow в `.github/workflows/build-backend-{service}.yml`. Трёхшаговая модель: проверка .NET SDK → Telegram-подтверждение → `dotnet publish` на self-hosted раннере + `docker build` из `Dockerfile.slim` → push в приватный registry.
