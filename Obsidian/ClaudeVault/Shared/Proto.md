# BarkFluff.Proto

Единый проект со всеми `.proto`-контрактами платформы. Только proto-файлы + `.csproj`, без рукописного C#.

Расположение: `Shared/BarkFluff.Proto/`

→ [[Shared/Proto-ProjectMap]] — детальная карта всех файлов и RPC

## Сборка

```bash
dotnet build BarkFluff.Proto.csproj
```

Код генерируется `Grpc.Tools` при сборке.

## Proto Files

| Файл | C# namespace | Назначение |
|------|-------------|------------|
| `shared.proto` | `BarkFluff.Proto.Shared` | `Message`, `MessageContent`, `MessageAttachment`, `MessageAttachmentType`, `PageRequest`, `ChatType`, `EncryptedMessage`, `SecretEnvelope` |
| `identity_api.proto` | `BarkFluff.Proto.Identity` | Auth, 2FA, сессии, сброс/смена пароля |
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

## Service Pairs Pattern

Большинство файлов определяют два сервиса:
- `XxxApi` — публичный, клиенты (JWT user token)
- `XxxServerApi` — внутренний, только микросервисы (service token)

Исключения (один сервис): `UpdatesApi`, `OnlinerApi`, `NavigatorApi`, `BeaconApi`, `ConfigurationApi`.

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
- `MessageAttachment.forwarded_message` (field 10) — `ForwardedMessageAttachment`: `author_name`, `original_message_id`, `text`, `repeated attachments` (без FORWARDED_MESSAGE). Заполняется только при `type = FORWARDED_MESSAGE`
- `OutgoingMessage.forwarded_message_id` (field 3) — ID пересылаемого сообщения; 0 = не пересылка
- `MessageAttachmentType.FORWARDED_MESSAGE = 8` — пересланное сообщение; исключается из медиа-галереи `ListChatAttachments` при пустом фильтре
- `MessageAttachment.image_width` (field 8) и `image_height` (field 9) — размеры изображения в пикселях; 0 если вложение не является изображением. Используются Android-клиентом для вычисления соотношения сторон ячейки **до** загрузки картинки, чтобы облачко сообщения не меняло размер.
- `UploadFileInfo.image_width` (field 12) и `image_height` (field 13) — аналогичные поля в данных о загруженном файле (в `files_api.proto`).
- `ChatType` enum (`shared.proto`): `CHAT_TYPE_REGULAR=0` (default), `CHAT_TYPE_PRIVATE=1` (E2E через passphrase, шифротекст в БД сервера), `CHAT_TYPE_SECRET=2` (Signal Double Ratchet, **НЕ** хранится на сервере — клиент держит чат в локальном хранилище, не возвращается через `ListChats`).
- `Chat.kdf_salt` (field 10) и `Chat.passphrase_verifier` (field 11) — заполнены только для `CHAT_TYPE_PRIVATE`. Verifier позволяет клиенту проверить корректность passphrase до Accept без передачи на сервер.
- `EncryptedMessage` хранится в отдельной таблице `EncryptedMessages` (планируется), отдельной от `Messages` — серверный код не должен делать join/мержить эти потоки. `EncryptedMessage.id` — отдельная последовательность от `Message.id`.
- `SecretEnvelope` сервер обращается как с opaque blob: только релэит указанному `recipient_device_id` и буферизует в Redis на 24ч (TTL). После `AckSecretMessage` удаляется. В БД секретные сообщения и чаты **не сохраняются** вообще.
- Подписки на секретные события в `updates_api.proto` — **device-scoped** (только устройство, на которое пришло сообщение). Подписки на приватные события — **user-scoped** (все устройства пользователя получают шифротекст; расшифровать сможет только устройство со знанием passphrase).
- Prekey-bundle (X3DH): RPC расположены в `users_api.proto` (`UsersApi`), не в `identity_api.proto`. Bundle принадлежит устройству, `device_id` берётся из JWT текущей сессии.

## Подключение в .csproj

```xml
<!-- Серверная сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Server" />

<!-- Клиентская сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Client" />

<!-- Только типы -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\shared.proto" GrpcServices="None" />
```
