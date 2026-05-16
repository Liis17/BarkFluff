# BarkFluff — База знаний

Распределённая платформа обмена сообщениями в реальном времени.
**Backend**: .NET 10, gRPC, RabbitMQ, PostgreSQL, Redis, Minio.

---

## Навигация

### Архитектура и паттерны
- [[Архитектура]] — tech stack, таблица сервисов с портами, XAuth, CQRS, gRPC-клиент, RabbitMQ, Proto

### Backend — микросервисы

| Файл | Сервис | Порт |
|------|--------|------|
| [[Backend/Configuration]] | Централизованная конфигурация | 7003 |
| [[Backend/Configuration-ProjectMap]] | Карта всех файлов и классов Configuration | — |
| [[Backend/Beacon]] | Точка входа клиентов | 7002 |
| [[Backend/Beacon-ProjectMap]] | Карта всех файлов и классов Beacon | — |
| [[Backend/Beacon-Metrics]] | Реестр метрик Beacon (через ServiceMetrics-логи в Seq) | — |
| [[Backend/Navigator]] | Реестр серверов BarkFluff | 7010 |
| [[Backend/GrpcServer]] | Shared-библиотека инфраструктуры | — |
| [[Backend/GrpcServer-ProjectMap]] | Карта всех файлов и классов GrpcServer | — |
| [[Backend/Identity]] | Auth, JWT, 2FA, сессии | 7000 |
| [[Backend/Identity-ProjectMap]] | Карта всех файлов и классов Identity | — |
| [[Backend/Users]] | Профили, устройства, бейджи | 7001 |
| [[Backend/Users-ProjectMap]] | Карта всех файлов и классов Users | — |
| [[Backend/Users-Metrics]] | Реестр метрик Users (через ServiceMetrics-логи в Seq) | — |
| [[Backend/Messages]] | Чаты, сообщения, вложения | 7007 |
| [[Backend/Messages-ProjectMap]] | Карта всех файлов и классов Messages | — |
| [[Backend/Messages-Metrics]] | Реестр метрик Messages (auto MediatR + доменные + consumer-метрики) | — |
| [[Backend/Files]] | Файлы, S3, стикеры | 7005 |
| [[Backend/Files-ProjectMap]] | Карта всех файлов и классов BarkFluff.Files | — |
| [[Backend/Updates]] | Real-time стриминг событий | 7015 |
| [[Backend/Updates-ProjectMap]] | Карта всех файлов и классов Updates | — |
| [[Backend/Updates-Metrics]] | Реестр метрик Updates (подписки, broadcast, push) | — |
| [[Backend/Onliner]] | Онлайн-статусы | 7009 |
| [[Backend/Onliner-ProjectMap]] | Карта всех файлов и классов Onliner | — |
| [[Backend/Notification]] | Email-уведомления (RabbitMQ consumer) | 7004 |
| [[Backend/Notification-ProjectMap]] | Карта всех файлов и классов Notification | — |
| [[Backend/FastAuth]] | QR-авторизация устройств | 7008 |
| [[Backend/FastAuth-ProjectMap]] | Карта всех файлов и классов FastAuth | — |
| [[Backend/AdminPanel]] | Веб-дашборд администратора | 51888 |
| [[Backend/AdminPanel-ProjectMap]] | Карта всех файлов и классов AdminPanel | — |
| [[Backend/AdminPanel-Files]] | Краткое описание каждого файла AdminPanel | — |
| [[Backend/CloudMessaging]] | Push-уведомления (Firebase) | — |
| [[Backend/CloudMessaging-ProjectMap]] | Карта всех файлов и классов CloudMessaging | — |
| [[Backend/Web]] | gRPC-Web прокси + статика | 7016 |
| [[Backend/Web-ProjectMap]] | Карта всех файлов и классов BarkFluff.Web | — |
| [[Backend/WebServer]] | Публичный HTTP-сервер | 64641 |
| [[Backend/WebServer-ProjectMap]] | Карта всех файлов и классов WebServer | — |
| [[Backend/ClientStorage]] | Хранилище клиентских приложений | — |
| [[Backend/ClientStorage-ProjectMap]] | Карта всех файлов и классов ClientStorage | — |
| [[Backend/Developers]] | Портал документации для разработчиков | 7020 |
| [[Backend/Nginx]] | Nginx reverse proxy — TLS, субдомены, gRPC/HTTP маршрутизация | — |

### Shared-библиотеки

| Файл | Описание |
|------|----------|
| [[Shared/Proto]] | Все .proto контракты платформы |
| [[Shared/Proto-ProjectMap]] | Карта всех файлов и RPC Proto |
| [[Shared/Auth]] | gRPC client interceptors (JWT, device metadata) |
| [[Shared/Auth-ProjectMap]] | Карта всех файлов и классов Auth |
| [[Shared/Exceptions]] | BaseGrpcException, ErrorCode, ExceptionClientInterceptor |
| [[Shared/Exceptions-ProjectMap]] | Карта всех файлов и классов Exceptions |
| [[Shared/Identity]] | ServiceId enum, TokenType enum, IdentityClaims |
| [[Shared/Identity-ProjectMap]] | Карта всех файлов и классов Identity |
| [[Shared/Queue]] | RabbitMQ события (MassTransit POCO) |
| [[Shared/SecurityUtilities]] | Утилиты оценки силы пароля |
| [[Shared/SecurityUtilities-ProjectMap]] | Карта всех файлов и классов SecurityUtilities |

### Клиенты

| Файл | Платформа |
|------|-----------|
| [[Клиенты/DesignDocument]] | **UI/UX дизайн-документ** — экраны, сценарии, вложения (источник: `dd.md`) |
| [[Клиенты/Android]] | Kotlin + gRPC-OkHttp, Activity-based |
| [[Клиенты/Android-ProjectMap]] | Карта всех файлов и классов Android-клиента |
| [[Клиенты/Android-FileIndex]] | Индекс файлов Android-клиента с кратким описанием каждого |
| [[Клиенты/Windows-WPF]] | WPF .NET 10, Code-behind + Reactive |
| [[Клиенты/Windows-WPF-ProjectMap]] | Карта всех файлов и классов WPF-клиента |
| [[Клиенты/Windows-WebApiCore]] | gRPC-клиентская библиотека для WPF |
| [[Клиенты/Windows-WebApiCore-ProjectMap]] | Карта всех файлов и менеджеров WebApi.Core |
| [[Клиенты/Windows-DBEditor]] | Редактор конфигурации БД (WPF) |
| [[Клиенты/Linux-Qt]] | Qt 6 / C++20, ранняя стадия |
| [[Клиенты/macOS]] | SwiftUI + gRPC-Swift (macOS 26) |
| [[Клиенты/macOS-ProjectMap]] | Карта всех файлов и классов macOS-клиента |
| [[Клиенты/iOS]] | SwiftUI + gRPC-Swift (iOS 26, на базе macOS-клиента) |
| [[Клиенты/iOS-ProjectMap]] | Карта всех файлов iOS-клиента с описанием |
| [[Клиенты/Developers-Web]] | React + Vite + TS, портал документации |

---

## Правила обновления базы знаний

При работе с проектом **всегда обновляй** соответствующий файл в этом хранилище, если:
- Изменилась архитектура сервиса или его API
- Добавлены новые эндпоинты, команды, или RabbitMQ-события
- Изменились ключи конфигурации или зависимости
- Добавлен новый сервис или библиотека

**Структура новых файлов:**
- Новый Backend-сервис → `Backend/{Название}.md`
- Новая Shared-библиотека → `Shared/{Название}.md`
- Новый клиент → `Клиенты/{Платформа}.md`
- Добавь ссылку в этот Index.md

**Wikilinks:** используй `[[Файл]]` или `[[Папка/Файл]]` для связей между заметками.
