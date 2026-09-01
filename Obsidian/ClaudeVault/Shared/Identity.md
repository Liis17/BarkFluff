# BarkFluff.Shared.Identity

Разделяемая библиотека с общими типами идентификации. Используется всеми микросервисами.

Расположение: `Shared/BarkFluff.Shared.Identity/`
Target framework: `net10.0`, без внешних зависимостей.

> 📋 Подробная карта файлов → [[Shared/Identity-ProjectMap]]

## Содержимое (три файла)

- **`ServiceId.cs`** — enum с ID каждого микросервиса (`Unknown=0`, `Identity=1`, `Users=2`, `Beacon=3`, ..., `Developers=12`, `Calls=13`, `Bots=14`, `Federation=15`). При добавлении нового сервиса — добавить сюда. `Federation` зарезервирован в Фазе 0 rearch — сам сервис ещё не создан (Фаза 1).
- **`TokenType.cs`** — enum: `Unknown=0`, `User=1`, `Service=2`, `Bot=3` (долгоживущий JWT бота, см. [[Backend/Bots]]).
- **`IdentityClaims.cs`** — строковые константы для JWT claims и gRPC metadata: `x-user-id`, `x-token-type`, `x-service-id`, `x-device-id`, `x-bot-token-id` (идентификатор выпуска bot-JWT для мгновенного отзыва).

## Использование

- [[Backend/GrpcServer]] — XAuth авторизация (политики на `TokenType`)
- [[Backend/Identity]] — генерация JWT с этими claims
- Все микросервисы — `builder.LoadConfiguration(ServiceId.XxxName)`

## Добавление нового сервиса

1. Добавить значение в `ServiceId` enum
2. Зарегистрировать в каталоге [[Backend/Settings]]
3. Использовать `builder.LoadConfiguration(ServiceId.NewService)` в `Program.cs`
