# BarkFluff.Shared.Identity — Карта проекта

Расположение: `Shared/BarkFluff.Shared.Identity/`
Target framework: `net10.0`
Зависимости: нет (zero-dependency библиотека)

---

## Назначение библиотеки

Разделяемая библиотека с минимальным набором типов идентификации и аутентификации, используемая **всеми** микросервисами платформы BarkFluff. Не имеет внешних NuGet-зависимостей — только базовые примитивы .NET.

---

## Файлы проекта

### `BarkFluff.Shared.Identity.csproj`
Файл проекта. net10.0, Nullable enable, ImplicitUsings enable. Никаких NuGet-зависимостей.

---

### `ServiceId.cs`
**Enum `ServiceId`** — числовые идентификаторы всех микросервисов платформы.

| Значение | ID | Назначение |
|----------|----|------------|
| `Unknown` | 0 | Значение по умолчанию / не определён |
| `Identity` | 1 | Сервис аутентификации и JWT |
| `Users` | 2 | Профили и устройства пользователей |
| `Beacon` | 3 | Точка входа клиентов |
| `Notifications` | 4 | Email-уведомления (RabbitMQ consumer) |
| `Files` | 5 | Файлы, S3, стикеры |
| `Messages` | 6 | Чаты и сообщения |
| `FastAuth` | 7 | QR-авторизация устройств |
| `Updates` | 8 | Real-time стриминг событий |
| `Onliner` | 9 | Онлайн-статусы |
| `CloudMessaging` | 10 | Push-уведомления (Firebase) |
| `Web` | 11 | gRPC-Web прокси |
| `Developers` | 12 | Портал документации |

> При добавлении нового сервиса — добавить значение сюда, затем зарегистрировать в БД [[Backend/Configuration]].

---

### `TokenType.cs`
**Enum `TokenType`** — тип JWT-токена, передаётся в claim `x-token-type`.

| Значение | ID | Назначение |
|----------|----|------------|
| `Unknown` | 0 | Значение по умолчанию |
| `User` | 1 | Токен обычного пользователя |
| `Service` | 2 | Межсервисный токен (XAuth) |

> Используется в [[Backend/GrpcServer]] при проверке политик XAuth.

---

### `IdentityClaims.cs`
**Класс `IdentityClaims`** — строковые константы имён JWT claims и gRPC metadata-заголовков.

| Константа | Значение | Назначение |
|-----------|----------|------------|
| `UserId` | `"x-user-id"` | ID пользователя |
| `TokenType` | `"x-token-type"` | Тип токена (`User` / `Service`) |
| `ServiceId` | `"x-service-id"` | ID сервиса-эмитента токена |
| `DeviceId` | `"x-device-id"` | ID устройства пользователя |

> Все четыре поля прописываются [[Backend/Identity]] при генерации JWT и читаются [[Backend/GrpcServer]] при авторизации входящих запросов.

---

## Схема использования

```
Identity (генерирует JWT)
    → IdentityClaims.* (имена claims)
    → TokenType (User / Service)
    → ServiceId (источник токена)

GrpcServer (проверяет JWT)
    → IdentityClaims.* (читает из metadata)
    → TokenType (XAuth политики)

Все сервисы (загрузка конфигурации)
    → ServiceId.XxxName → builder.LoadConfiguration(ServiceId.XxxName)
```

---

## Связанные файлы Obsidian

- [[Shared/Identity]] — краткое описание библиотеки
- [[Backend/Identity]] — сервис аутентификации
- [[Backend/GrpcServer]] — использует TokenType и IdentityClaims для XAuth
- [[Backend/Configuration]] — хранит конфигурацию по ServiceId
