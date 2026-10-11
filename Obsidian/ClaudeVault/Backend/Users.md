# BarkFluff.Users

Users владеет локальными профилями и данными аккаунта: устройствами, приватностью, персонализацией, папками чатов, бейджами и device prekeys. Авторизацию выполняет Identity; Users предоставляет API профиля и аккаунта для клиентов и сервисов. Пути к коду ниже указаны относительно `Backend/BarkFluff.Users/`; `Shared/` и `Tests/` — от корня репозитория.

## API и идентичность

`Shared/BarkFluff.Proto/users_api.proto` разделяет пользовательский `UsersApi` (`TokenType.User`) и межсервисный `UsersServerApi` (`TokenType.Service`). Клиентский API включает профиль/поиск, настройки, устройства, mute, папки чатов и prekeys. Server API используется для поиска пользователей, операций с аккаунтом, ботами, федеративными UUID/профилями, бейджами, устройствами и административных задач.

Локальный пользователь имеет постоянный UUID для федерации. Удалённый пользователь представлен UUID и server, не локальным account ID. `ResolveFederatedUser`, `UpsertRemoteUsers`, `GetUsersByUuid` и RPC федеративного профиля реализованы в коде, это не заглушки только в proto.

## Приватность и состояние клиента

- `GetPrivacySettings`/`UpdatePrivacySettings` хранят online visibility и настройки федерации. Origin-сервисы применяют privacy владельца перед выдачей профиля или presence.
- Mute чата и настройки уведомлений принадлежат Users; Messages запрашивает mute при формировании списка чатов.
- Персонализация включает фоны чатов и постер профиля. Загрузку исходных изображений обслуживает Files.
- Папки чатов хранят пользовательский список ID чатов с порядком. Users не проверяет существование чатов через Messages и не публикует stream изменений папок; клиент синхронизирует их через RPC папок.

## Устройства, prekeys и экспорт

- Устройства и Firebase tokens поддерживают управление сессиями и push notifications.
- Prekey RPC регистрируют signed prekey устройства и одноразовые prekeys, получают bundle устройства собеседника, перечисляют peer devices, пополняют пул и меняют signed prekey. `PrekeyStorage.FetchBundleAsync` атомарно расходует one-time key; когда пул пуст, bundle возвращается без него. Это хранилище prekeys для настройки секретных чатов, а не общий KeyStore API.
- `ExportData` собирает профиль, сообщения и чаты через Messages, а сведения о связанных файлах — через Files; файл-экспорт содержит метаданные, а не бинарное содержимое. Users координирует агрегированный экспорт, но владение данными остаётся у сервисов-источников (`Features/ExportData/ExportDataCommandHandler.cs`).

## Хранение и связи

EF-модель описана в `Persistence/Contexts/UsersContext.cs`, миграции находятся в `Persistence/Migrations/`. `Program.cs` регистрирует PostgreSQL, XAuth, метрики и обработчик отзыва сессий. `Infrastructure/UserInfoQueueSender.cs` публикует события изменения профиля; `Consumers/SessionRevokedConsumer.cs` обновляет локальный cache отзыва токенов.

Messages, Identity, Files, Bots, Onliner и Federation используют внутренний Users API для профиля, устройств, privacy, prekeys или UUID resolution. Основные файлы: `Host/UsersApiService.cs`, `Host/UsersServerApiService.cs`, `Persistence/Services/PrekeyStorage.cs`, `Features/ResolveFederatedUser/`.

Связанные заметки: [[Backend/Identity]], [[Backend/Messages]], [[Backend/Files]], [[Backend/Bots]], [[Backend/Onliner]], [[Backend/Federation]]. Метрики: [[Backend/Users-Metrics]]. Тесты: `Tests/BarkFluff.Users.Tests`.
