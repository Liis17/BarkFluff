# BarkFluff.Shared.Identity

Разделяемая библиотека с общими типами идентификации. Используется всеми микросервисами.

Расположение: `Shared/BarkFluff.Shared.Identity/`
Target framework: `net10.0`, без внешних зависимостей.

> 📋 Подробная карта файлов → [[Shared/Identity-ProjectMap]]

## Содержимое (три файла)

- **`ServiceId.cs`** — enum с числовыми ID от `Unknown=0` до `Federation=15`; актуальный полный список — в [[Shared/Identity-ProjectMap]]. Новые ID добавляются только в конец, чтобы не менять существующие значения.
- **`TokenType.cs`** — enum: `Unknown=0`, `User=1`, `Service=2`, `Bot=3` (долгоживущий JWT бота, см. [[Backend/Bots]]).
- **`IdentityClaims.cs`** — строковые константы для JWT claims и gRPC metadata: `x-user-id`, `x-token-type`, `x-service-id`, `x-device-id`, `x-bot-token-id` (идентификатор выпуска bot-JWT для мгновенного отзыва).

## Использование

- [[Backend/GrpcServer]] — XAuth авторизация (политики на `TokenType`)
- [[Backend/Identity]] — генерация JWT с этими claims
- Все микросервисы — `builder.LoadConfiguration(ServiceId.XxxName)`

## Добавление нового сервиса

1. Добавить новое значение в конец `ServiceId` enum
2. Зарегистрировать в каталоге [[Backend/Settings]]
3. Использовать `builder.LoadConfiguration(ServiceId.NewService)` в `Program.cs`
