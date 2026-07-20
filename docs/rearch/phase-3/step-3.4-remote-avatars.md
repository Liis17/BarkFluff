# Этап 3.4 — Аватары remote-пользователей

## Цель

Аватар remote-пользователя отображается у клиентов принимающей ноды; privacy origin-стороны соблюдена: при `AvatarVisibility`, скрывающей аватар для федерации (`None`), файл не отдаётся. Критерий роадмапа: аватар отображается; при `AvatarVisibility=None` — нет.

## Контекст

- Модель: [../06-files.md](../06-files.md), «Аватары remote-пользователей» — аватар публичен с учётом privacy; origin проверяет privacy при `FetchFile` для типа `UserAvatar` (доступ по privacy, **не по членству в чате**).
- Аватары и локально публичны по оригинальному Guid (whitelist типов в `DownloadFileCommandHandler`) — поэтому прямой публичный маршрут `/download/fed/{server}/{fileId}` уместен именно для них (решение фазы, README).
- `RemoteUsers.AvatarFileId` (2.1) — file_id на origin; `ServerName` — в той же строке. S2S `GetUserProfile` (2.1) и `UserProfileChangedPayload` (2.9) уже фильтруют/несут `avatar` по privacy.
- Транспорт: 3.2 (`FetchFile`, `FetchRemoteFile`, `Files.FetchFileStream`, Range). `Files.GetFileData` отдаёт тип файла (`UploadFile.Type`); `UploadFile.Uploaders` — владельцы; у Files уже есть клиент `UsersServerApi`.
- Долг 2.8 («аватар в пуше с origin-ноды — Фаза 3») закрывается здесь (Изменение 4).

## Изменение 1 — Users (origin): `IsAvatarVisibleToFederation`

Новый server-RPC в `UsersServerApi` (proto-добавление): `IsAvatarVisibleToFederation(user_id) → bool`. Правило — **ровно то**, которым `GetFederatedProfile` (2.1) решает, отдавать ли `avatar_file_id` чужой ноде (поле `AvatarVisibility` в Privacy; посмотри фактическую логику 2.1 и переиспользуй, не дублируй). Инвариант: если профиль наружу отдал аватар — `FetchFile` его отдаёт; если скрыл — `FetchFile` откажет даже при утёкшей ссылке.

## Изменение 2 — Files (origin): `CheckFedAvatarAccess`

Новый server-RPC в `FilesServerApi`: `CheckFedAvatarAccess(file_id) → bool`: `UploadFile` найден, `Type == UserAvatar`, владелец (`Uploaders`, первый/создатель — посмотри семантику списка при upload аватара через `SetProfilePicture`/`UploadAvatarServer`) → `Users.IsAvatarVisibleToFederation(owner)` → результат. Файл не найден / не аватар / владельца нет → `false`.

## Изменение 3 — Federation (origin): ветка аватара в `FetchFile`

В обработчике 3.2, **после** блоклиста и rate limit, **до** chat-проверки:

1. `Files.GetFileData(file_id)` → тип.
2. `Type == UserAvatar` → `Files.CheckFedAvatarAccess(file_id)` → `false` → `PermissionDenied`; `true` → стрим.
3. Иначе → существующая ветка `Messages.CheckFileFederationAccess` (3.2).

`GetFileData` на каждый `FetchFile` — приемлемо (один вызов на старт стрима); кеш не вводить.

## Изменение 4 — Files (принимающая сторона): прямой маршрут `/download/fed/{server}/{fileId}`

Новый маршрут в `FilesController` (публичный, как существующий `/download` — без auth):

1. **Open-proxy защита:** `Users.CheckRemoteAvatarRef(server_name, file_id) → bool` — новый server-RPC Users (B-сторона): существует `RemoteUsers` с `(ServerName, AvatarFileId)`. `false` → 404 (ничего не светим; random Guid + произвольная нода не проксируются).
2. `server` == свой `Federation:ServerName` → 404 (свои аватары — обычный `/download`). Blocked/неизвестная нода → отказ всплывёт из `FetchRemoteFile` (3.2) → 404/502 (уточняется картой ошибок в 3.5; здесь — 404, как невалидная ссылка).
3. `FetchRemoteFile(server, file_id, range)` → стрим клиенту, как fed-ветка 3.3 (те же: chunk→Stream адаптер, Range, проброс отмены). `Content-Type` — из первого чанка.
4. **Кап размера** вместо снапшота: конфиг `Files:FedAvatarMaxBytes` (дефолт 20 МБ) — обрыв при превышении (плюс обрыв по declared `total_size` в Federation, 3.2).
5. Кеш-заголовки: сверь с локальным `/download` для аватаров и повтори (смена аватара = новый file_id → URL иммутабелен, кеширование уместно — подтверди по коду `SetProfilePicture`, что аватар всегда новый Guid).

Клиентский контракт (задокументировать в [../06](../06-files.md), реализация — Фаза 5): у remote-профиля есть `server_name` + `avatar_file_id` → клиент строит `https://{своя нода}/web/download/fed/{server}/{fileId}` (префикс `/web` — существующий nginx-rewrite, `Backend/nginx/files.conf`). Старые клиенты строят `/download/{fileId}` → 404 → дефолт-аватар (читаемая деградация).

## Изменение 5 — пуш-аватар (долг 2.8)

- Найди, как формируется URL аватара отправителя в пуше для **локальных** отправителей (CloudMessaging). Если аватара в пушах сейчас нет вовсе — эта часть сводится к нулю, зафиксируй в коммите и не выдумывай новую фичу.
- Если есть: `NewMessageEvent` += `SenderAvatarFileId` (nullable; Queue-событие, только добавление); Messages заполняет **только для remote-отправителя** (`SenderUuid != null` — из `RemoteUsers.AvatarFileId`; локальный путь аватара в CloudMessaging уже существует и не меняется). CloudMessaging: для события с `SenderUuid != null` URL = `/download/fed/{server}/{fileId}` (server — из `RemoteParticipants`/`SenderFid`; базовый хост — как у существующего пути, посмотри, откуда он берёт `ExternalEndpoint`); для локальных — без изменений.

## Чего НЕ делать

- Вложения чатов (temp-модель) — 3.3. Карта ошибок недоступного origin / placeholder — 3.5.
- Кеш аватаров на принимающей ноде — запрещён (README фазы).
- Превью-версии аватаров: если для аватаров используется `PreviewId` — тот же путь `CheckRemoteAvatarRef` не покрывает preview-file-id; **не расширяй** — превью аватара remote-пользователя в MVP не федерируется (клиент тянет полный), зафиксируй ограничение в коммите и Obsidian.
- Клиентский рендер — Фаза 5.

## Критерии готовности

1. Стенд, критерий роадмапа: у пользователя node1 аватар с разрешающей privacy → после резолва/входящего сообщения клиентская выдача на node2 содержит данные для URL; `curl https://node2/web/download/fed/{node1}/{avatarFileId}` отдаёт аватар (байты = оригиналу). Пользователь node1 ставит `AvatarVisibility=None` → тот же URL → отказ (403/404 по принятой карте), S2S `FetchFile` аватара → `PermissionDenied`.
2. Open-proxy негатив: `/download/fed/{node1}/{random Guid}` (нет в `RemoteUsers`) → 404; `/download/fed/{неизвестная нода}/...` → отказ; заблокированная нода → отказ.
3. Смена аватара на node1 → событие профиля (2.9) обновляет `RemoteUsers.AvatarFileId` на node2 → новый URL работает, старый file_id перестаёт проходить `CheckRemoteAvatarRef` (404) — кешей нет, поведение согласованное.
4. Пуш: при наличии аватаров в пушах локальных отправителей — пуш remote-отправителя несёт fed-URL аватара (проверка до точки формирования, как в 2.8).
5. Юнит-тесты: `IsAvatarVisibleToFederation` (таблица по значениям `AvatarVisibility`, согласованность с `GetFederatedProfile`), `CheckFedAvatarAccess`, `CheckRemoteAvatarRef`, ветка аватара в `FetchFile` — зелёные; существующие тесты Users/Files/Federation — без регрессий.
6. Obsidian: `Backend/Users.md` (два новых RPC), `Backend/Files.md` (прямой fed-маршрут, кап), `Backend/Federation.md` (ветка аватара в `FetchFile`), `Backend/CloudMessaging.md` (пуш-аватар, если реализован).
7. Коммит: `feat(rearch-phase3): 3.4 — аватары remote-пользователей с privacy-проверкой на origin`.
