# Этап 5.2 — Android V1: федерация в клиенте

## Цель

Пользователь Android-клиента находит человека по `@username:servername`, пишет ему, видит его статус/typing/вложения и может запретить входящие с чужих нод. Критерий роадмапа: E2E «нашёл → написал → получил ответ».

Клиент: `Android/Barkfluff.Client.Android` (+ модуль `Android/core`). **`Android/Barkfluff.ClientV2.Android` не трогать.**

## Контекст

- Контракт и правила отображения — `Obsidian/ClaudeVault/Клиенты/Federation-ClientGuide.md` (этап 5.1). **Прочитать целиком до начала**: этот план не повторяет правила, он говорит, где в Android их применить.
- Карта клиента — `Obsidian/ClaudeVault/Клиенты/Android.md`, `Android-ProjectMap.md`, `Android-FileIndex.md`.

Точки интеграции (проверено при планировании; номера строк ориентировочные — читай актуальный код):

| Что | Файл |
|---|---|
| Копии proto | `Android/core/src/main/proto/*.proto` (генерация — protobuf-plugin в `Android/core/build.gradle.kts`) |
| gRPC-фасад | `Android/core/src/main/java/com/barkfluff/client/grpc/GrpcManager.kt` (`searchUsers` ~1481, `getServerInfo` ~765, `getFileDownloadUrl` ~1571, `getPrivacySettings`/`updatePrivacySettings` ~618/636, `data class UserData` ~2256) |
| Realtime | `.../grpc/RealtimeService.kt` (`onlineStatuses`, `typingEvents`, подписки) |
| Отправка | `.../repository/ChatRepository.kt` (`sendMessage` ~90) |
| Поиск | `.../client/SearchActivity.kt` (+ `adapter/UserAdapter.kt`) |
| Профиль | `.../client/UserProfileActivity.kt` |
| Чат | `.../client/ChatActivity.kt` (онлайн-статус ~2569, typing ~3086), `ChatsFragment.kt`, `adapter/ChatAdapter.kt` |
| Файлы/аватары | `.../utils/AvatarLoader.kt`, кеш URL (`FileUrlCache`) |
| Приватность | `.../client/PrivacySettingsActivity.kt` |
| Нода | `.../client/SelectServerActivity.kt`, `.../data/GlobalParam.kt` (адреса сервисов) |
| Строки | `app/src/main/res/values{,-en,-de,-es,-zh-rCN}/strings.xml` |

## Изменение 1 — синхронизация proto и модель ноды

1. Скопировать из `Shared/BarkFluff.Proto/` в `Android/core/src/main/proto/` актуальные `users_api.proto`, `messages_api.proto`, `onliner_api.proto`, `beacon_api.proto`, `shared.proto`, `files_api.proto` (целиком, без ручных правок). Собрать модуль — codegen должен пройти.
2. `GlobalParam` (или его аналог, где хранится информация о выбранной ноде) дополнить `serverName` и `federationEnabled` из `GetServerInfo`; заполнять при бутстрапе в `SelectServerActivity`/при переподключении.
3. Единая точка `isFederationAvailable = federationEnabled && serverName.isNotBlank()` — по ней гейтятся **все** федеративные ветки UI.

## Изменение 2 — резолв FID в поиске

В `SearchActivity`:

1. Распознать во вводе FID (маска и правила — гайд 5.1). Похоже на FID **и** `isFederationAvailable` → вместо `searchUsers` звать новый `GrpcManager.resolveFederatedUser(fid)`.
2. Servername равен своему → обычный локальный поиск по username (без federation-ветки).
3. Результат — одна карточка: имя/аватар + строка FID; `found = false` или ошибка → понятное сообщение (тексты — гайд, раздел «Ошибки»), а не пустой список.
4. `UserData`/`UserDisplayItem` дополнить `uuid` и `serverName`; для remote `userId` остаётся нулевым — весь код, который дальше берёт `userId`, обязан ветвиться на uuid (см. Изменение 3).

## Изменение 3 — начало переписки по uuid

1. `GrpcManager`/`ChatRepository`: `sendMessage` и `getPersonChatId` получают возможность передать `user_uuid` вместо `user_id` (в proto это поля того же `oneof`/пары — заполнять строго одно).
2. `SearchActivity` → открытие чата с remote-пользователем: если `chatId` ещё нет, путь «отправить первое сообщение по uuid» (либо `getPersonChatId(user_uuid)`, если сервер уже создал чат) — выбрать тот же сценарий, что сейчас используется для локальных, не изобретать новый.
3. Ошибки отправки (`FederatedDmRejected`, `ServerNotFound`) показывать текстом из гайда, не «Ошибка отправки».

## Изменение 4 — отображение remote-участников в чатах

1. `ChatActivity` (шапка) и `ChatAdapter` (список): участник с непустым `server_name`, не равным своему, помечается подписью/бейджем ноды; в шапке чата — полный FID под именем. Servername рисовать **как пришёл** (сервер отдаёт punycode; клиент ничего не декодирует — правило гайда).
2. Прочтения: `Message.federated_read_by` (uuid'ы) объединять с существующим `read_by` при расчёте статуса «прочитано».
3. Аватар/имя remote-участника приходят в тех же полях, что у локального (сервер подставляет кеш) — отдельной ветки загрузки не требуется.

## Изменение 5 — presence и typing remote-собеседника

1. `RealtimeService`: подписка на статусы обязана передавать `user_uuids` (наряду с `user_ids`) — иначе remote-собеседник всегда «не в сети». Обновление подписки (`ChangeUsersInSubscription`) — так же.
2. Входящий `UserOnlineStatus` с непустым `user_uuid` матчить по uuid; `STATUS_TYPE_ID_UNKNOWN` для remote отображать как «нет данных» (не как «был(а) давно»).
3. `TypingEvent` с непустым `user_uuid` → «печатает…» от remote-участника (имя из карточки участников, иначе FID).

## Изменение 6 — вложения недоступного origin

В местах скачивания/предпросмотра (`AvatarLoader`, кеш URL, открытие вложений):

1. Плитку/карточку файла рисовать **сразу по метаданным** сообщения, до получения URL (снапшот уже есть в сообщении).
2. HTTP `503` → placeholder «Сервер собеседника недоступен» + повтор по тапу; `404` → «Файл недоступен» без повтора (коды и семантика — гайд 5.1, раздел «Файлы»).
3. `file_name` из федеративного сообщения экранировать при отображении и санитизировать при сохранении в загрузки (path traversal).

## Изменение 7 — настройка приватности

`PrivacySettingsActivity`: переключатель «Разрешить сообщения с других серверов» = `!deny_federated_dm`, с подписью, что настройка действует только на новые переписки. Показывать только при `isFederationAvailable`.

## Изменение 8 — строки и документация

- Все новые строки — в пяти `strings.xml` (ru/en/de/es/zh-rCN). Никаких строк в коде.
- `Obsidian/ClaudeVault/Клиенты/Android.md` — раздел «Федерация» (что умеет клиент, где точки входа), ссылка на `[[Клиенты/Federation-ClientGuide]]`; `Android-FileIndex.md`/`Android-ProjectMap.md` — если добавлены новые файлы.

## Чего НЕ делать

- Не трогать `Android/Barkfluff.ClientV2.Android`, WPF, Linux, Apple-клиенты.
- Не менять бэкенд (включая proto-источник в `Shared/`): нужное поле отсутствует — останови этап и сообщи.
- Не добавлять федеративные группы/звонки, не переделывать существующие экраны поиска/чата «заодно».
- Не кешировать содержимое remote-файлов на устройстве сверх существующего механизма кеша (правило «без кеша» касается сервера, но и на клиенте не изобретать новый слой).

## Критерии готовности

1. `cd Android/Barkfluff.Client.Android && ./gradlew assembleDebug` — успешно; proto-копии совпадают с `Shared/BarkFluff.Proto/` (diff пуст).
2. Существующие сценарии не изменились: локальный поиск, локальный чат, отправка, вложения, аватары, приватность — проверены по коду (диффы точечные) и, где есть тесты, тестами.
3. При `federation_enabled = false` (или пустом `server_name`) ни одна федеративная ветка не активируется: поиск ведёт себя как раньше, тумблер приватности скрыт, FID-ввод не даёт спец-поведения.
4. Новые строки присутствуют во всех пяти локалях.
5. **[делает разработчик]** E2E на стенде: с ноды 1 найти `@user:node2` → написать → получить ответ; статус и «печатает…» remote-собеседника видны; вложение с ноды 2 скачивается; при остановленной ноде 2 — placeholder «сервер недоступен», приложение не зависает; тумблер приватности на ноде 2 блокирует новый fed-чат с понятной ошибкой у отправителя.
6. Obsidian обновлён (Изменение 8).
7. Коммит: `feat(rearch-phase5): 5.2 — федерация в Android-клиенте`.
