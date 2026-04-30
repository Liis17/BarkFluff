# BarkFluff.Proto

Единый проект со всеми `.proto`-контрактами платформы. Только proto-файлы + `.csproj`, без рукописного C#.

Расположение: `Shared/BarkFluff.Proto/`

## Сборка

```bash
dotnet build BarkFluff.Proto.csproj
```

Код генерируется `Grpc.Tools` при сборке.

## Proto Files

| Файл | C# namespace | Назначение |
|------|-------------|------------|
| `shared.proto` | `BarkFluff.Proto.Shared` | `Message`, `MessageContent`, `MessageAttachment`, `MessageAttachmentType`, `PageRequest` |
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
- `MessageAttachment.image_width` (field 8) и `image_height` (field 9) — размеры изображения в пикселях; 0 если вложение не является изображением. Используются Android-клиентом для вычисления соотношения сторон ячейки **до** загрузки картинки, чтобы облачко сообщения не меняло размер.
- `UploadFileInfo.image_width` (field 12) и `image_height` (field 13) — аналогичные поля в данных о загруженном файле (в `files_api.proto`).

## Подключение в .csproj

```xml
<!-- Серверная сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Server" />

<!-- Клиентская сторона -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Client" />

<!-- Только типы -->
<Protobuf Include="..\..\Shared\BarkFluff.Proto\shared.proto" GrpcServices="None" />
```
