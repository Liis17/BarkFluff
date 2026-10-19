# BarkFluff.Bots

Боты — это пользователи платформы с `IsBot=true`; отдельная база Bots хранит владельца, токен, роль и команды. Пути к коду сервиса ниже отсчитываются от `Backend/BarkFluff.Bots/`; `Shared/` и `Tests/` — от корня репозитория.

## Контракты и транспорт

- `Shared/BarkFluff.Proto/bots_api.proto` описывает внутренний `BotsServerApi` и внешний `BotsExternalApi` для ботов (по умолчанию gRPC: 7027).
- HTTP API на HTTP/1.1 слушает порт 7028; `Host/Http/BotApiEndpoints.cs` задаёт `/bot/{method}`: получение профиля, отправка/правка/удаление сообщений, отправка фото/документов, получение файла и профиля, управление командами, long-poll `getUpdates`.
- Внешние gRPC и HTTP-запросы используют `TokenType.Bot` через заголовок `x-auth-token`. `BotAccessValidator` дополнительно проверяет актуальный `TokenId` и лимит запросов. Токены выпускает Identity через `BotTokenIssuer`; Bots хранит идентификатор токена, но не сам bearer token.

## Поток сообщений и обновлений

- Сообщения от бота проходят через `MessagesServerApi.SendMessageServer`; Messages проверяет членство в чате и запрещает обычному боту первым начинать личный диалог. Та же серверная граница проверяет авторство при правке и удалении.
- Bots принимает `NewMessageEvent`, сохраняет подходящие обновления в `BotUpdates` и публикует сигнал для локальных обработчиков запросов. Любой инстанс после сигнала читает уже сохранённое обновление.
- `getUpdates(offset)` подтверждает сохранённые записи с меньшим ID. Redis не допускает более одного активного polling-потребителя на бота и хранит общий для инстансов лимит запросов.
- `SystemBotsSeeder` создаёт встроенных `@botfather` и `@barkfluffnotifier`; логика BotFather находится в `Services/BotFather/`, уведомлений о входе — в `Consumers/LoginNotificationConsumer.cs`.

## Хранение и жизненный цикл

`BotsContext` хранит владельца/профиль бота, `TokenId`, системную роль, `LastConfirmedUpdateId` и команды в JSONB. `BotUpdates` хранит JSONB-обновления; `BotFatherSessions` — временное состояние диалога. `BotsCleanupService` удаляет старые обновления и сессии. `BotRegistryCache` синхронизируется между инстансами событиями об изменении реестра.

Для загрузки файлов бот вызывает Files server API и подчиняется квоте ботов; `getFile` возвращает временную ссылку вместо прямого доступа к произвольному ID вложения. Настройки находятся в Settings, ServiceId 14: `RunSettings`, `BotsDb`, `Redis`, а также адреса/токены Users, Messages, Files и Identity.

Источники: `Host/BotsExternalApiService.cs`, `Host/BotsServerApiService.cs`, `Host/Http/BotApiEndpoints.cs`, `Services/BotAccessValidator.cs`, `Persistence/BotsContext.cs`, `Persistence/Services/BotUpdatesStorage.cs`, `Consumers/BotUpdateSignalConsumer.cs`. Тесты: `Tests/BarkFluff.Bots.Tests` (путь от корня репозитория).

Связанные сервисы: [[Backend/Users]], [[Backend/Messages]], [[Backend/Files]], [[Backend/Identity]], [[Backend/Settings]].
