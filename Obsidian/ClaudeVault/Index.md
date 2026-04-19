# BarkFluff — База знаний

Распределённая платформа обмена сообщениями в реальном времени.
**Backend**: .NET 9, gRPC, RabbitMQ, PostgreSQL, Redis, Minio.

---

## Навигация

### Архитектура и паттерны
- [[Архитектура]] — tech stack, таблица сервисов с портами, XAuth, CQRS, gRPC-клиент, RabbitMQ, Proto

### Backend — микросервисы

| Файл | Сервис | Порт |
|------|--------|------|
| [[Backend/Configuration]] | Централизованная конфигурация | 7003 |
| [[Backend/Beacon]] | Точка входа клиентов | 7002 |
| [[Backend/Navigator]] | Реестр серверов BarkFluff | 7010 |
| [[Backend/GrpcServer]] | Shared-библиотека инфраструктуры | — |
| [[Backend/Identity]] | Auth, JWT, 2FA, сессии | 7000 |
| [[Backend/Users]] | Профили, устройства, бейджи | 7001 |
| [[Backend/Messages]] | Чаты, сообщения, вложения | 7007 |
| [[Backend/Files]] | Файлы, S3, стикеры | 7005 |
| [[Backend/Updates]] | Real-time стриминг событий | 7015 |
| [[Backend/Onliner]] | Онлайн-статусы | 7009 |
| [[Backend/Notification]] | Email-уведомления (RabbitMQ consumer) | 7004 |
| [[Backend/FastAuth]] | QR-авторизация устройств | 7008 |
| [[Backend/AdminPanel]] | Веб-дашборд администратора | 51888 |
| [[Backend/CloudMessaging]] | Push-уведомления (Firebase) | — |
| [[Backend/Web]] | gRPC-Web прокси + статика | 7016 |
| [[Backend/WebServer]] | Публичный HTTP-сервер | 64641 |
| [[Backend/ClientStorage]] | Хранилище клиентских приложений | — |

### Shared-библиотеки

| Файл | Описание |
|------|----------|
| [[Shared/Proto]] | Все .proto контракты платформы |
| [[Shared/Auth]] | gRPC client interceptors (JWT, device metadata) |
| [[Shared/Exceptions]] | BaseGrpcException, ErrorCode, ExceptionClientInterceptor |
| [[Shared/Identity]] | ServiceId enum, TokenType enum, IdentityClaims |
| [[Shared/Queue]] | RabbitMQ события (MassTransit POCO) |
| [[Shared/SecurityUtilities]] | Утилиты оценки силы пароля |

### Клиенты

| Файл | Платформа |
|------|-----------|
| [[Клиенты/Android]] | Kotlin + gRPC-OkHttp, Activity-based |
| [[Клиенты/Android-ProjectMap]] | Карта всех файлов и классов Android-клиента |
| [[Клиенты/Windows-WPF]] | WPF .NET 10, Code-behind + Reactive |
| [[Клиенты/Windows-WebApiCore]] | gRPC-клиентская библиотека для WPF |
| [[Клиенты/Windows-WebApiCore-ProjectMap]] | Карта всех файлов и менеджеров WebApi.Core |
| [[Клиенты/Windows-DBEditor]] | Редактор конфигурации БД (WPF) |
| [[Клиенты/Linux-Qt]] | Qt 6 / C++20, ранняя стадия |
| [[Клиенты/macOS]] | SwiftUI + gRPC-Swift |
| [[Клиенты/iOS]] | SwiftUI + gRPC-Swift (на базе macOS-клиента) |

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
