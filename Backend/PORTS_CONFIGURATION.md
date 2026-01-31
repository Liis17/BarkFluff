# BarkFluff Backend Services - Порты и настройки

Этот документ описывает дефолтные порты для всех Backend сервисов BarkFluff для запуска на localhost и в Docker.

## 📋 Таблица портов сервисов

| Сервис | ServiceId | Порт (HTTP/2) | Порт (HTTP/1.1) | Переменная окружения |
|--------|-----------|---------------|-----------------|----------------------|
| **Configuration** | 0 | 7003 | - | `CONFIGURATION_PORT` |
| **Identity** | 1 | 7000 | - | `IDENTITY_PORT` |
| **Users** | 2 | 7001 | - | `USERS_PORT` |
| **Beacon** | 3 | 7002 | - | `BEACON_PORT` |
| **Notification** | 4 | 7004 | - | `NOTIFICATION_PORT` |
| **Files** | 5 | 7005 | 7006 | `FILES_PORT`, `FILES_HTTP1PORT` |
| **Messages** | 6 | 7007 | - | `MESSAGES_PORT` |
| **FastAuth** | 7 | 7008 | - | `FASTAUTH_PORT` |
| **Updates** | 8 | 7015 | - | `UPDATES_PORT` |
| **Onliner** | 9 | 7009 | - | `ONLINER_PORT` |
| **Navigator** | - | 7010 | - | `NAVIGATOR_PORT` |

## 🐳 Инфраструктурные сервисы

| Сервис | Порт(ы) | Переменная окружения |
|--------|---------|----------------------|
| **PostgreSQL** | 5432 | `POSTGRES_PORT` |
| **RabbitMQ** | 5672, 15672 | `RABBITMQ_1PORT`, `RABBITMQ_2PORT` |
| **Redis** | 6379 | `REDIS_PORT` |
| **MinIO (S3)** | 9000, 9001 | `MINIO_PORT`, `MINIO_WEBPORT` |

## 🔧 Настройка для локальной разработки

### appsettings.json

Все Backend сервисы теперь имеют настройки портов в `appsettings.json`:

```json
{
  "RunSettings": {
    "Port": 7000
  },
  "ConfigurationServiceAddr": "http://localhost:7003"
}
```

### Порядок запуска сервисов

1. **Configuration Service** (7003) - должен быть запущен первым
2. **Navigator** (7010) - для регистрации серверов
3. Все остальные сервисы могут запускаться параллельно

### Переменные окружения

Каждый сервис может переопределить свой порт через переменную окружения:

```bash
# Пример для Identity
IDENTITY_PORT=7000

# Пример для Configuration Service
CONFIGURATION_PORT=7003
CONFIGURATION_SERVICE_URL=http://localhost:7003
```

## 🐋 Настройка для Docker

### Docker Compose

В Docker Compose используйте имена сервисов вместо `localhost`:

```yaml
environment:
  - CONFIGURATION_SERVICE_URL=http://configuration:7003
  - IDENTITY_PORT=7000
```

### Docker Network

Все контейнеры должны находиться в одной Docker сети для взаимодействия:

```yaml
networks:
  barkfluff-network:
    driver: bridge
```

## 📝 Конфигурационные ключи

Configuration Service управляет следующими ключами конфигурации:

### RunSettings
- `Port` - основной порт сервиса (HTTP/2 для gRPC)
- `Http1Port` - дополнительный порт для HTTP/1.1 (только Files)
- `Host` - хост для прослушивания (по умолчанию: localhost)

### Database Connections
- `IdentityDb` - строка подключения для Identity (ServiceId: 1)
- `UsersDb` - строка подключения для Users (ServiceId: 2)
- `FilesDb` - строка подключения для Files (ServiceId: 5)
- `MessagesDb` - строка подключения для Messages (ServiceId: 6)
- `OnlinerDb` - строка подключения для Onliner (ServiceId: 9)

### Service URLs
- `NavigatorUrl` - URL сервиса Navigator
- `UsersService:Host` - URL сервиса Users
- `UsersService:Token` - JWT токен для межсервисной коммуникации
- `FilesService:Host` - URL сервиса Files
- `FilesService:Token` - JWT токен для межсервисной коммуникации

### RabbitMQ
- `RabbitMQ:Host` - хост RabbitMQ
- `RabbitMQ:Username` - имя пользователя
- `RabbitMQ:Password` - пароль
- `RabbitMQ:VirtualHost` - виртуальный хост

### Redis
- `Redis` - строка подключения к Redis (для Messages)

### MinIO (S3)
- `Minio:ServiceUrl` - URL MinIO сервиса
- `Minio:AccessKey` - access key
- `Minio:SecretKey` - secret key

### JWT Settings
- `JwtSettings:SecretKey` - секретный ключ для JWT
- `JwtSettings:Issuer` - издатель токена
- `JwtSettings:Audience` - аудитория токена
- `JwtSettings:ExpiryMinutes` - время жизни токена в минутах

### Server Properties (Beacon)
- `ServerProps:Name` - имя сервера
- `ServerProps:Description` - описание сервера
- `ServerProps:PublicName` - публичное имя сервера
- `ServerProps:Location` - местоположение сервера
- `ServerColor:Lite` - светлый цвет темы сервера
- `ServerColor:Main` - основной цвет темы сервера
- `ServerColor:Hard` - тёмный цвет темы сервера

### Email (Notification)
- `Email:Host` - SMTP хост
- `Email:Port` - SMTP порт
- `Email:SenderEmail` - email отправителя
- `Email:SenderPassword` - пароль email

## 🚀 Быстрый старт

### Локальная разработка

1. Запустите Configuration Service:
```bash
cd Backend/BarkFluff.Configuration
dotnet run
```

2. Запустите Navigator:
```bash
cd Backend/BarkFluff.Navigator
dotnet run
```

3. Запустите остальные сервисы по необходимости

### Docker

```bash
docker-compose up -d
```

Все сервисы автоматически найдут друг друга через DNS имена в Docker сети.

## 🔍 Отладка

### Проверка доступности сервиса

```bash
# Для gRPC сервисов используйте grpcurl
grpcurl -plaintext localhost:7003 list

# Для HTTP/1.1 эндпоинтов (Files)
curl http://localhost:7006/health
```

### Логи

Все сервисы пишут логи в stdout. Для просмотра логов в Docker:

```bash
docker logs <container_name>
```

## 📚 Дополнительная информация

- Все gRPC сервисы поддерживают gRPC Reflection
- Files сервис имеет два порта: один для gRPC (7005), другой для REST API (7006)
- Configuration Service хранит настройки в PostgreSQL
- При изменении портов в .env файле убедитесь, что они совпадают с appsettings.json
