# BarkFluff — память проекта

Распределённый мессенджер: самостоятельные ноды, backend-сервисы и клиенты. Память описывает текущее устройство; предложения находятся отдельно. Пути исходников отсчитываются от корня репозитория.

- [[Architecture|Архитектура]] — стек, порты, потоки, auth, deployment и CI.
- [[Testing|Тестирование]] — test entry points, провайдеры БД и ограничения проверок.

## Backend

| Компонент | Назначение | Дополнительные заметки |
|-----------|------------|------------------------|
| [[Backend/Settings]] | Централизованные настройки и setup API | — |
| [[Backend/Setup]] | Первичная настройка Settings | — |
| [[Backend/Beacon]] | Адреса сервисов и метаданные ноды | [[Backend/Beacon-Metrics|Метрики]], [[Backend/Beacon-ProjectMap|Карта]] |
| [[Backend/Navigator]] | Каталог нод | — |
| [[Backend/GrpcServer]] | Общая backend-инфраструктура, XAuth, health и метрики | [[Backend/GrpcServer-ProjectMap|Карта]] |
| [[Backend/Identity]] | JWT, challenge auth, OTP и сессии | [[Backend/Identity-Metrics|Метрики]], [[Backend/Identity-ProjectMap|Карта]] |
| [[Backend/Users]] | Профили, устройства, prekeys и папки чатов | [[Backend/Users-ChatFolders-ClientGuide|ChatFolders-ClientGuide]], [[Backend/Users-Metrics|Метрики]], [[Backend/Users-ProjectMap|Карта]] |
| [[Backend/Messages]] | Чаты, сообщения, поиск и federation operations | [[Backend/Messages-Metrics|Метрики]], [[Backend/Messages-PinnedMessages-ClientGuide|PinnedMessages-ClientGuide]], [[Backend/Messages-ProjectMap|Карта]] |
| [[Backend/Files]] | S3, upload/download, превью и стикеры | [[Backend/Files-ProjectMap|Карта]] |
| [[Backend/Updates]] | Потоки событий и push-routing | [[Backend/Updates-Metrics|Метрики]], [[Backend/Updates-ProjectMap|Карта]] |
| [[Backend/Onliner]] | Presence и typing/recording | [[Backend/Onliner-Metrics|Метрики]], [[Backend/Onliner-ProjectMap|Карта]] |
| [[Backend/Notification]] | RabbitMQ → SMTP, HTTP health | [[Backend/Notification-ProjectMap|Карта]] |
| [[Backend/FastAuth]] | QR-авторизация устройств | [[Backend/FastAuth-ProjectMap|Карта]] |
| [[Backend/AdminPanel]] | Администрирование, health/Seq, Docker и SSH | [[Backend/AdminPanel-Files|Файлы]], [[Backend/AdminPanel-ProjectMap|Карта]] |
| [[Backend/CloudMessaging]] | Firebase worker | [[Backend/CloudMessaging-ProjectMap|Карта]] |
| [[Backend/Web]] | gRPC-Web gateway и статика | [[Backend/Web-ProjectMap|Карта]] |
| [[Backend/WebServer]] | Публичный сайт/HTTP | [[Backend/WebServer-ProjectMap|Карта]] |
| [[Backend/ClientStorage]] | Дистрибутивы клиентов | [[Backend/ClientStorage-ProjectMap|Карта]] |
| [[Backend/Developers]] | Backend портала разработчиков | — |
| [[Backend/Calls]] | Управление звонками и LiveKit | — |
| [[Backend/Bots]] | Bot API, BotFather и встроенные боты | — |
| [[Backend/Federation]] | Межсерверная доставка и XFed | — |
| [[Backend/Nginx]] | TLS/subdomains и reverse proxy | — |

## Shared

| Компонент | Назначение | Дополнительные заметки |
|-----------|------------|------------------------|
| [[Shared/Proto]] | Wire schemas и RPC платформы | [[Shared/Proto-ProjectMap|Карта]] |
| [[Shared/Auth]] | JWT/device client interceptors | [[Shared/Auth-ProjectMap|Карта]] |
| [[Shared/Exceptions]] | ErrorCode, server exceptions и client mapping | [[Shared/Exceptions-ProjectMap|Карта]] |
| [[Shared/Identity]] | ServiceId, TokenType и claims | [[Shared/Identity-ProjectMap|Карта]] |
| [[Shared/Queue]] | MassTransit события | — |
| [[Shared/SecurityUtilities]] | Password strength utility | [[Shared/SecurityUtilities-ProjectMap|Карта]] |

## Clients

| Компонент | Назначение | Дополнительные заметки |
|-----------|------------|------------------------|
| [[Clients/DesignDocument]] | Продуктовый UI/UX ориентир из dd.md | — |
| [[Clients/Android]] | V1 UI, gateways, SQLCipher drafts/outbox и TLS | [[Clients/Android-FileIndex|Исходники]], [[Clients/Android-ProjectMap|Карта]] |
| [[Clients/Windows-WPF]] | Legacy WPF клиент | [[Clients/Windows-WPF-ProjectMap|Карта]] |
| [[Clients/Windows-WPF-V2]] | WPF V2, MVVM/DI и SQLite | [[Clients/Windows-WPF-V2-ProjectMap|Карта]] |
| [[Clients/Windows-WinUI]] | WinUI 3 клиент | — |
| [[Clients/Windows-WebApiCore]] | Общий Windows транспорт/менеджеры | [[Clients/Windows-WebApiCore-ProjectMap|Карта]] |
| [[Clients/Windows-UpdaterCLI]] | Отдельный legacy инсталлятор/апдейтер | — |
| [[Clients/Linux-Qt]] | Qt 6/C++ клиент | — |
| [[Clients/macOS]] | SwiftUI клиент и общие Swift-пакеты | [[Clients/macOS-ProjectMap|Карта]] |
| [[Clients/iOS]] | SwiftUI мобильный клиент | [[Clients/iOS-ProjectMap|Карта]] |
| [[Clients/Developers-Web]] | React/Vite портал разработчиков | — |
| [[Clients/Web]] | Vanilla-JS браузерный мессенджер | [[Clients/Web-Network-Reliability|Сеть]] |

## Предложения

- [[Ideas/Index|Идеи]] — отдельные продуктовые предложения с границами уже реализованного.

Правила чтения и обновления памяти закреплены в корневом `AGENTS.md`.
