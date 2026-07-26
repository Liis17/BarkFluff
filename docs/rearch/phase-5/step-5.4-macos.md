# Этап 5.4 — macOS + общие Swift-пакеты: федерация

## Цель

Федерация появляется в Apple-стеке: **общие пакеты** (`BFProto`, `BFNetworking`, `BFCore`) получают весь транспортный и доменный слой федерации, macOS-приложение — соответствующий UI. iOS в 5.5 переиспользует пакеты как есть и добавляет только свои экраны.

Критерий роадмапа: E2E «нашёл → написал → получил ответ» на macOS.

## Контекст

- Контракт и правила отображения — `Obsidian/ClaudeVault/Клиенты/Federation-ClientGuide.md` (этап 5.1). **Прочитать целиком до начала.**
- Карты клиента — `Obsidian/ClaudeVault/Клиенты/macOS.md`, `macOS-ProjectMap.md`; дизайн-спецификация — `Клиенты/DesignDocument.md`.

Структура (проверено при планировании): iOS и macOS подключают **одни и те же** локальные пакеты из `Mac/Barkfluff/Packages/` (в `iOS/.../project.pbxproj` — `XCLocalSwiftPackageReference` с `relativePath = ../../Mac/Barkfluff/Packages/BFxxx`). Дублируются между платформами только `Features/**` (Views + ViewModels), бизнес-логика — общая.

| Что | Файл/каталог |
|---|---|
| Исходные proto | `Mac/Barkfluff/Protos/*.proto` |
| Сгенерированный Swift | `Mac/Barkfluff/Packages/BFProto/Sources/BFProto/Generated/*.{pb,grpc}.swift` |
| Соединение и бутстрап | `Packages/BFNetworking/Sources/BFNetworking/Connection/ConnectionManager.swift` (`bootstrap(host:port:)` → Beacon `GetServerInfo` → `ServiceEndpoints`; интерсепторы auth/device) |
| Репозитории | `Packages/BFNetworking/Sources/BFNetworking/Repositories/{UsersRepository,MessagesRepository,FilesRepository}.swift` |
| Домен и сервисы | `Packages/BFCore/Sources/BFCore/Models/User.swift`, `Services/{Protocols,Implementations}/…` (`UserService`, `ChatService`, статусы) |
| macOS-экраны | `Mac/Barkfluff/Barkfluff/Features/{UserSearch,Conversation,ChatList,Settings}/…` |
| Локализация | `Mac/Barkfluff/Barkfluff/Resources/Localizable.xcstrings`, `Packages/BFCore/Sources/BFCore/Resources/Localizable.xcstrings` (en/de/es/ru/zh-Hans) |

## Изменение 1 — proto и его генерация

1. Обновить `Mac/Barkfluff/Protos/` копиями из `Shared/BarkFluff.Proto/` (`users_api`, `messages_api`, `onliner_api`, `beacon_api`, `files_api`, `shared`) — целиком, без ручных правок.
2. Перегенерировать Swift в `BFProto/Sources/BFProto/Generated/`. Скрипта генерации в репозитории нет — определи фактически использованные версии `protoc-gen-swift`/`protoc-gen-grpc-swift` по заголовкам существующих файлов и повтори их (иначе диф станет нечитаемым из-за смены генератора).
3. **Зафиксировать команду генерации** в `Mac/Barkfluff/Protos/README.md` (создать, если нет): 5–10 строк — какие плагины, какие версии, какая команда. Инструмент не пишем, воспроизводимость документируем.
4. Собрать пакет: генерация не должна ломать существующие вызовы.

## Изменение 2 — BFNetworking: знание о своей ноде и федеративные вызовы

1. `ConnectionManager`: из ответа `GetServerInfo` сохранить `server_name` и `federation_enabled`; отдать наружу (свойство/метод) — это единственный источник для UI-гейта `federationAvailable`.
2. `UsersRepository`: `resolveFederatedUser(fid:)` → `UsersApi.ResolveFederatedUser`; маппинг ответа в существующий тип пользователя (см. Изменение 3). Плюс поддержка `deny_federated_dm` в get/update privacy.
3. `MessagesRepository`: `sendMessage` и `getPersonChatId` — вариант с `user_uuid` (заполнять строго одно из полей). Не ломать существующие сигнатуры: добавить перегрузку/опциональный параметр.
4. Подписка на статусы: передавать `user_uuids` наряду с `user_ids` (иначе remote-собеседник всегда офлайн); принимать `UserOnlineStatus.user_uuid` и `TypingEvent.user_uuid`.

## Изменение 3 — BFCore: модель и сервисы

1. `User` (`Models/User.swift`): добавить `uuid` и `serverName` (опциональные). Для remote-пользователя `id` (Int64) не имеет смысла — предусмотреть в модели явный признак (например, вычислимое `isRemote`), а не «id == 0» по месту использования.
2. `UserService`: `resolveFederatedUser(fid:)`, распознавание FID во вводе (одна функция-парсер, используется и macOS, и iOS — правила в гайде 5.1); `ChatService`: работа с uuid-собеседником.
3. Сервис статусов: uuid-ветка (матчинг входящих статусов/typing по uuid), трактовка `UNKNOWN` для remote как «нет данных».
4. Ошибки: типизировать `FederatedDmRejected`, `FederatedGroupsNotSupported`, «сервер не найден» так, чтобы UI показывал текст из гайда, а не generic-ошибку (см., как обрабатываются коды ошибок сейчас).
5. Строки федерации, живущие в BFCore, — в `BFCore/Resources/Localizable.xcstrings`, все пять языков.

## Изменение 4 — macOS UI

1. **Поиск** (`Features/UserSearch`): ввод похож на FID и `federationAvailable` → резолв вместо `searchUsers` (минимальная длина/дебаунс сохраняются); результат — одна карточка с FID; ошибки — текстом.
2. **Шапка чата** (`ConversationHeaderView`) и **список чатов** (`ChatRowView`): для remote-собеседника — подпись/бейдж ноды, в шапке полный FID; servername рисуется как пришёл (punycode), без декодирования.
3. **Сообщения**: `federated_read_by` объединять с `read_by`; typing от remote (по uuid) — «@bob:node печатает…».
4. **Статусы**: подписка включает uuid; `UNKNOWN` для remote → «нет данных».
5. **Вложения** (`CachedImageView` и путь скачивания): карточка рисуется по метаданным сразу; `503` → placeholder «Сервер собеседника недоступен» + повтор; `404` → «Файл недоступен» без повтора; `file_name` экранировать/санитизировать при сохранении.
6. **Приватность** (`Features/Settings`): переключатель «Разрешить сообщения с других серверов» = `!deny_federated_dm` + подпись «действует только на новые переписки»; виден только при `federationAvailable`.
7. Стиль и компоненты — существующие (`DesignSystem`), по `DesignDocument`. Новых визуальных языков не вводить.

## Чего НЕ делать

- Не трогать `iOS/**` — это 5.5 (даже если «там та же строчка»).
- Не менять `Shared/BarkFluff.Proto` и бэкенд.
- Не рефакторить `ConnectionManager`/репозитории «заодно»: только добавления.
- Не выносить дублирующиеся ViewModel'ы iOS/macOS в общий пакет — соблазн большой, но это отдельная задача вне фазы (и она сломает 5.5, который пишется параллельно по факту).

## Критерии готовности

1. Сборка: `xcodebuild -project Mac/Barkfluff/Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build` (или актуальная схема) — успех. Если Xcode/toolchain недоступны в окружении исполнителя — явно зафиксировать это в отчёте и коммите, не выдавая сборку за пройденную.
2. `Mac/Barkfluff/Protos/*.proto` совпадают с `Shared/BarkFluff.Proto/` (diff пуст); сгенерированные файлы обновлены той же связкой плагинов, что раньше; команда записана в `Protos/README.md`.
3. При `federation_enabled = false` поведение приложения не отличается от текущего (поиск, чаты, вложения, настройки).
4. Новые строки — во всех пяти языках обоих каталогов (`app` и `BFCore`).
5. **[делает разработчик]** E2E на стенде: найти `@user:node2` → написать → ответ; бейдж ноды/FID; статус и typing remote-собеседника; скачивание вложения с чужой ноды; остановленная нода 2 → placeholder, приложение не зависает; тумблер приватности блокирует новый fed-чат.
6. Obsidian: `Клиенты/macOS.md` (раздел «Федерация», ссылка на гайд), `macOS-ProjectMap.md` — новые файлы, если появились.
7. Коммит: `feat(rearch-phase5): 5.4 — федерация в macOS-клиенте и общих Swift-пакетах`.
