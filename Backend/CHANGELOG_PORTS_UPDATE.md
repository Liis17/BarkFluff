# ✅ Обновление Backend сервисов - Дефолтные порты

## 📝 Что было сделано

Все Backend сервисы BarkFluff теперь настроены для запуска на дефолтных портах как локально на localhost, так и в Docker контейнерах.

## 🔧 Изменённые файлы

### appsettings.json для всех сервисов

Добавлены дефолтные порты в `appsettings.json` для каждого Backend сервиса:

1. ✅ **BarkFluff.Configuration** - порт 7003
2. ✅ **BarkFluff.Identity** - порт 7000
3. ✅ **BarkFluff.Users** - порт 7001
4. ✅ **BarkFluff.Beacon** - порт 7002
5. ✅ **BarkFluff.Notification** - порт 7004
6. ✅ **BarkFluff.Files** - порты 7005 (gRPC) и 7006 (HTTP/1.1)
7. ✅ **BarkFluff.Messages** - порт 7007
8. ✅ **BarkFluff.FastAuth** - порт 7008
9. ✅ **BarkFluff.Onliner** - порт 7009
10. ✅ **BarkFluff.Updates** - порт 7015
11. ✅ **BarkFluff.Navigator** - порт 7010 (уже был)

### Ключевые изменения

#### 1. `WebApplicationBuilderExtensions.cs`
- Обновлён метод `LoadConfiguration` для приоритизации источников конфигурации:
  1. Переменная окружения `CONFIGURATION_SERVICE_URL`
  2. Значение из `appsettings.json` → `ConfigurationServiceAddr`
  3. Fallback на `http://localhost:7003` (вместо `http://configuration:7003`)

#### 2. `BarkFluff.FastAuth/Program.cs`
- Исправлен `ServiceId` с `ServiceId.Users` на `ServiceId.FastAuth`

#### 3. Миграции Configuration Service
- Добавлена новая миграция `20260201000000_AddOnlinerDbConfiguration.cs`
- Добавлены конфигурационные ключи для Onliner сервиса:
  - `OnlinerDb` - строка подключения к базе данных
  - `RunSettings:Port` для ServiceId 9

## 📋 Соответствие портов из .env

Все порты теперь соответствуют значениям из `.env` файла:

```env
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
```

## 🚀 Как использовать

### Локальная разработка

1. Запустите Configuration Service:
```bash
cd Backend/BarkFluff.Configuration
dotnet run
```

2. Запустите другие сервисы (они автоматически подключатся к `http://localhost:7003`):
```bash
cd Backend/BarkFluff.Identity
dotnet run
```

### Docker

1. Создайте `.env` файл с портами (см. пример в `Backend/DOCKER_SETUP.md`)
2. Запустите через docker-compose:
```bash
docker-compose up -d
```

Сервисы в Docker будут обращаться друг к другу по именам контейнеров:
- `http://configuration:7003`
- `http://identity:7000`
- и т.д.

## 📚 Документация

Создана подробная документация:

1. **`Backend/PORTS_CONFIGURATION.md`** - Полный список портов, конфигурационных ключей и настроек
2. **`Backend/DOCKER_SETUP.md`** - Подробная инструкция по настройке Docker Compose

## ✨ Преимущества

1. ✅ **Консистентность**: Все порты в одном месте и согласованы с `.env`
2. ✅ **Гибкость**: Можно переопределить через переменные окружения
3. ✅ **Простота**: Работает "из коробки" для локальной разработки
4. ✅ **Docker-ready**: Готово для развёртывания в контейнерах
5. ✅ **Service Discovery**: Сервисы находят друг друга автоматически в Docker сети

## 🔄 Порядок запуска

### Локально
1. Configuration Service (7003)
2. Navigator (7010)
3. Остальные сервисы параллельно

### Docker
Docker Compose сам управляет порядком через `depends_on`

## 🎯 Следующие шаги

1. Проверьте настройки базы данных в Configuration Service
2. Настройте MinIO credentials для Files сервиса
3. Настройте RabbitMQ credentials для сервисов с очередями
4. Добавьте JWT секретные ключи в Configuration
5. Настройте SMTP для Notification сервиса

## 🐛 Troubleshooting

Если сервис не может подключиться к Configuration:

1. Убедитесь что Configuration Service запущен
2. Проверьте переменную окружения `CONFIGURATION_SERVICE_URL`
3. Проверьте `appsettings.json` → `ConfigurationServiceAddr`
4. Для Docker - убедитесь что все контейнеры в одной сети

## 📞 Контакты

При возникновении проблем обращайтесь к документации в:
- `Backend/PORTS_CONFIGURATION.md`
- `Backend/DOCKER_SETUP.md`
