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
- Prekey RPC регистрируют signed EC prekey и одноразовый EC pool; дополнительно принимают подписанный Kyber last-resort key и one-time Kyber pool для PQXDH. Сервер ограничивает размер сериализованных ключей и signatures, но не проверяет криптографические подписи: это обязанность клиента.
- `PrekeyStorage` блокирует строку устройства и существующий `DevicePrekeyBundle` внутри PostgreSQL-транзакции при регистрации, пополнении, ротации и fetch. Fetch с `require_pqxdh=false` сохраняет legacy-поведение и расходует только EC key. При `require_pqxdh=true` он не расходует ничего, если bundle не поддерживает PQXDH; иначе атомарно расходует EC и одну Kyber key, либо возвращает last-resort key с флагом fallback, если Kyber pool исчерпан.
- `OneTimePrekey` и `OneTimeKyberPrekey` хранят `ConsumedAt` вместо удаления строки. Повторная публикация того же `(device, id, payload)` идемпотентна даже после расходования и не воскрешает ключ; тот же ID с другим payload отклоняется. Identity/registration replacement физически удаляет оба пула прежней регистрации, включая consumed tombstones, и заменяет last-resort key в одной транзакции: новая identity может начать ID counters заново. Legacy registration без PQ полей сохраняет PQ-состояние при той же identity и registration ID; смена регистрации при активном PQ без нового last-resort отклоняется. В пределах той же identity/registration tombstones остаются до удаления устройства или явной замены регистрации; обычные retry их не удаляют.
- `GetOwnPrekeyStatus` возвращает непотребляющий статус только текущего авторизованного устройства: доступные размеры обоих пулов, `supports_pqxdh`, identity key, registration ID и last-resort key. `PeerDeviceInfo.supports_pqxdh` не меняет существующий смысл `has_bundle`. Это хранилище prekeys для настройки секретных чатов, а не общий KeyStore API.
- `ExportData` собирает профиль, сообщения и чаты через Messages, а сведения о связанных файлах — через Files; файл-экспорт содержит метаданные, а не бинарное содержимое. Users координирует агрегированный экспорт, но владение данными остаётся у сервисов-источников (`Features/ExportData/ExportDataCommandHandler.cs`).

## Хранение и связи

EF-модель описана в `Persistence/Contexts/UsersContext.cs`, миграции находятся в `Persistence/Migrations/`. `Program.cs` регистрирует PostgreSQL, XAuth, метрики и обработчик отзыва сессий. `Infrastructure/UserInfoQueueSender.cs` публикует события изменения профиля; `Consumers/SessionRevokedConsumer.cs` обновляет локальный cache отзыва токенов.

Messages, Identity, Files, Bots, Onliner и Federation используют внутренний Users API для профиля, устройств, privacy, prekeys или UUID resolution. Основные файлы: `Host/UsersApiService.cs`, `Host/UsersServerApiService.cs`, `Persistence/Services/PrekeyStorage.cs`, `Features/ResolveFederatedUser/`.

Связанные заметки: [[Backend/Identity]], [[Backend/Messages]], [[Backend/Files]], [[Backend/Bots]], [[Backend/Onliner]], [[Backend/Federation]]. Метрики: [[Backend/Users-Metrics]]. Тесты: `Tests/BarkFluff.Users.Tests`.
