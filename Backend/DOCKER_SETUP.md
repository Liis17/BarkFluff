# 🐳 Docker Compose настройка для BarkFluff

Эта конфигурация поднимает все Backend сервисы BarkFluff в Docker с использованием портов из `.env` файла.

## 📋 Структура сервисов

Все сервисы работают в единой Docker сети `barkfluff-network` и могут обращаться друг к другу по именам контейнеров.

### Пример docker-compose.yml

```yaml
version: '3.8'

networks:
  barkfluff-network:
    driver: bridge

services:
  # PostgreSQL Database
  postgres:
    image: postgres:16-alpine
    container_name: barkfluff-postgres
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-barkfluff}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-barkfluff}
    ports:
      - "${POSTGRES_PORT:-5432}:5432"
    networks:
      - barkfluff-network
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-barkfluff}"]
      interval: 10s
      timeout: 5s
      retries: 5

  # RabbitMQ Message Broker
  rabbitmq:
    image: rabbitmq:3-management-alpine
    container_name: barkfluff-rabbitmq
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER:-barkfluff}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:-barkfluff}
    ports:
      - "${RABBITMQ_1PORT:-5672}:5672"
      - "${RABBITMQ_2PORT:-15672}:15672"
    networks:
      - barkfluff-network
    volumes:
      - rabbitmq-data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  # Redis Cache
  redis:
    image: redis:7-alpine
    container_name: barkfluff-redis
    ports:
      - "${REDIS_PORT:-6379}:6379"
    networks:
      - barkfluff-network
    volumes:
      - redis-data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  # MinIO S3 Storage
  minio:
    image: minio/minio:latest
    container_name: barkfluff-minio
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: ${MINIO_ROOT_USER:-minioadmin}
      MINIO_ROOT_PASSWORD: ${MINIO_ROOT_PASSWORD:-minioadmin}
    ports:
      - "${MINIO_PORT:-9000}:9000"
      - "${MINIO_WEBPORT:-9001}:9001"
    networks:
      - barkfluff-network
    volumes:
      - minio-data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 10s
      timeout: 5s
      retries: 5

  # Configuration Service (должен запускаться первым)
  configuration:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Configuration/Dockerfile
    container_name: barkfluff-configuration
    environment:
      - CONFIGURATION_PORT=${CONFIGURATION_PORT:-7003}
      - CONFIGURATION_HOST=${POSTGRES_HOST:-postgres}
      - CONFIGURATION_DATABASE=${CONFIGURATION_DATABASE:-barkfluff_configuration}
      - CONFIGURATION_USERNAME=${POSTGRES_USER:-barkfluff}
      - CONFIGURATION_PASSWORD=${POSTGRES_PASSWORD:-barkfluff}
    ports:
      - "${CONFIGURATION_PORT:-7003}:${CONFIGURATION_PORT:-7003}"
    networks:
      - barkfluff-network
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped

  # Navigator Service
  navigator:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Navigator/Dockerfile
    container_name: barkfluff-navigator
    environment:
      - NAVIGATOR_PORT=${NAVIGATOR_PORT:-7010}
    ports:
      - "${NAVIGATOR_PORT:-7010}:${NAVIGATOR_PORT:-7010}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
    restart: unless-stopped

  # Identity Service
  identity:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Identity/Dockerfile
    container_name: barkfluff-identity
    environment:
      - IDENTITY_PORT=${IDENTITY_PORT:-7000}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${IDENTITY_PORT:-7000}:${IDENTITY_PORT:-7000}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - postgres
      - rabbitmq
    restart: unless-stopped

  # Users Service
  users:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Users/Dockerfile
    container_name: barkfluff-users
    environment:
      - USERS_PORT=${USERS_PORT:-7001}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${USERS_PORT:-7001}:${USERS_PORT:-7001}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - postgres
      - rabbitmq
    restart: unless-stopped

  # Beacon Service
  beacon:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Beacon/Dockerfile
    container_name: barkfluff-beacon
    environment:
      - BEACON_PORT=${BEACON_PORT:-7002}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${BEACON_PORT:-7002}:${BEACON_PORT:-7002}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - navigator
    restart: unless-stopped

  # Notification Service
  notification:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Notification/Dockerfile
    container_name: barkfluff-notification
    environment:
      - NOTIFICATION_PORT=${NOTIFICATION_PORT:-7004}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${NOTIFICATION_PORT:-7004}:${NOTIFICATION_PORT:-7004}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - rabbitmq
    restart: unless-stopped

  # Files Service
  files:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Files/Dockerfile
    container_name: barkfluff-files
    environment:
      - FILES_PORT=${FILES_PORT:-7005}
      - FILES_HTTP1PORT=${FILES_HTTP1PORT:-7006}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${FILES_PORT:-7005}:${FILES_PORT:-7005}"
      - "${FILES_HTTP1PORT:-7006}:${FILES_HTTP1PORT:-7006}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - postgres
      - minio
    restart: unless-stopped

  # Messages Service
  messages:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Messages/Dockerfile
    container_name: barkfluff-messages
    environment:
      - MESSAGES_PORT=${MESSAGES_PORT:-7007}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${MESSAGES_PORT:-7007}:${MESSAGES_PORT:-7007}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - postgres
      - redis
      - rabbitmq
    restart: unless-stopped

  # FastAuth Service
  fastauth:
    build:
      context: .
      dockerfile: Backend/BarkFluff.FastAuth/Dockerfile
    container_name: barkfluff-fastauth
    environment:
      - FASTAUTH_PORT=${FASTAUTH_PORT:-7008}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${FASTAUTH_PORT:-7008}:${FASTAUTH_PORT:-7008}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
    restart: unless-stopped

  # Onliner Service
  onliner:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Onliner/Dockerfile
    container_name: barkfluff-onliner
    environment:
      - ONLINER_PORT=${ONLINER_PORT:-7009}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${ONLINER_PORT:-7009}:${ONLINER_PORT:-7009}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - postgres
    restart: unless-stopped

  # Updates Service
  updates:
    build:
      context: .
      dockerfile: Backend/BarkFluff.Updates/Dockerfile
    container_name: barkfluff-updates
    environment:
      - UPDATES_PORT=${UPDATES_PORT:-7015}
      - CONFIGURATION_SERVICE_URL=http://configuration:${CONFIGURATION_PORT:-7003}
    ports:
      - "${UPDATES_PORT:-7015}:${UPDATES_PORT:-7015}"
    networks:
      - barkfluff-network
    depends_on:
      - configuration
      - rabbitmq
    restart: unless-stopped

volumes:
  postgres-data:
  rabbitmq-data:
  redis-data:
  minio-data:
```

## 📝 Пример .env файла

```env
# Порты сервисов
BEACON_PORT=7002
CONFIGURATION_PORT=7003
FILES_PORT=7005
FILES_HTTP1PORT=7006
FASTAUTH_PORT=7008
IDENTITY_PORT=7000
MESSAGES_PORT=7007
NOTIFICATION_PORT=7004
USERS_PORT=7001
UPDATES_PORT=7015
ONLINER_PORT=7009
NAVIGATOR_PORT=7010

# Порты инфраструктуры
MINIO_PORT=9000
MINIO_WEBPORT=9001
RABBITMQ_1PORT=5672
RABBITMQ_2PORT=15672
REDIS_PORT=6379
POSTGRES_PORT=5432

# Настройки PostgreSQL
POSTGRES_USER=barkfluff
POSTGRES_PASSWORD=barkfluff_secure_password
POSTGRES_HOST=postgres

# Настройки MinIO
MINIO_ROOT_USER=minioadmin
MINIO_ROOT_PASSWORD=minioadmin_secure_password

# Настройки RabbitMQ
RABBITMQ_USER=barkfluff
RABBITMQ_PASSWORD=barkfluff_secure_password

# Настройки базы данных для Configuration
CONFIGURATION_DATABASE=barkfluff_configuration
```

## 🚀 Запуск

### 1. Полный запуск всех сервисов

```bash
docker-compose up -d
```

### 2. Запуск только инфраструктуры

```bash
docker-compose up -d postgres rabbitmq redis minio
```

### 3. Запуск конкретных сервисов

```bash
docker-compose up -d configuration navigator identity users
```

### 4. Просмотр логов

```bash
# Все сервисы
docker-compose logs -f

# Конкретный сервис
docker-compose logs -f configuration

# Последние 100 строк
docker-compose logs --tail=100 -f identity
```

### 5. Остановка

```bash
docker-compose down
```

### 6. Полная очистка (включая volumes)

```bash
docker-compose down -v
```

## 🔍 Проверка работоспособности

### Проверка доступности gRPC сервисов

Используйте `grpcurl` для проверки:

```bash
# Configuration Service
grpcurl -plaintext localhost:7003 list

# Identity Service
grpcurl -plaintext localhost:7000 list

# Beacon Service
grpcurl -plaintext localhost:7002 list
```

### Проверка HTTP эндпоинтов

```bash
# Files Service HTTP/1.1
curl http://localhost:7006/health

# MinIO Console
# Откройте в браузере: http://localhost:9001

# RabbitMQ Management
# Откройте в браузере: http://localhost:15672
```

### Проверка здоровья контейнеров

```bash
docker-compose ps
```

## 📊 Мониторинг

### Использование ресурсов

```bash
docker stats
```

### Логи конкретного контейнера

```bash
docker logs barkfluff-configuration -f
```

## 🛠️ Отладка

### Вход в контейнер

```bash
docker exec -it barkfluff-configuration /bin/sh
```

### Проверка сетевых подключений

```bash
# Проверка что сервисы видят друг друга
docker exec barkfluff-identity ping configuration
docker exec barkfluff-users nslookup postgres
```

### Перезапуск сервиса

```bash
docker-compose restart identity
```

### Пересборка образа

```bash
docker-compose build --no-cache configuration
docker-compose up -d configuration
```

## 📝 Примечания

1. **Порядок запуска**: Configuration Service должен запускаться первым, так как все остальные сервисы зависят от него
2. **Healthchecks**: Добавлены healthcheck'и для инфраструктурных сервисов
3. **Volumes**: Данные PostgreSQL, RabbitMQ, Redis и MinIO сохраняются в именованных volumes
4. **Restart policy**: Все сервисы автоматически перезапускаются при падении
5. **Сеть**: Все контейнеры находятся в одной bridge сети для взаимодействия

## 🔐 Безопасность

⚠️ **Важно**: Не используйте дефолтные пароли в production!

1. Измените все пароли в `.env` файле
2. Используйте секреты Docker для чувствительных данных
3. Настройте TLS для production окружения
4. Ограничьте доступ к портам через firewall

## 🌐 Production рекомендации

1. Используйте Docker Swarm или Kubernetes для оркестрации
2. Настройте load balancer (например, Nginx или Traefik)
3. Используйте внешние managed сервисы для БД (RDS, Azure Database)
4. Настройте мониторинг (Prometheus + Grafana)
5. Настройте логирование (ELK Stack или Loki)
6. Используйте Docker secrets вместо environment variables для паролей
7. Настройте автоматический backup для БД
