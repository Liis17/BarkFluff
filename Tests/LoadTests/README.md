# BarkFluff Load Tests

Нагрузочное тестирование микросервисов BarkFluff через gRPC.

## Установка зависимостей

```powershell
# grpcurl + ghz установлены в C:\Users\Fooxb\Tools\ и добавлены в PATH
# Переустановка:
# grpcurl: https://github.com/fullstorydev/grpcurl/releases
# ghz:     https://github.com/bojand/ghz/releases
```

## Структура

```
LoadTests/
├── common.ps1                Общие функции (auth, grpcurl, ghz)
├── run-load-test.ps1         SendMessage (write)
├── run-readonly-tests.ps1    6 read-only тестов (Messages, Users, Onliner)
├── run-chat-ops-test.ps1     Chat ops — 4 теста (GetChatInfo, Members, Pinned, Attachments)
├── run-mark-as-read-test.ps1 MarkAsRead flood
├── run-mixed-workload.ps1    Mixed workload (user flow simulation)
├── run-users-tests.ps1       Users — 7 тестов (GetUser, Search, Devices, Privacy, ...)
├── run-files-tests.ps1       Files — 3 теста (Storage, Stickers, Hash)
├── run-identity-tests.ps1    Identity — 2 теста (RefreshToken, Sessions)
├── run-auth-stress.ps1       Auth stress — логин флуд (без токена)
├── run-stream-test.ps1       Streaming stress (Updates SubscribeNewMessages)
├── run-multi-user-test.ps1   Multi-user (несколько ghz параллельно)
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

## Все тесты — сводная таблица

| # | Скрипт | Сервис | Методы | Тип |
|---|--------|--------|--------|-----|
| 1 | `run-load-test.ps1` | Messages | `SendMessage` | Write |
| 2 | `run-readonly-tests.ps1` | Messages, Users, Onliner | `ListChats`, `ListMessages`, `GetChatInfo`, `GetUser`, `SearchUsers`, `GetOnlineStatus` | Read |
| 3 | `run-chat-ops-test.ps1` | Messages | `GetChatInfo`, `ListChatMembers`, `ListPinnedMessages`, `ListChatAttachments` | Read |
| 4 | `run-mark-as-read-test.ps1` | Messages | `MarkAsRead` | Write |
| 5 | `run-users-tests.ps1` | Users | `GetUser`, `SearchUsers`, `CheckExistUsername`, `GetDevices`, `GetPrivacySettings`, `GetPersonalization`, `GetChatFolders` | Read |
| 6 | `run-files-tests.ps1` | Files | `GetUserStorageInfo`, `ListStickerPacks`, `CheckFileHash` | Read |
| 7 | `run-identity-tests.ps1` | Identity | `CreateToken` (refresh), `GetActiveSessions` | Read |
| 8 | `run-auth-stress.ps1` | Identity | `Auth` (логин) | Write |
| 9 | `run-mixed-workload.ps1` | Messages | ListChats → ListMessages → GetChatInfo → SendMessage → ListMessages | Mixed |
| 10 | `run-stream-test.ps1` | Updates | `SubscribeNewMessages` (server streaming) | Stream |
| 11 | `run-multi-user-test.ps1` | Messages | `SendMessage` (параллельно по разным чатам) | Write |

---

## 1. SendMessage — нагрузка на запись

```powershell
.\run-load-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 5000 `
  -UseTls
```

| Параметр | Описание |
|----------|----------|
| `-MessagesHost` | Адрес Messages |
| `-ChatId` | ID чата (авто — первый из ListChats) |
| `-PeerUserId` | ID пользователя (резолвит chat_id) |
| `-MessageText` | Текст сообщения |

---

## 2. Read-only тесты — Messages, Users, Onliner

6 тестов: `ListChats`, `ListMessages`, `GetChatInfo`, `GetUser`, `SearchUsers`, `GetOnlineStatus`. Re-auth перед каждым.

```powershell
.\run-readonly-tests.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -UsersHost "users.barkfluff.com:443" `
  -OnlinerHost "onliner.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 100 -TotalRequests 10000 `
  -UseTls
```

---

## 3. Chat Operations — 4 теста по одному чату

`GetChatInfo`, `ListChatMembers`, `ListPinnedMessages`, `ListChatAttachments`. Re-auth перед каждым.

```powershell
.\run-chat-ops-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 3000 `
  -UseTls
```

---

## 4. MarkAsRead Flood

Бомбит `MarkAsRead` с ID реальных сообщений.

```powershell
.\run-mark-as-read-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 100 -TotalRequests 10000 `
  -UseTls
```

---

## 5. Users Service — 7 тестов

`GetUser`, `SearchUsers`, `CheckExistUsername`, `GetDevices`, `GetPrivacySettings`, `GetPersonalization`, `GetChatFolders`. Re-auth перед каждым.

```powershell
.\run-users-tests.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -UsersHost "users.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 3000 `
  -UseTls
```

---

## 6. Files Service — 3 теста

`GetUserStorageInfo`, `ListStickerPacks`, `CheckFileHash`.

```powershell
.\run-files-tests.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -FilesHost "files.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 3000 `
  -UseTls
```

---

## 7. Identity Service — 2 теста

`CreateToken` (refresh flow), `GetActiveSessions`.

```powershell
.\run-identity-tests.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 3000 `
  -UseTls
```

---

## 8. Auth Stress — логин флуд

Тестирует `IdentityApi/Auth` напрямую. Токен не нужен — каждый запрос = логин.

```powershell
.\run-auth-stress.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Concurrency 50 -TotalRequests 5000 `
  -UseTls
```

---

## 9. Mixed Workload — user flow simulation

Последовательно: ListChats → ListMessages → GetChatInfo → SendMessage → ListMessages.
Re-auth каждые 10 итераций. Выводит per-step avg/p50/p95/p99.

```powershell
.\run-mixed-workload.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Iterations 100 `
  -UseTls
```

---

## 10. Streaming Stress — Updates SubscribeNewMessages

Открывает N стримов, держит T секунд, логирует живые/упавшие.

```powershell
.\run-stream-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -UpdatesHost "updates.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -Connections 200 -DurationSeconds 300 `
  -UseTls
```

---

## 11. Multi-user — параллельные ghz по разным чатам

```powershell
.\run-multi-user-test.ps1 `
  -IdentityHost "identity.barkfluff.com:443" `
  -MessagesHost "messages.barkfluff.com:443" `
  -Username "foxreal" `
  -Password (Read-Host "Password" -AsSecureString) `
  -NumUsers 10 -ConcurrencyPerUser 5 -TotalRequestsPerUser 500 `
  -UseTls
```

---

## Авторизация

Все скрипты (кроме auth-stress) используют XAuth:
1. `IdentityApi/Auth` → JWT access_token
2. Токен в metadata `x-auth-token` (plain text)
3. Остальные заголовки Base64(UTF-8): `x-device-id`, `x-device-name`, `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version`

Множественные тесты в одном скрипте делают re-auth перед каждым тестом для обхода истечения токена.

## Типы нагрузочных тестов

| Тип | Параметры | Что покажет |
|-----|-----------|-------------|
| Smoke | `-Concurrency 5 -TotalRequests 50` | Работает ли вообще |
| Load | `-Concurrency 50 -TotalRequests 5000` | Нормальная нагрузка |
| Stress | `-Concurrency 200 -Duration "120s"` | Превышение нормы |
| Spike | `-Concurrency 500 -TotalRequests 10000` | Резкий рост |
| Soak | `-Concurrency 50 -Duration "30m"` | Утечки, деградация |
| RPS limit | `-Rps 200 -Duration "60s"` | Плавная нагрузка |
