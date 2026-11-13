# BarkFluff.Users

Users владеет локальными профилями и данными аккаунта: устройствами, приватностью, персонализацией, папками чатов, бейджами и device prekeys. Авторизацию выполняет Identity; Users предоставляет API профиля и аккаунта для клиентов и сервисов. Пути к коду ниже указаны относительно `Backend/BarkFluff.Users/`; `Shared/` и `Tests/` — от корня репозитория.

## API и идентичность

`Shared/BarkFluff.Proto/users_api.proto` разделяет пользовательский `UsersApi` (`TokenType.User`) и межсервисный `UsersServerApi` (`TokenType.Service`). Клиентский API включает профиль/поиск, настройки, устройства, mute, папки чатов и prekeys. Server API используется для поиска пользователей, операций с аккаунтом, ботами, федеративными UUID/профилями, бейджами, устройствами и административных задач.

`GetUserBadges` возвращает назначенные активные бейджи с полной и мини-картинкой и признаком `is_primary`; пропущенный optional `limit` означает выдачу всего списка. `SetPrimaryBadge` работает только с текущим пользователем из XAuth: `badge_id = 0` снимает выбор, иначе можно выбрать только активный бейдж, уже назначенный этому пользователю. Отсутствующая или неактивная связь возвращает NotFound. Переключение выполняется транзакционно; повторный выбор идемпотентен.

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

## Статус аккаунта

`Domain/AccountStatus.cs` задаёт wire-значения `Active=0`, `Blocked=1`, `PendingDeletion=2`, `Deleted=3`; статус, причина, время ограничения, дата удаления и отметка завершённой очистки хранятся в `User`. `users_api.proto` добавляет `User.account_status=14` и service-only RPC `SetAccountRestriction`, `GetAccountRestriction`, `ClaimAccountsForDeletion`, `CompleteAccountCleanup`. Причина и даты не входят в обычный `User`: `SearchUsersServer` возвращает `AccountRestrictionInfo` отдельно для AdminPanel, а его `User` для неактивного аккаунта маскирует имя, username, bio и аватар. `SetAccountRestriction`/`GetAccountRestriction` возвращают необходимые сырые данные для Identity; `FindByLogin` остаётся внутренним lookup для Identity и исключает Deleted. Клиентский поиск и публичные/федеративные lookup скрывают неактивные аккаунты; регистрационные проверки продолжают считать Blocked/PendingDeletion занятыми, а Deleted освобождает username и email.

Разрешённые действия задают переходы Active→Blocked/PendingDeletion, Blocked→Active/PendingDeletion и PendingDeletion→Active; повтор уже достигнутого состояния идемпотентен. Причина обязательна (до 500 символов) для Block и ScheduleDeletion, срок PendingDeletion — 7 суток. Операции не применяются к draft и bot аккаунтам. `GetUserPrivacy` скрывает presence и last seen для статуса != Active; Onliner запрашивает эти настройки у Users при проверке видимости.

CancelDeletion восстанавливает состояние до планирования удаления: аккаунт, ранее Blocked, остаётся Blocked с прежними причиной и `RestrictedAt`; остальные возвращаются в Active. Эти три nullable-поля предыдущего ограничения хранятся в `User` только на время PendingDeletion и очищаются при отмене или окончательном удалении. Строка `Privacy` не создаётся для Deleted; handlers настроек и персонализации также проверяют статус до ленивого создания.

`UsersStorage.ClaimAccountsForDeletion` условно переводит просроченные PendingDeletion в Deleted и в одной транзакции анонимизирует профиль, удаляет контакт, устройства/prekeys, настройки, privacy, personalization, папки, mute и user badges. Захват также возвращает все Deleted-аккаунты с `CleanupCompletedAt IS NULL`; после захвата отмена невозможна. Username получает уникальное значение с дефисом, запрещённое username regex. Сообщения и их файлы принадлежат Messages/Files и Users ими не удаляет. Запись остаётся как tombstone до `CompleteAccountCleanup` после очистки Identity/Files/Bots.

У Users нет outbox для этих изменений: статус/анонимизация фиксируются раньше публикации `UserChangedName` и `UserChangedAvatar`. Identity отдельно публикует `UserAccountStatusChanged` после изменения статуса; запись статуса и публикация события не атомарны. Ошибка публикации возвращается вызывающей стороне; восстановление выполняется идемпотентным повтором того же действия администратора, который повторяет side effects и публикацию. У Messages нет периодической сверки DM-кеша: событие инвалидирует локальный кеш каждого инстанса, а потерянное событие самоустраняется после TTL 45 секунд при следующей проверке статуса. У Bots есть дополнительная сверка раз в минуту и при старте.

## Хранение и связи

EF-модель описана в `Persistence/Contexts/UsersContext.cs`, миграции находятся в `Persistence/Migrations/`. `Program.cs` регистрирует PostgreSQL, XAuth, метрики и обработчик отзыва сессий. `Infrastructure/UserInfoQueueSender.cs` публикует события изменения профиля; `Consumers/SessionRevokedConsumer.cs` обновляет локальный cache отзыва токенов.

Профили и бейджи для аккаунтов со статусом, отличным от `Active`, маскируются: `SearchUsersServer` возвращает AdminPanel только `AccountRestrictionInfo`, а в `User` оставляет статус и placeholder; бейджи добавляются только активным пользователям. `GetUserBadges` также возвращает пустой список. Изменения профиля, назначений и основного бейджа проходят через `AccountRestrictionGuard`.

Бейдж хранит необязательный `MiniImageUrl` для 32×32 PNG; пустое значение при создании совместимо со старыми server API вызовами, а пустое при обновлении сохраняет прежний URL. `UserBadge.IsPrimary` помечает выбор пользователя. Частичный уникальный индекс `IX_UserBadges_UserId_Primary` ограничивает основной бейдж одной строкой на пользователя. Установка очищает прежний выбор и условно назначает активный бейдж; конфликт уникального индекса обрабатывается повторной попыткой. Деактивация бейджа снимает его primary-флаг, реактивация не восстанавливает выбор.

Основные места: `Features/Badges/`, `Features/AccountRestrictions/`, `Persistence/Services/UsersStorage.cs`, `Domain/AccountRestrictionGuard.cs`, `Domain/Badge.cs`, `Domain/UserBadge.cs`, `Mapping/BadgeMapping.cs`. Схема и конкурентные сценарии покрыты `Tests/BarkFluff.Users.Tests/Features/Badges/`; PostgreSQL-тесты используют отдельную временную базу и пропускаются без настройки тестового окружения.

Messages, Identity, Files, Bots, Onliner и Federation используют внутренний Users API для профиля, устройств, privacy, prekeys или UUID resolution. Onliner не кеширует `GetUserPrivacy`: фильтр спрашивает Users при проверке, поэтому скрытие online/last seen для неактивного локального пользователя начинает действовать сразу. Основные входы — `Host/UsersApiService.cs`, `Host/UsersServerApiService.cs`, `Persistence/Services/PrekeyStorage.cs` и `Features/ResolveFederatedUser/`.

Связанные заметки: [[Backend/Identity]], [[Backend/Messages]], [[Backend/Files]], [[Backend/Bots]], [[Backend/Onliner]], [[Backend/Federation]]. Метрики: [[Backend/Users-Metrics]]. Тесты: `Tests/BarkFluff.Users.Tests`.
