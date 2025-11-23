# BarkFluff Backend Documentation

## 📚 Оглавление

- [Обзор системы](#обзор-системы)
- [Архитектура](#архитектура)
- [Микросервисы](#микросервисы)
- [Быстрый старт](#быстрый-старт)
- [Документация](#документация)

## Обзор системы

BarkFluff — это распределённая платформа для обмена сообщениями в реальном времени, построенная на микросервисной архитектуре. Система поддерживает:

- ✅ Обмен сообщениями в реальном времени (личные и групповые чаты)
- ✅ Файловые вложения (изображения, видео, документы)
- ✅ Аутентификацию и авторизацию с 2FA
- ✅ Быструю аутентификацию через QR-коды
- ✅ Управление профилями пользователей и значками (badges)
- ✅ Email-уведомления
- ✅ Обнаружение серверов (service discovery)

## Архитектура

### Технологический стек

- **Framework**: .NET 9.0
- **API Protocol**: gRPC (HTTP/2)
- **Message Broker**: RabbitMQ
- **Databases**: PostgreSQL
- **Cache**: Redis
- **File Storage**: Minio (S3-совместимый)
- **Containerization**: Docker

### Микросервисы

| Сервис | Порт | Описание |
|--------|------|----------|
| [Configuration](./architecture/CONFIGURATION.md) | 7003 | Централизованное управление конфигурацией и service discovery |
| [Beacon](./microservices/BEACON.md) | 7004 | Центральная точка входа, предоставление информации о всех сервисах |
| [Identity](./microservices/IDENTITY.md) | 7001 | Аутентификация и авторизация (JWT, 2FA, сброс пароля) |
| [Users](./microservices/USERS.md) | 7002 | Управление профилями пользователей и badges |
| [Messages](./microservices/MESSAGES.md) | 7006 | Обработка сообщений (личные и групповые чаты) |
| [Files](./microservices/FILES.md) | 7005 | Хранение файлов в Minio с генерацией превью |
| [Updates](./microservices/UPDATES.md) | 7015 | Real-time обновления через gRPC streaming |
| [Notification](./microservices/NOTIFICATION.md) | 7004 | Отправка email-уведомлений через SMTP |
| [FastAuth](./microservices/FASTAUTH.md) | 7008 | Быстрая аутентификация через QR-коды (в разработке) |
| [Navigator](./microservices/NAVIGATOR.md) | 7010 | Регистрация и обнаружение BarkFluff серверов |

### Диаграмма взаимодействия

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
       ├─────────────────────────────────────────────────┐
       │                                                 │
       ▼                                                 ▼
┌─────────────┐                                  ┌─────────────┐
│   Beacon    │◄─────────────────────────────────┤Configuration│
│   (Entry)   │                                  │  (Config)   │
└──────┬──────┘                                  └──────┬──────┘
       │                                                 │
       │  ┌──────────────────────────────────────────────┘
       │  │
       ▼  ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│  Identity   │◄────►│    Users    │◄────►│    Files    │
│   (Auth)    │      │ (Profiles)  │      │  (Storage)  │
└─────────────┘      └─────────────┘      └──────┬──────┘
       │                     │                    │
       │                     │                    │
       ▼                     ▼                    ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│  Messages   │◄────►│   Updates   │      │    Minio    │
│   (Chat)    │      │  (Realtime) │      │   (S3 API)  │
└─────────────┘      └─────────────┘      └─────────────┘
       │
       ▼
┌─────────────┐
│ Notification│
│   (Email)   │
└─────────────┘

       Стрелки (◄────►) = gRPC взаимодействие
       События передаются через RabbitMQ
```

## Быстрый старт

### Предварительные требования

- Docker Desktop
- .NET 9.0 SDK (для локальной разработки)
- PostgreSQL 16+
- RabbitMQ 3.x
- Redis 7+
- Minio

### Запуск через Docker Compose

```bash
# Перейти в директорию Backend
cd Backend

# Создать .env файл на основе примера
cp .env.example .env

# Отредактировать .env с вашими настройками
nano .env

# Запустить все сервисы
docker-compose -f docker-compose-dev.yml up -d

# Проверить статус
docker-compose -f docker-compose-dev.yml ps
```

### Переменные окружения

Основные переменные (см. `.env.example`):

```env
# PostgreSQL
POSTGRES_HOST=postgres
POSTGRES_PASSWORD=your_password

# RabbitMQ
RABBITMQ_DEFAULT_USER=admin
RABBITMQ_DEFAULT_PASS=your_password

# Minio
MINIO_ROOT_USER=admin
MINIO_ROOT_PASSWORD=your_password

# Configuration Service
CONFIGURATION_SERVICE_URL=http://configuration:7003
```

## Документация

### Архитектура
- [Общая архитектура](./architecture/OVERVIEW.md) - высокоуровневое описание
- [Service Discovery](./architecture/SERVICE-DISCOVERY.md) - как сервисы находят друг друга
- [Event Bus (RabbitMQ)](./architecture/EVENT-BUS.md) - асинхронное взаимодействие
- [Аутентификация](./architecture/AUTHENTICATION.md) - JWT, XAuth, политики безопасности

### Микросервисы
- [Configuration](./microservices/CONFIGURATION.md)
- [Beacon](./microservices/BEACON.md)
- [Identity](./microservices/IDENTITY.md)
- [Users](./microservices/USERS.md)
- [Messages](./microservices/MESSAGES.md)
- [Files](./microservices/FILES.md)
- [Updates](./microservices/UPDATES.md)
- [Notification](./microservices/NOTIFICATION.md)
- [FastAuth](./microservices/FASTAUTH.md)
- [Navigator](./microservices/NAVIGATOR.md)

### API Reference
- [gRPC API Documentation](./api/GRPC-API.md) - все gRPC методы
- [Proto файлы](./api/PROTO-REFERENCE.md) - описание Protocol Buffers

### Deployment
- [Deployment Guide](./deployment/DEPLOYMENT.md)
- [Configuration Management](./deployment/CONFIGURATION.md)
- [Docker Setup](./deployment/DOCKER.md)
- [Troubleshooting](./deployment/TROUBLESHOOTING.md)

## Контакты и поддержка

- **GitHub Issues**: [https://github.com/Fooxboy/BarkFluff/issues](https://github.com/Fooxboy/BarkFluff/issues)
- **Документация**: [docs/](./docs/)

## Лицензия

Информация о лицензии указана в корневом файле LICENSE проекта.
