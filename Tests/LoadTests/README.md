# BarkFluff Load Tests

Нагрузочное тестирование микросервисов BarkFluff через gRPC.

## Установка зависимостей

```powershell
# grpcurl + ghz уже установлены в C:\Users\Fooxb\Tools\ и добавлены в PATH
# Если нужно переустановить — скачиваем бинарники:
# grpcurl: https://github.com/fullstorydev/grpcurl/releases
# ghz:     https://github.com/bojand/ghz/releases
```

## Структура

```
LoadTests/
├── common.ps1                Общие функции (auth, grpcurl, ghz)
├── run-load-test.ps1         SendMessage (write) — нагрузка на запись
├── run-readonly-tests.ps1    6 read-only тестов (Messages, Users, Onliner)
├── run-stream-test.ps1       Streaming stress test (Updates SubscribeNewMessages)
├── run-multi-user-test.ps1   Мультипользовательский тест (несколько ghz параллельно)
├── reports/                  HTML отчёты ghz + JSON результаты
└── README.md
```

## Общие параметры

| Параметр | Описание | Обязательный |
|----------|----------|:---:|
| `-IdentityHost` | Адрес Identity (с портом) | Да |
| `-Username` | Логин | Да |
| `-Password` | Пароль (SecureString) | Да |
| `-UseTls` | Использовать TLS (порт 443) | Нет |
| `-ProtoPath` | Путь к proto файлам | Нет (авто) |
| `-Concurrency` | Параллельные соединения | Нет (50) |
| `-TotalRequests` | Всего запросов | Нет (3000-5000) |
| `-Duration` | Длительность вместо TotalRequests | Нет |
| `-Rps` | Лимит запросов/сек | Нет |
| `-OutputDir` | Папка отчётов | Нет (reports/) |

---

## 1. SendMessage — нагрузка на запись

Нагружает `MessagesApi/SendMessage`. Автоматически находит первый чат если не указан `-ChatId`.

```powershell
.\run-load-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 5000 `
  -UseTls
```

### Дополнительные параметры

| Параметр | Описание |
|----------|----------|
| `-MessagesHost` | Адрес Messages сервиса |
| `-ChatId` | ID чата (авто — первый из ListChats) |
| `-PeerUserId` | ID пользователя (резолвит chat_id автоматически) |
| `-MessageText` | Тест сообщения (по умолчанию "Load test message from ghz") |

### Примеры

```powershell
# Конкретный чат, 100RPS лимит, 2 минуты
.\run-load-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -ChatId "019caae6-65b6-77bf-aba4-b2e92879904a" `
  -Concurrency 200 -Duration "120s" -Rps 100 `
  -UseTls

# По user_id вместо chat_id
.\run-load-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -PeerUserId 42 `
  -UseTls

# Localhost (Docker с пробросом портов)
.\run-load-test.ps1 `
  -IdentityHost "localhost:7000" `
  -MessagesHost "localhost:7007" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 5000
```

---

## 2. Read-only тесты — нагрузка на чтение

6 тестов, выполняются последовательно. Каждый генерирует отдельный HTML-отчёт.

| # | Сервис | Метод | Запрос |
|---|--------|-------|--------|
| 1 | Messages | `ListChats` | pagination: offset=0, size=20 |
| 2 | Messages | `ListMessages` | 20 сообщений из первого чата |
| 3 | Messages | `GetChatInfo` | Инфо первого чата |
| 4 | Users | `GetUser` | Профиль текущего пользователя |
| 5 | Users | `SearchUsers` | query="a", pagination size=20 |
| 6 | Onliner | `GetOnlineStatus` | Статус текущего пользователя |

```powershell
.\run-readonly-tests.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -UsersHost "users.barkfluff.com:443" `
  -OnlinerHost "onliner.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 3000 `
  -UseTls
```

### Дополнительные параметры

| Параметр | Описание |
|----------|----------|
| `-MessagesHost` | Адрес Messages |
| `-UsersHost` | Адрес Users |
| `-OnlinerHost` | Адрес Onliner |

---

## 3. Streaming stress test — прочность стримов

Открывает N параллельных `SubscribeNewMessages` стримов на Updates сервисе.
Каждые 5 секунд логирует количество живых/упавших соединений.

```powershell
.\run-stream-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -UpdatesHost "updates.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Connections 100 `
  -DurationSeconds 60 `
  -UseTls
```

### Дополнительные параметры

| Параметр | Описание | По умолчанию |
|----------|----------|:---:|
| `-UpdatesHost` | Адрес Updates | Обязательный |
| `-Connections` | Количество стримов | 100 |
| `-DurationSeconds` | Время удержания | 60 |

### Результат

JSON-файл с метриками:
- `connected` — сколько стримов открылось
- `alive_after` — сколько осталось живыми
- `dropped_during` — сколько упало за время теста
- `drop_rate_pct` — процент падения

---

## 4. Multi-user тест

Несколько ghz-воркеров параллельно по разным чатам.

```powershell
.\run-multi-user-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -NumUsers 10 `
  -ConcurrencyPerUser 5 `
  -TotalRequestsPerUser 500 `
  -UseTls
```

---

## Авторизация

Все скрипты используют XAuth:
1. Вызов `IdentityApi/Auth` → получение JWT access_token
2. Токен передаётся в metadata `x-auth-token` (plain text)
3. Остальные заголовки — Base64(UTF-8): `x-device-id`, `x-device-name`, `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version`

## Типы нагрузочных тестов

| Тип | Как запустить | Что покажет |
|-----|---------------|-------------|
| Smoke | `-Concurrency 5 -TotalRequests 50` | Работает ли вообще |
| Load | `-Concurrency 50 -TotalRequests 5000` | Нормальная нагрузка |
| Stress | `-Concurrency 200 -Duration "120s"` | Превышение нормы |
| Spike | `-Concurrency 500 -TotalRequests 10000` | Резкий рост |
| Soak | `-Concurrency 50 -Duration "30m"` | Утечки, деградация |
| RPS limit | `-Rps 200 -Duration "60s"` | Плавная нагрузка |
