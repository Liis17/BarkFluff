# Этап 5.3 — Web-клиент: федерация в vanilla-JS SPA

## Цель

Веб-мессенджер умеет то же, что и остальные клиенты: резолв `@username:servername`, переписка по uuid, отображение remote-собеседников, их presence/typing, placeholder недоступных вложений, тумблер приватности. Критерий роадмапа: E2E «нашёл → написал → получил ответ».

Клиент: `Backend/BarkFluff.Web/wwwroot` (vanilla JS, gRPC-Web). React-версия в `Frontend/Web` мертва — не трогать.

## Контекст

- Контракт и правила отображения — `Obsidian/ClaudeVault/Клиенты/Federation-ClientGuide.md` (этап 5.1). **Прочитать целиком до начала.**
- Карта клиента — `Obsidian/ClaudeVault/Клиенты/Web.md`.

Точки интеграции (проверено при планировании):

| Что | Файл |
|---|---|
| Обёртки API | `wwwroot/js/app/api.js` (`searchUsers`, `sendMessage`, `getPersonChatId`, `getTempDownloadUrl`, `getPrivacySettings`/`updatePrivacySettings`) |
| gRPC-Web клиенты | `wwwroot/js/app/clients.js` (8 клиентов, `authCall` с рефрешем токена), метаданные — `metadata.js` |
| Поиск/создание чата | `wwwroot/js/app/newchat.js` |
| Список чатов, шапка чата | `wwwroot/js/app/main.js` |
| Сообщения и вложения | `wwwroot/js/app/messages.js`, `files.js` |
| Realtime (статусы/typing) | `wwwroot/js/app/realtime.js` |
| Настройки | `wwwroot/js/app/settings.js` (`renderPrivacy`) |
| Бандл proto | `wwwroot/js/proto/barkfluff.bundle.js`, генерация — `Backend/BarkFluff.Web/scripts/generate-proto.sh` и `generate-proto.ps1` (+ `proto-bundle-index.js`) |
| YARP-маршруты | `Backend/BarkFluff.Web/Program.cs` (`BuildRoutes`, `BuildClusters`) |

## Изменение 1 — доступ к Beacon (бэкенд-правка, оговорённое исключение)

Веб-клиент **не знает имени своей ноды**: Beacon не проксируется и не входит в бандл, `window.location.origin` — единственный адрес. Без `server_name` невозможно отличать remote-участников от локальных.

1. `Program.cs`: добавить маршрут `("beacon", "beacon", "/barkfluff.beacon.BeaconApi/{**catchall}")` и кластер `("beacon", "BeaconService:Host", "http://beacon:7002")` — строго по образцу соседних записей (в том числе трансформ `RequestHeaderRemove: Content-Length`). В список `streamingServices` beacon **не** добавлять (стримов у него нет).
2. `scripts/generate-proto.sh` **и** `scripts/generate-proto.ps1` — добавить `beacon_api.proto` в список генерируемых; `proto-bundle-index.js` — экспорт `BeaconApiClient`. Обе версии скрипта правятся синхронно (разработка ведётся и на macOS, и на Windows).
3. `clients.js`: клиент Beacon + вызов `GetServerInfo` при инициализации мессенджера; результат (`server_name`, `federation_enabled`) положить в состояние приложения.
4. Единый флаг `federationAvailable = federation_enabled && !!server_name` — гейт всех федеративных веток UI.

`GetServerInfo` не требует авторизации — вызывать до/независимо от логина, ошибку трактовать как «федерация недоступна» (не ломать вход в мессенджер).

## Изменение 2 — резолв FID в поиске

`newchat.js` (+ `api.js`):

1. `api.resolveFederatedUser(fid)` поверх `UsersApiClient.resolveFederatedUser`.
2. В обработчике ввода: строка похожа на FID (правила — гайд) и `federationAvailable` → вместо `searchUsers` звать резолв; servername равен своему → обычный поиск.
3. Результат — одна карточка с FID; `found = false`/ошибка → текст из гайда.
4. Выбранный remote-пользователь хранится в `selected` **по uuid**, а не по `userId` (сейчас Map ключуется числовым id — предусмотреть оба вида ключей).
5. Группы: ввод FID в режиме создания группы — запретить с пояснением (сервер вернёт `FederatedGroupsNotSupported`).

## Изменение 3 — переписка по uuid

- `api.sendMessage(opts)` — поддержать `opts.userUuid` (заполняется вместо `opts.userId`).
- `api.getPersonChatId` — поддержать `user_uuid`.
- Обработка ошибок отправки: `FederatedDmRejected`, `ServerNotFound` — тексты из гайда, а не общий «не удалось отправить».

## Изменение 4 — отображение remote-участников

`main.js` (шапка чата, список чатов) и `messages.js` (пузыри):

1. Участник с непустым `server_name` ≠ своего → подпись/бейдж ноды; в шапке чата — полный FID.
2. **HTML-экранирование обязательно**: FID, `username`, имя и `file_name` приходят с чужой ноды. Использовать существующий способ безопасной вставки текста (см. как рендерятся имена сейчас); никакой конкатенации в `innerHTML` без экранирования.
3. Servername выводить как пришёл (punycode), без декодирования в Unicode.
4. `federated_read_by` объединять с `read_by` при отрисовке статуса «прочитано».

## Изменение 5 — presence и typing

`realtime.js`:

1. `subscribeOnlineStatus` — передавать `user_uuids` вместе с `user_ids`; обновление подписки — так же.
2. `UserOnlineStatus` с `user_uuid` матчить по uuid; `UNKNOWN` для remote → «нет данных», не «был(а) давно».
3. `TypingEvent` с `user_uuid` → «печатает…» от remote-участника (имя из участников чата, иначе FID).

## Изменение 6 — вложения недоступного origin

`files.js` / `messages.js`:

1. Карточку/плитку рисовать по метаданным сообщения сразу, до получения ссылки.
2. Ответ `503` при скачивании → placeholder «Сервер собеседника недоступен» + повтор по клику; `404` → «Файл недоступен» без повтора. Существующая логика «протухшая ссылка → перезапросить `getTempDownloadUrl`» не должна зацикливаться на 503/404 (проверить `refreshFileUrl`).
3. `file_name` экранировать при выводе и при подстановке в атрибут `download`.

## Изменение 7 — настройка приватности

`settings.js` (`renderPrivacy`): переключатель «Разрешить сообщения с других серверов» = `!deny_federated_dm`, с подписью про «только новые переписки»; показывать только при `federationAvailable`. Значение отправлять в `updatePrivacySettings` вместе с существующими полями (не затирать их).

## Чего НЕ делать

- Не заводить сборщик/фреймворк/i18n — клиент остаётся vanilla, строки пишутся по-русски как сейчас.
- Не трогать `Frontend/Web` (мёртвая React-версия) и другие клиенты.
- Не менять `Shared/BarkFluff.Proto` и бэкенд, кроме маршрута/кластера Beacon из Изменения 1.
- Не добавлять кеш содержимого федеративных файлов.

## Критерии готовности

1. Бандл перегенерирован (`scripts/generate-proto.sh`; если Node/protoc недоступны в окружении — зафиксировать это в отчёте и коммите, не оставляя бандл несогласованным молча), `BeaconApiClient` доступен в `window.barkfluff`.
2. `dotnet build Backend/BarkFluff.Web/BarkFluff.Web.csproj` — успех; маршрут и кластер Beacon добавлены по образцу (проверить, что остальные маршруты не задеты).
3. Синтаксическая проверка изменённых JS-модулей (`node --check` по каждому файлу) — без ошибок.
4. При `federation_enabled = false` веб-клиент ведёт себя ровно как до этапа: поиск, чаты, вложения, настройки без федеративных элементов.
5. Экранирование: строки с чужой ноды (FID, имя, `file_name`) нигде не попадают в `innerHTML` без экранирования — проверить точечно по диффу.
6. **[делает разработчик]** E2E на стенде: найти `@user:node2` → написать → получить ответ; бейдж ноды и FID в шапке; статус/typing remote-собеседника; скачивание вложения с ноды 2; остановленная нода 2 → placeholder «сервер недоступен»; тумблер приватности блокирует новый fed-чат.
7. Obsidian: `Клиенты/Web.md` — раздел «Федерация» + ссылка на `[[Клиенты/Federation-ClientGuide]]`; `Backend/Web.md` — упоминание нового маршрута Beacon в YARP.
8. Коммит: `feat(rearch-phase5): 5.3 — федерация в веб-клиенте`.
