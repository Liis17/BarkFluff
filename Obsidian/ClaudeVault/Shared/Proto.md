# BarkFluff.Proto

Единый проект со всеми `.proto`-контрактами платформы. Только proto-файлы + `.csproj`, без рукописного C#.

Расположение: `Shared/BarkFluff.Proto/`

→ [[Shared/Proto-ProjectMap]] — детальная карта всех файлов и RPC

## Сборка

```bash
dotnet build BarkFluff.Proto.csproj
```

Код генерируется `Grpc.Tools` при сборке. Проект сам по себе не подключал ни один `.proto` (только AWSSDK.Core, вестигиальная зависимость) — сборка ничего не валидировала. С Фазой 0 rearch добавлены `Google.Protobuf`/`Grpc.AspNetCore.Server`/`Grpc.Tools` + `<Protobuf Include="federation_api.proto"/>`/`federation_internal_api.proto` именно для того, чтобы `dotnet build` этого проекта валидировал синтаксис/импорты черновых федеративных контрактов, пока их не подключил ни один реальный сервис.

## Proto Files

| Файл | C# namespace | Назначение |
|------|-------------|------------|
| `shared.proto` | `BarkFluff.Proto.Shared` | `Message`, `MessageContent`, `MessageAttachment`, `MessageAttachmentType`, `PageRequest`, `ChatType`, `EncryptedMessage`, `SecretEnvelope` |
| `identity_api.proto` | `BarkFluff.Proto.Identity` | Auth, 2FA, сессии, сброс/смена пароля; `IdentityServerApi.CreateBotTokenServer` и `GetBotTokenServer` — выпуск bot-JWT для [[Backend/Bots]] |
| `users_api.proto` | `BarkFluff.Proto.Users` | Профили, устройства, бейджи, поиск |
| `messages_api.proto` | `BarkFluff.Proto.Messages` | Чаты, сообщения, вложения |
| `files_api.proto` | `BarkFluff.Proto.Files` | Upload/download URL, стикеры, бейджи |
| `updates_api.proto` | `BarkFluff.Proto.Updates` | Real-time стримы: новые сообщения, прочтения |
| `onliner_api.proto` | `BarkFluff.Proto.Onliner` | Онлайн-статусы |
| `fast_auth_api.proto` | `BarkFluff.Proto.FastAuth` | QR/текстовая авторизация |
| `beacon_api.proto` | `BarkFluff.Proto.Beacon` | Описание сервера и адреса микросервисов |
| `navigator_api.proto` | `BarkFluff.Proto.Navigator` | Глобальный реестр серверов |
| `configuration_api.proto` | `BarkFluff.Proto.Configuration` | Централизованная конфигурация |
| `developers_api.proto` | `BarkFluff.Proto.Developers` | Секции документации, proto-файлы, коды ошибок |
| `calls_api.proto` | `BarkFluff.Proto.Calls` | Звонки (1-на-1 и групповые) поверх LiveKit SFU: инициация, подписка на события, история, качество голоса |
| `bots_api.proto` | `BarkFluff.Proto.Bots` | Bot API: `BotsServerApi` (AdminPanel — создание/список/профиль/аватары через Users+Files/удаление/токены) + `BotsExternalApi` (внешние программы, bot-JWT в `x-auth-token`, политика `TokenType.Bot`) |
| `federation_api.proto` | `BarkFluff.Proto.Federation` | **Фаза 0 rearch — только контракт, RPC не реализованы.** S2S API `FederationS2SApi` (нода↔нода, авторизация — Ed25519-подпись запросов, НЕ XAuth): Ping, GetServerKeys, GetUserProfile, DeliverEvents/FetchChatHistory (события чатов, `FederationEvent` с `origin_signature`/`origin_key_id`), FetchFile (стрим), SubscribePresence/DeliverTyping |
| `federation_internal_api.proto` | `BarkFluff.Proto.FederationInternal` | **Фаза 0 — только контракт.** Внутренний API Federation-сервиса (XAuth, TokenType.Service): `FederationInternalApi` — ResolveRemoteUser, FetchRemoteFile/FetchRemoteChatHistory (мост для Files/Messages), управление пирами для AdminPanel (GetKnownServers/UpsertManualPeer/SetServerBlocked/GetFederationStatus). Импортирует `federation_api.proto` |

## Service Pairs Pattern

Большинство файлов определяют два сервиса:
- `XxxApi` — публичный, клиенты (JWT user token)
- `XxxServerApi` — внутренний, только микросервисы (service token)

Исключения (один сервис): `UpdatesApi`, `OnlinerApi`, `NavigatorApi`, `BeaconApi`, `ConfigurationApi`, `CallsApi`.

## Важные детали

- `shared.proto` импортируется в `messages_api.proto`, `updates_api.proto`, `users_api.proto` — общие типы сюда
- `MessageAttachmentType` enum — в `shared.proto` (не в `messages_api.proto`), т.к. используется и в `files_api.proto`
- `CreateTokenResponse.access_token` имеет **field_number=2** (не 1) — важно при ручной десериализации
- `AuthRequest` и `FindByLoginRequest` используют `oneof login` (username ИЛИ email)
- `SendMessageRequest` использует `oneof source_id` (chat_id ИЛИ user_id)
- `ListMessagesRequest`: поле `count` устарело, использовать `offset_before`/`offset_after`
- `MessageReadEvent.new_read_by` — **полный** список прочитавших, не только новых
- `UploadFileType` — отдельный enum от `MessageAttachmentType`, маппировать при отправке сообщений
- `GetUserByUsernameResponse.profile_poster_url` (field 7) — URL постера профиля (пусто если не задан); заполняется в Users через Files gRPC
- `MessageAttachment.forwarded_message` (field 10) — `ForwardedMessageAttachment`: `author_name`, `original_message_id`, `text`, `repeated attachments` (без FORWARDED_MESSAGE), плюс `original_chat_id` (5), `original_sender_id` (6), `original_sent_at` (7), `order` (8). Заполняется только при `type = FORWARDED_MESSAGE`; поля 5–8 пусты у снапшотов, созданных до разделения reply/forward
- `Message.reply_to` (field 12) — `ReplyInfo`: `message_id`, `sender_id`, `sender_name`, `text_preview` (≤200 символов), `first_attachment_type`, `is_deleted`, `federated_message_id`. Заполнено => сообщение является ответом. **Не снапшот**: сервер резолвит его из живого оригинала при каждой выдаче
- `OutgoingMessage.reply_to_message_id` (field 4) — ответ; только на сообщение того же чата
- `OutgoingMessage.forwarded_message_ids` (field 5) — пересылка до 20 сообщений, порядок сохраняется
- `OutgoingMessage.forwarded_message_id` (field 3) — **DEPRECATED**: до разделения reply/forward им отправлялись оба действия. Оставлено рабочим для необновлённых клиентов (iOS, macOS, ClientV2.WPF, Linux) и трактуется как пересылка. Смешивать с полями 4/5 нельзя → InvalidArgument
- `MessageAttachmentType.FORWARDED_MESSAGE = 8` — пересланное сообщение; исключается из медиа-галереи `ListChatAttachments` при пустом фильтре
- `MessageAttachment.image_width` (field 8) и `image_height` (field 9) — размеры изображения в пикселях; 0 если вложение не является изображением. Используются Android-клиентом для вычисления соотношения сторон ячейки **до** загрузки картинки, чтобы облачко сообщения не меняло размер.
- `UploadFileInfo.image_width` (field 12) и `image_height` (field 13) — аналогичные поля в данных о загруженном файле (в `files_api.proto`).
- `ChatType` enum (`shared.proto`): `CHAT_TYPE_REGULAR=0` (default), `CHAT_TYPE_PRIVATE=1` (E2E через passphrase, шифротекст в БД сервера), `CHAT_TYPE_SECRET=2` (Signal Double Ratchet, **НЕ** хранится на сервере — клиент держит чат в локальном хранилище, не возвращается через `ListChats`). `PrivateChatInviteState` описывает pending/accepted/rejected без раскрытия содержимого.
- `Chat.kdf_salt` (field 10) и `Chat.passphrase_verifier` (field 11) — заполнены только для `CHAT_TYPE_PRIVATE`. Verifier позволяет клиенту проверить корректность passphrase до Accept без передачи на сервер.
- `Chat.last_activity_at` (field 14) и `private_invite_state` (field 15) позволяют сортировать и отображать приватные чаты без plaintext-превью. `CreatePrivateChatResponse.created` отличает новый чат от идемпотентно возвращённого существующего.
- `EncryptedMessage` хранится в отдельной таблице `EncryptedMessages` (планируется), отдельной от `Messages` — серверный код не должен делать join/мержить эти потоки. `EncryptedMessage.id` — отдельная последовательность от `Message.id`.
- `SecretEnvelope` сервер обращается как с opaque blob: только релэит указанному `recipient_device_id` и буферизует в Redis на 24ч (TTL). После `AckSecretMessage` удаляется. В БД секретные сообщения и чаты **не сохраняются** вообще.
- Подписки на секретные события в `updates_api.proto` — **device-scoped** (только устройство, на которое пришло сообщение). Подписки на приватные события — **user-scoped** (все устройства пользователя получают шифротекст; расшифровать сможет только устройство со знанием passphrase).
- `MarkPrivateMessagesAsRead` хранит только высокий watermark шифрованных сообщений. `SubscribePrivateMessagesRead` рассылает этот watermark всем устройствам участников для синхронизации счётчиков непрочитанных.
- Prekey-bundle (X3DH): RPC расположены в `users_api.proto` (`UsersApi`), не в `identity_api.proto`. Bundle принадлежит устройству, `device_id` берётся из JWT текущей сессии.
- Групповые чаты (V1): `AddUser`/`UpdateGroupChat` в `MessagesApi`; `GetChatMemberIds` (для ринга групповых звонков) и `PostCallSystemMessage` (системное сообщение об итоге звонка — `CallSystemResult`: ENDED/MISSED/REJECTED) в `MessagesServerApi`.
- Звонки: `calls_api.proto` описывает `CallsApi` (1-на-1 через `callee_user_id` или групповой через `chat_id`, `oneof target`). `SubscribeCallEvents` — device-scoped стрим, как `SubscribeSecretMessages` в Updates. `beacon_api.proto` содержит `livekit_url` (field 13) и `Service calls` (field 14) в `GetServerInfoResponse`.
- Боты: `User.is_bot` (field 12), `GetUserByUsernameResponse.is_bot` (8) + `id` (9); `UsersServerApi.CreateBotUser`/`DeleteBotUser`/`ListByIds`/`UpdateProfileServer`/профильные изображения; `MessagesServerApi.SendMessageServer(sender_user_id, oneof chat_id/user_id, allow_chat_creation)`; `FilesServerApi.UploadFileServer`/`UploadAvatarServer`/`UploadPosterServer`; `beacon_api.proto` — `Service bots` (field 15). `BotsExternalApi` аутентифицируется штатным XAuth: bot-JWT (`TokenType.Bot`) в заголовке `x-auth-token` (gRPC и HTTP), выпуск — `IdentityServerApi.CreateBotTokenServer` или повторная выдача через `GetBotTokenServer` с тем же `token_id`.
- Отдельный файловый адрес ноды: `beacon_api.proto` `GetServerInfoResponse.files_media_endpoint` (field 18) и `navigator_api.proto` `ServerInfo.files_media_endpoint` (field 14) — absolute origin файлового HTTP мимо CDN ([[Backend/Nginx]] `files2.barkfluff.com`). Пустая строка = адреса нет, клиент качает по ссылкам [[Backend/Files]] как раньше. Копии контракта у Swift- и Android-клиентов (`Mac/Barkfluff/Protos/`, `Android/core/src/main/proto/`) неполные — там нет полей 15-17, но номера совпадают с исходным контрактом.

### Федерация (Фаза 0 rearch) — расширения существующих proto

Только поля/RPC-заглушки (`Unimplemented` до реализации в соответствующей фазе), обратная совместимость не нарушена — только добавления.

- `shared.proto` `Message`: `federated_id` (9, uuid, пусто для локальных), `sender_uuid` (10)
- `users_api.proto`: `User.uuid` (13, из этапа 0.2); `UsersApi.ResolveFederatedUser` (Фаза 2) + `ResolveFederatedUserRequest/Response`; `PrivacySettings.deny_federated_dm` (7, default false = разрешено — именно deny, не allow)
- `messages_api.proto`: `SendMessageRequest.source_id` oneof + `user_uuid` (4); `GetPersonChatIdRequest.user_uuid` (2); `ChatMember.user_uuid` (5) + `server_name` (6); `MessagesServerApi` — 7 новых RPC федеративного импорта/экспорта (Фаза 2: ImportFederatedChat/Message, ApplyFederatedEdit/Delete/Read, ExportChatEvents, CheckFileFederationAccess) с плоскими DTO (`FederatedFileRefFlat`, `FederatedChatEvent`) — файл **не импортирует** `federation_api.proto`, поля продублированы плоско
- `onliner_api.proto`: `user_uuids`/`user_uuid` в Subscribe/Change/Status/Typing-сообщениях; новый сервис `OnlinerServerApi` (Фаза 4) — UpsertRemoteStatus/InjectRemoteTyping
- `navigator_api.proto`: `ServerInfo` +5 полей (server_name, federation_endpoint, signing_keys, tls_spki_sha256, federation_protocol_versions) + `NavigatorSigningKey`; `NavigatorApi.GetServerByName` (Фаза 1)
- `beacon_api.proto`: `GetServerInfoResponse.server_name` (16), `federation_enabled` (17) — **реализовано** (не заглушка): Beacon читает `Federation:ServerName`/`Federation:Enabled` из Configuration, см. [[Backend/Beacon]]
- `users_api.proto`: `PushPlatform` (`ANDROID`, `WEB`) в `SetFirebaseTokenRequest` и `DeviceFirebaseToken`; `UsersApi.ClearFirebaseToken` очищает FCM-привязку текущего устройства. Это позволяет [[Backend/CloudMessaging]] отправлять Android и PWA разные, privacy-safe payload.
- `federation_api.proto`/`federation_internal_api.proto` пока **не подключены** ни в один сервисный `.csproj` (Federation-сервиса ещё нет, Фаза 1) — только в `BarkFluff.Proto.csproj` (см. ниже), чтобы codegen валидировал синтаксис при сборке

## Подключение в .csproj

```xml
<!-- Серверная сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Server" />

<!-- Клиентская сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Client" />

<!-- Только типы -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\shared.proto" GrpcServices="None" />
```
