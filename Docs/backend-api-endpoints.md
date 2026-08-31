# BarkFluff — Справочник API эндпоинтов backend-микросервисов

Свод всех эндпоинтов backend-сервисов (`Backend/*`) с описанием входных и выходных данных.

**Источники:** `.proto`-контракты `Shared/BarkFluff.Proto/*.proto` (для gRPC-сервисов) и REST/HTTP-контроллеры сервисов без gRPC (AdminPanel, WebServer, ClientStorage). Актуально на дату составления — см. `git log` по `Shared/BarkFluff.Proto/`.

## Как читать таблицы

- Тип `int64`/`int32`/`string`/`bool`/`bytes` — стандартные protobuf scalar-типы.
- `T[]` — `repeated T` (список).
- `T?` — `optional T` (поле может отсутствовать).
- `ts` — `google.protobuf.Timestamp`.
- `oneof(a, b)` — ровно одно из полей `a`/`b` должно быть заполнено.
- `stream T` в столбце «Выход» — server-streaming RPC (соединение живёт, сервер пишет `T` по мере событий).
- Часто повторяющиеся составные типы (`Message`, `Chat`, `User` и т.д.) описаны один раз в разделе [Общие типы](#общие-типы) и далее только упоминаются по имени.

## Аутентификация (XAuth)

Все gRPC-сервисы (кроме анонимных методов, отмеченных явно) используют единый механизм `XAuth` (`BarkFluff.GrpcServer`):

- JWT передаётся в заголовке `x-auth-token` (не стандартный `Authorization`).
- Обязательные device-заголовки (Base64): `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Политики авторизации на каждом методе:
  - **`User`** — пользовательский или сервисный (`Service`) JWT.
  - **`Service`** — только межсервисный JWT (вызовы backend→backend).
  - **`Bot`** — только долгоживущий bot-JWT ([[Backend/Bots]] External API).
- Межсерверная федерация (`FederationS2SApi`) использует **не XAuth**, а Ed25519-подпись запроса (заголовки `x-bf-origin/-destination/-timestamp/-key-id/-signature`), см. раздел [Federation](#6-федерация).

Если в заголовке группы не указано иное — все RPC группы защищены политикой `User`.

---

## Оглавление

1. [Общие типы](#общие-типы)
2. [Инфраструктура и discovery](#1-инфраструктура-и-discovery) — Settings, Navigator, Beacon
3. [Идентификация и устройства](#2-идентификация-и-устройства) — Identity, FastAuth, Users
4. [Сообщения и коммуникация](#3-сообщения-и-коммуникация) — Messages, Updates, Onliner, Files
5. [Звонки](#4-звонки) — Calls
6. [Боты](#5-боты) — Bots
7. [Федерация](#6-федерация) — Federation
8. [Уведомления](#7-уведомления) — Notification, CloudMessaging
9. [Порталы и вспомогательные HTTP-сервисы](#8-порталы-и-вспомогательные-http-сервисы) — Developers, AdminPanel, WebServer, ClientStorage, Web

---

## Общие типы

Определены в `shared.proto` (`barkfluff.shared`), используются в Messages/Updates/Files и др.

| Тип | Поля |
|---|---|
| `PageRequest` | `offset: int32`, `size: int32` |
| `Message` | `id: int64`, `sender_id: int64`, `read_by: int64[]`, `sent_at: ts`, `content: MessageContent`, `type: MessageContentType`, `is_edited: bool`, `edited_at: ts`, `federated_id: string` (uuid, для федеративных сообщений), `sender_uuid: string`, `federated_read_by: string[]`, `reply_to: ReplyInfo?` |
| `ReplyInfo` | `message_id: int64`, `sender_id: int64`, `sender_name: string`, `text_preview: string` (≤200 симв.), `first_attachment_type: MessageAttachmentType`, `is_deleted: bool`, `federated_message_id: string` |
| `MessageContent` | `text: string`, `attachments: MessageAttachment[]` |
| `MessageAttachment` | `id: int64`, `type: MessageAttachmentType`, `file_id: string`, `preview_url: string`, `attachment_size: int64`, `preview_file_id: string`, `file_name: string`, `origin_server: string` (нода-владелец для federated), `image_width/height: int32`, `forwarded_message: ForwardedMessageAttachment?` |
| `ForwardedMessageAttachment` | `author_name, text: string`, `original_message_id: int64`, `attachments: MessageAttachment[]`, `original_chat_id: string`, `original_sender_id: int64`, `original_sent_at: ts`, `order: int32` |
| `MessageAttachmentType` (enum) | `UNKNOWN, IMAGE, VIDEO, GIF, DOCUMENT, AUDIO, VOICE, STICKER, FORWARDED_MESSAGE` |
| `MessageContentType` (enum) | `UNKNOWN, GENERIC, SYSTEM` |
| `PinnedMessageInfo` | `message: Message`, `pinner_user_id: int64`, `pinned_at: ts` |
| `ChatType` (enum) | `REGULAR` (обычный), `PRIVATE` (E2E passphrase), `SECRET` (E2E Double Ratchet, не хранится на сервере) |
| `PrivateChatInviteState` (enum) | `PENDING, ACCEPTED, REJECTED` |
| `EncryptedMessage` | `id: int64`, `chat_id: string`, `sender_id: int64`, `sender_device_id: string`, `sent_at: ts`, `ciphertext/nonce/associated_data: bytes`, `is_edited: bool`, `edited_at: ts`, `is_deleted: bool` |
| `SecretEnvelope` | `message_id: string`, `sender_user_id: int64`, `sender_device_id: string`, `recipient_device_id: string`, `envelope: bytes` (opaque libsignal), `sent_at: ts` |

---

## 1. Инфраструктура и discovery

### 1.1 Settings (порт 7003; wire-имя `ConfigurationApi`)

Централизованные настройки всех сервисов хранятся в `BarkFluff.Settings`. Для
совместимости старых клиентов proto, package, RPC и типы по-прежнему называются
`configuration_api.proto`/`ConfigurationApi`; runtime endpoint обслуживает Settings.
Для нового deployment используйте `SETTINGS_SERVICE_URL`.

| RPC | Вход | Выход |
|---|---|---|
| `GetConfiguration` | `service_id: int32` | `configurations: ConfigurationItem[]` |
| `GetAllConfigurations` | — | `configurations: ConfigurationItem[]` (для админ-панели) |
| `UpdateConfiguration` | `section, key, value: string`, `service_id: int32`, `edited_by, edited_from: string` | `success: bool`, `message: string` |
| `GetReservedNames` | — | `names: string[]` |
| `AddReservedName` | `name: string` | `success: bool`, `message: string` |
| `UpdateReservedName` | `old_name, new_name: string` | `success: bool`, `message: string` |
| `DeleteReservedName` | `name: string` | `success: bool`, `message: string` |

`ConfigurationItem`: `section, key, value: string`, `edited_at: ts`, `edited_by, edited_from: string`, `service_id: int32`.

### 1.2 Navigator (порт 7010)

Глобальный каталог серверов BarkFluff (публичный, без XAuth). Proto: `navigator_api.proto` → **`NavigatorApi`**.

| RPC | Вход | Выход |
|---|---|---|
| `ListServers` | — | `servers: ServerInfo[]` |
| `RegisterServer` | `server: ServerInfo` | *(пусто)* |
| `GetServerByName` | `server_name: string` | `found: bool`, `server: ServerInfo` |

`ServerInfo`: `name, description, server_public_name, location: string`, `accounts_count: int64`, `beacon_uri: ServiceEndpoint{host,port}`, `color: ServerColor{lite_hex,main_hex,hard_hex}`, `server_name: string` (DNS-домен), `federation_endpoint: string`, `signing_keys: NavigatorSigningKey[]`, `tls_spki_sha256: string[]`, `federation_protocol_versions: int32[]`, `web_endpoint, files_media_endpoint: string`.

### 1.3 Beacon (порт 7002)

Точка входа для клиентов — единая карта адресов всех сервисов ноды (публичный, без XAuth). Proto: `beacon_api.proto` → **`BeaconApi`**.

| RPC | Вход | Выход |
|---|---|---|
| `GetServerInfo` | — | `GetServerInfoResponse` |

`GetServerInfoResponse`: `name, description, public_name, location: string`, `color: ServerColor`, `identity/users/files/messages/updates/onliner/fast_auth/calls/bots: Service` (каждый — `name: string`, `endpoint: ServiceEndpoint`, `tls_enabled: bool`, `status: ServiceStatus{Unknown,Healthy,Degraded,Unhealthy,Offline}`), `livekit_url: string`, `server_name: string`, `federation_enabled: bool`, `files_media_endpoint: string`.

---

## 2. Идентификация и устройства

### 2.1 Identity (порт 7000)

Авторизация, JWT, 2FA, сброс пароля, сессии. Proto: `identity_api.proto`.

#### `IdentityApi` (клиентский, `User`/анонимный где указано)

| RPC | Вход | Выход |
|---|---|---|
| `Auth` (анон.) | `oneof(username, email): string`, `password: string`, `otp_code: string?` | `access_token, refresh_token: Token` |
| `FastAuth` (анон.) | `fast_auth_id: string` | `AuthResponse` (как выше) |
| `CreateToken` (анон.) | `refresh_token: string` | `access_token: Token` |
| `CreateAccount` (анон.) | `first_name, last_name, username, email: string` | `code_id: string` |
| `ConfirmAccount` (анон.) | `code_id, code_value: string` | `refresh_token: Token` |
| `GetActiveSessions` | — | `sessions: Session[]` (`id: int64`, `created_at/expiration_at: ts`, `device_id, original_name, custom_name, app_name, operation_system, location: string`) |
| `RemoveActiveSession` | `device_id: string` | *(пусто)* |
| `EnableOtpVerification` | `otp_type: OtpTypeId{Authenticator,Email}` | `otp_qr: string` (base64 QR), `otp_code: string` |
| `ConfirmOtpVerification` | `otp_code: string` | *(пусто)* |
| `DisableOtpVerification` | `otp_type: OtpTypeId`, `otp_code: string?` | *(пусто)* |
| `ListOtpVerification` | — | `authenticator_enabled, email_enabled: bool` |
| `ResetPassword` (анон.) | `oneof(username, email): string`, `otp_type: OtpTypeId` | `reset_id: string` |
| `ConfirmResetPassword` (анон.) | `reset_id, otp_code: string` | `access_token, refresh_token: Token` |
| `SetPassword` | `password: string`, `old_password: string?` (обязателен, если пароль уже задан) | *(пусто)* |
| `Logout` | — | *(пусто)* |

`Token`: `value: string`, `expiration_date: ts`.

#### `IdentityServerApi` (`Service`, для AdminPanel/FastAuth/Bots)

| RPC | Вход | Выход |
|---|---|---|
| `ListOtpVerificationServer` | `user_id: int64` | `authenticator_enabled, email_enabled: bool` |
| `DisableOtpVerificationServer` | `user_id: int64`, `otp_type: OtpTypeId` | *(пусто)* |
| `GetActiveSessionsServer` | `user_id: int64` | `sessions: Session[]` |
| `RemoveActiveSessionServer` | `user_id: int64`, `device_id: string` | *(пусто)* |
| `CreateSessionForUserServer` | `user_id: int64`, `device_id, device_name, operation_system, app_name, ip_address: string` | `access_token, refresh_token: Token` |
| `ForceSetPasswordServer` | `user_id: int64`, `new_password: string` | *(пусто)* |
| `CreateBotTokenServer` | `bot_user_id: int64` | `token: string` (bot-JWT), `token_id: string` |

### 2.2 FastAuth (порт 7008)

QR-авторизация устройств. Proto: `fast_auth_api.proto`.

#### `FastAuthApi` (клиентский)

| RPC | Вход | Выход | Auth |
|---|---|---|---|
| `GenerateFastAuthToken` | `format: TokenFormat{QR,TEXT}` | `token: FastAuthToken{format,value}`, `fast_auth_id: string`, `expires_at: ts` | анонимно |
| `SubscribeFastAuthResult` | `fast_auth_id: string` | `stream FastAuthResult{status: FastAuthStatus, access_token?, access_token_expires_at?, refresh_token?, refresh_token_expires_at?}` | анонимно |
| `ScanFastAuth` | `fast_auth_id: string` | `device_name, operation_system, app_name, app_version, ip_address, confirmation_code: string`, `expires_at: ts` | User |
| `AcceptFastAuth` | `fast_auth_id, confirmation_code: string` | *(пусто)* | User |
| `RejectFastAuth` | `fast_auth_id, confirmation_code: string` | *(пусто)* | User |

`FastAuthStatus`: `PENDING, SCANNED, ACCEPTED, REJECTED, EXPIRED`.

#### `FastAuthServerApi` (`Service`)

| RPC | Вход | Выход |
|---|---|---|
| `GetFastAuthInfo` | `fast_auth_id: string` | `status: FastAuthStatus`, `expires_at: ts`, `user_id: int64` (0 = ещё не отсканировано) |

### 2.3 Users (порт 7001)

Профили, устройства, бейджи, приватность, папки чатов, prekeys для секретных чатов. Proto: `users_api.proto` (самый крупный контракт — 1168 строк).

#### `UsersApi` (клиентский)

**Профиль:**

| RPC | Вход | Выход |
|---|---|---|
| `GetUser` | `user_id: int64` | `user: User` |
| `SetProfilePicture` | `file_id: string` | *(пусто)* |
| `CheckExistUsername` / `CheckExistEmail` | `username`/`email: string` | `exist: bool` |
| `ChangeName` | `first_name, last_name: string` | *(пусто)* |
| `ChangeUsername` | `username: string` | *(пусто)* |
| `ChangeBio` | `bio: string` | *(пусто)* |
| `SearchUsers` | `query: string`, `pagination: PageRequest` | `users: User[]`, `total_count: int32` |
| `ResolveFederatedUser` | `fid: string` (`@user:server`) | `found: bool`, `uuid, username, server_name, first_name, last_name, bio, avatar_url: string` |
| `GetUserBadges` | `user_id: int64`, `limit: int32?` | `badges: UserBadge[]` |

`User`: `id: int64`, `first_name, last_name, username: string`, `registration_date: ts`, `profile_picture, profile_picture_preview: string`, `bio: string`, `badges: UserBadge[]`, `storage_limit_gb: int32`, `profile_poster_file_id: string`, `is_bot: bool`, `uuid: string`.
`UserBadge`: `badge: Badge{id,name,description,image_url,created_date,is_active}`, `priority: int32`, `assigned_date: ts`.

**Устройства:**

| RPC | Вход | Выход |
|---|---|---|
| `GetDevices` | — | `devices: Device[]` |
| `GetCurrentDevice` | — | `device: Device` |
| `RenameDevice` | `device_id, custom_name: string` | *(пусто)* |
| `SetFirebaseToken` | `firebase_token: string`, `push_platform: PushPlatform{ANDROID,WEB}` | *(пусто)* |
| `ClearFirebaseToken` | — | *(пусто)* |
| `SetNotificationsEnabled` | `enabled: bool` | *(пусто)* |

`Device`: `device_id: string`, `user_id: int64`, `original_name, custom_name: string`, `authorized_at: ts`, `app_name, operation_system, location: string`, `notifications_enabled: bool`.

**Чаты — mute, приватность, персонализация:**

| RPC | Вход | Выход |
|---|---|---|
| `SetChatMuted` | `chat_id: string`, `muted: bool`, `muted_until: ts?` | *(пусто)* |
| `GetMutedChats` | — | `chats: MutedChat[]{chat_id, muted_until}` |
| `GetPrivacySettings` | — | `settings: PrivacySettings` |
| `UpdatePrivacySettings` | `settings: PrivacySettings` | *(пусто)* |
| `AcceptLegalConsent` | `revision: string` | *(пусто)* |
| `GetPersonalization` / `UpdatePersonalization` | — / `personalization: UserPersonalizationData{profile_poster_file_id, chat_background_file_ids[]}` | `personalization: UserPersonalizationData` |
| `GetUserSettings` | — | `settings: UserSettingsData{global_chat_background_file_id, chat_backgrounds: ChatBackgroundOverride[]}` |
| `SetGlobalChatBackground` | `chat_background_file_id: string` | *(пусто)* |
| `SetChatBackground` | `chat_id, chat_background_file_id: string` | *(пусто)* |
| `GetProfilePoster` / `SetProfilePoster` | — / `profile_poster_file_id: string` | `profile_poster_file_id: string` |

`PrivacySettings`: `profile_visible_on_site: bool`, `avatar_visibility/bio_visibility/email_visibility/online_visibility: ProfileFieldVisibility{ALL,FRIENDS,NONE}`, `search_visible: bool`, `deny_federated_dm: bool`.

**Папки чатов:**

| RPC | Вход | Выход |
|---|---|---|
| `GetChatFolders` | — | `folders: ChatFolderData[]` |
| `CreateChatFolder` | `folder_name: string`, `folder_icon: string?` | `folder: ChatFolderData` |
| `UpdateChatFolder` | `folder_id: string`, `folder_name?, folder_icon?: string`, `has_chat_list_update: bool`, `chat_list: string[]` | `folder: ChatFolderData` |
| `DeleteChatFolder` | `folder_id: string` | *(пусто)* |
| `AddChatToFolder` / `RemoveChatFromFolder` | `folder_id, chat_id: string` | `folder: ChatFolderData` |
| `ReorderChatFolders` | `orders: ChatFolderOrder[]{folder_id, sort_order}` | *(пусто)* |

`ChatFolderData`: `folder_id, folder_name, folder_icon: string`, `chat_list: string[]`, `sort_order: int32`.

**Prekey bundle (X3DH, для секретных чатов):**

| RPC | Вход | Выход |
|---|---|---|
| `RegisterPrekeyBundle` | `registration_id: uint32`, `identity_pubkey: bytes`, `signed_prekey: SignedPreKey`, `one_time_prekeys: OneTimePreKey[]` | *(пусто)* |
| `FetchPrekeyBundle` | `user_id: int64`, `device_id: string` | `bundle: PrekeyBundle`, `remaining_one_time_prekeys: int32` |
| `ListPeerDevices` | `user_id: int64` | `devices: PeerDeviceInfo[]{device_id, display_name, has_bundle, last_seen_at}` |
| `ReplenishOneTimePrekeys` | `prekeys: OneTimePreKey[]` | `total_one_time_prekeys: int32` |
| `RotateSignedPrekey` | `signed_prekey: SignedPreKey` | *(пусто)* |

`SignedPreKey`: `prekey_id: uint32`, `public_key: bytes` (X25519, 32B), `signature: bytes` (Ed25519, 64B). `OneTimePreKey`: `prekey_id: uint32`, `public_key: bytes`. `PrekeyBundle`: `device_id: string`, `registration_id: uint32`, `identity_pubkey: bytes`, `signed_prekey: SignedPreKey`, `one_time_prekey: OneTimePreKey?`, `has_one_time_prekey: bool`.

#### `UsersServerApi` (`Service`, межсервисный — Identity/Messages/Files/Bots/AdminPanel/Federation/Onliner)

| RPC | Вход | Выход |
|---|---|---|
| `FindByLogin` | `oneof(username, email): string` | `user: User` |
| `CheckExistUsername` / `CheckExistEmail` | как в клиентском API | `exist: bool` |
| `AddDraftUser` / `OverrideDraftUser` | `first_name, last_name, username, email: string` | `user_id: int64` |
| `ConfirmUser` | `user_id: int64` | *(пусто)* |
| `GetById` | `user_id: int64` | `user: User` |
| `GetUserContacts` | `user_id: int64` | `user: User`, `contact: UserContact{email}` |
| `ListByIds` | `ids: int64[]` | `users: User[]` |
| `AssignUserBadge` / `RemoveUserBadge` / `UpdateUserBadgePriority` | `user_id, badge_id: int64/int32`, `priority?` | `user_badge: UserBadge` / `success: bool` |
| `CreateBadge` / `UpdateBadge` / `DeleteBadge` / `GetAllBadges` | `name, description, image_url` / `id, is_active` / `id` / `include_inactive: bool` | `badge: Badge` / `success: bool` / `badges: Badge[]` |
| `ExportData` | `user_id: int64` | `files: JsonFile[]{filename, content}` (GDPR-экспорт) |
| `RegisterDevice` | `device_id, user_id, original_name, app_name, operation_system, location` | `device: Device` |
| `GetUserDevices` | `user_id: int64` | `devices: Device[]` |
| `DeleteUserDevice` | `device_id: string`, `user_id: int64` | *(пусто)* |
| `UpdateDeviceAppInfo` | `device_id, user_id, original_name, app_name` | `updated: bool` |
| `GetUserByUsername` | `username: string` | `found: bool`, `first_name, last_name, username, bio, profile_picture, profile_poster_url: string`, `is_bot: bool`, `id: int64` |
| `SearchUsersServer` | `query: string`, `offset, size: int32` | `users: User[]`, `total_count: int32` |
| `UpdateStorageLimit` | `user_id: int64`, `storage_limit_gb: int32` (1–250) | `user: User` |
| `SetProfilePictureServer` | `user_id: int64`, `profile_picture_url, profile_picture_preview_url: string` | *(пусто)* |
| `GetDevicesWithFirebaseTokens` | `user_ids: int64[]`, `chat_id: string?` (исключить мьютнутых) | `tokens: DeviceFirebaseToken[]` |
| `GetDevicesWithFirebaseTokensByDeviceIds` | `device_ids: string[]` | `tokens: DeviceFirebaseToken[]` |
| `GetAllDevicesWithFirebaseTokens` | — | `tokens: DeviceFirebaseToken[]` |
| `GetUserPrivacy` | `user_id: int64` | `settings: PrivacySettings` |
| `GetMutedChatIds` | `user_id: int64`, `chat_ids: string[]` | `muted_chat_ids: string[]` |
| `UpdateProfileServer` | `user_id: int64`, `first_name, last_name, bio, username: string` | *(пусто)* |
| `SetProfilePosterServer` / `GetProfilePosterServer` | `user_id, poster_file_id?` / `user_id` | *(пусто)* / `poster_url: string` |
| `CreateBotUser` | `username, first_name: string`, `bypass_username_rules: bool` | `user_id: int64`, `already_existed: bool` |
| `DeleteBotUser` | `user_id: int64` | *(пусто)* |
| `UpsertRemoteUsers` | `records: UpsertRemoteUserInfo[]` (федерация) | `results: UpsertRemoteUserResult[]{uuid, ok, reject_reason}` |
| `GetUsersByUuid` | `uuids: string[]` | `users: UserProfileByUuid[]` (локальные+remote вперемешку) |
| `GetFederatedProfile` | `oneof(username, uuid): string` | `found: bool`, `uuid, username, first_name, last_name, bio, avatar_file_id: string` (privacy-фильтровано) |
| `IsAvatarVisibleToFederation` | `user_id: int64` | `visible: bool` |
| `CheckRemoteAvatarRef` | `server_name, file_id: string` | `exists: bool` |

---

## 3. Сообщения и коммуникация

### 3.1 Messages (порт 7007)

Чаты, сообщения, вложения, приватные/секретные чаты. Proto: `messages_api.proto` (916 строк, самый функционально насыщенный сервис).

#### `MessagesApi` — обычные чаты

| RPC | Вход | Выход |
|---|---|---|
| `ListChats` | `pagination: PageRequest` (≤50) | `chats: Chat[]`, `total_count: int32` |
| `ListMessages` | `chat_id: string`, `from_message_id: int64`, `offset_before/offset_after: int32` (≤50 каждый) | `messages: Message[]` |
| `ListChatMembers` | `chat_id: string`, `pagination: PageRequest` | `chat_members: {general_info: ChatMember, first_name, last_name}[]`, `total_count: int32` |
| `SendMessage` | `oneof(chat_id, user_id, user_uuid)`, `message: OutgoingMessage` | `message: Message` |
| `CreateGroupChat` | `user_ids: int64[]`, `title, picture_file_id: string` | `created_chat: Chat` |
| `KickUser` / `AddUser` | `chat_id: string`, `user_id: int64` | *(пусто)* |
| `UpdateGroupChat` | `chat_id: string`, `title?, picture_file_id?: string` | `chat: Chat` |
| `MarkAsRead` | `message_ids: int64[]` | *(пусто)* |
| `ListChatAttachments` | `chat_id: string`, `pagination`, `attachment_type: MessageAttachmentType`, `sort_descending: bool`, `file_name_query: string?` | `attachments: ChatAttachmentInfo[]`, `total_count: int32` |
| `GetPersonChatId` | `oneof(user_id, user_uuid)` | `chat_id: string` |
| `GetChatInfo` | `chat_id: string` | `last_message_id, first_unread_message_id: int64`, `title, picture: string`, `is_group_chat: bool`, `count_unread: int64`, `members_id: int64[]`, `muted: bool`, `muted_until: ts` |
| `GetChatDraft` / `UpsertChatDraft` / `DeleteChatDraft` | `chat_id` / `chat_id, text, reply_to_message_id` / `chat_id, expected_revision` | `draft: ChatDraftInfo?` / `draft: ChatDraftInfo` / `deleted: bool` |
| `EditMessage` | `message_id: int64`, `text: string`, `files_ids: string[]` | `message: Message` |
| `DeleteMessage` | `message_id: int64` | *(пусто)* |
| `PinMessage` / `UnpinMessage` | `chat_id: string`, `message_id: int64` | `pinned: PinnedMessageInfo` / *(пусто)* |
| `ListPinnedMessages` | `chat_id: string`, `pagination` | `pinned: PinnedMessageInfo[]`, `total_count: int32` |
| `UnpinAll` | `chat_id: string` | `unpinned_count: int32` |

`Chat`: `id, title, picture: string`, `is_group_chat: bool`, `last_message: Message`, `members: ChatMember[]` (только для ЛС), `count_unread, first_unread_message_id: int64`, `chat_type: ChatType`, `kdf_salt, passphrase_verifier: bytes` (только PRIVATE), `muted: bool`, `muted_until: ts`, `last_activity_at: ts`, `private_invite_state: PrivateChatInviteState`, `private_inviter_user_id: int64`, `has_draft: bool`.
`OutgoingMessage`: `text: string`, `files_ids: string[]`, `reply_to_message_id: int64` (0=нет), `forwarded_message_ids: int64[]` (≤20).

#### `MessagesApi` — приватные чаты (E2E passphrase, только 1-к-1)

| RPC | Вход | Выход |
|---|---|---|
| `CreatePrivateChat` | `peer_user_id: int64`, `kdf_salt, passphrase_verifier: bytes` | `chat: Chat`, `created: bool` |
| `AcceptPrivateChat` / `RejectPrivateChat` | `chat_id: string` | `chat: Chat` / *(пусто)* |
| `SendPrivateMessage` | `chat_id: string`, `ciphertext, nonce, associated_data: bytes` | `message: EncryptedMessage` |
| `ListPrivateMessages` | `chat_id: string`, `from_message_id: int64`, `offset_before/after: int32` | `messages: EncryptedMessage[]` |
| `EditPrivateMessage` | `message_id: int64`, `ciphertext, nonce, associated_data: bytes` | `message: EncryptedMessage` |
| `DeletePrivateMessage` | `message_id: int64` | *(пусто)* |
| `MarkPrivateMessagesAsRead` | `chat_id: string`, `last_read_message_id: int64` | *(пусто)* |

#### `MessagesApi` — секретные чаты (Signal Double Ratchet, сервер не хранит содержимое)

| RPC | Вход | Выход |
|---|---|---|
| `SendSecretChatInvite` | `recipient_user_id: int64`, `recipient_device_id: string`, `initial_envelope: bytes` (PreKeySignalMessage) | `invite_id: string`, `invite_expires_at: ts` (24ч) |
| `AcceptSecretChatInvite` | `invite_id: string`, `response_envelope: bytes?` | *(пусто)* |
| `RejectSecretChatInvite` | `invite_id: string` | *(пусто)* |
| `SendSecretMessage` | `recipient_user_id: int64`, `recipient_device_id: string`, `envelope: bytes` | `message_id: string`, `expires_at: ts` |
| `AckSecretMessage` | `message_id: string` | *(пусто)* |

#### `MessagesServerApi` (`Service` — межсервисный: Bots, Calls, AdminPanel, Federation)

| RPC | Вход | Выход |
|---|---|---|
| `GetUserAllMessages` | `user_id: int64` | `messages: ExportMessage[]`, `chats: ExportChat[]` (GDPR-экспорт) |
| `CheckChatMembership` | `user_id: int64`/`user_uuid: string`, `chat_ids: string[]` | `member_chat_ids: string[]`, `federated_chats: FederatedChatContext[]`, `requester_uuid: string` |
| `GetChatMemberIds` | `chat_id: string` | `user_ids: int64[]` |
| `GetChatInfoServer` | `chat_id: string` | `found: bool`, `title, picture: string`, `is_group_chat: bool`, `member_ids: int64[]` |
| `PostCallSystemMessage` | `oneof(chat_id, person: PersonCallTarget)`, `sender_user_id: int64`, `result: CallSystemResult`, `duration_seconds: int64` | `posted: bool` |
| `SendMessageServer` | `sender_user_id: int64`, `oneof(chat_id, user_id)`, `message: OutgoingMessage`, `allow_chat_creation: bool` | `message: Message` |
| `EditMessageServer` | `sender_user_id, message_id: int64`, `text: string`, `files_ids: string[]` | `message: Message` |
| `DeleteMessageServer` | `sender_user_id, message_id: int64` | *(пусто)* |
| `ImportFederatedChat` | `chat_id, initiator_uuid/username/server_name, invitee_uuid: string`, `origin_ts_ms: int64` | `imported: bool` |
| `ImportFederatedMessage` | `chat_id, federated_message_id, sender_uuid/username/server_name, text: string`, `attachments[]`, `origin_ts_ms, raw_event, event_id, reply_to_federated_message_id, forwards[]` | `message_id: int64` |
| `ApplyFederatedEdit` / `ApplyFederatedDelete` / `ApplyFederatedRead` | см. proto (chat_id, federated_message_id, origin_ts_ms, origin_server, event_id, ...) | `applied: bool` |
| `ExportChatEvents` | `chat_id: string`, `since_ts_ms: int64`, `limit: int32` | `events: FederatedChatEvent[]`, `has_more: bool` |
| `CheckFileFederationAccess` | `file_id, requesting_server: string` | `allowed: bool` |
| `CheckFederatedPresenceAccess` | `requesting_server: string`, `user_uuids: string[]` | `allowed_user_uuids: string[]` |
| `CheckFedFileUserAccess` | `user_id: int64`, `origin_server, file_id: string` | `allowed: bool`, `file_name: string`, `size_bytes: int64`, `attachment_type: int32` |

### 3.2 Updates (порт 7015)

Real-time доставка событий клиентам через server-streaming gRPC (только подписки, без унарных методов записи). Proto: `updates_api.proto` → **`UpdatesApi`**.

| RPC (все `stream`) | Вход | Выход события |
|---|---|---|
| `SubscribeNewMessages` | — | `NewMessageEvent{message: Message, chat_id}` |
| `SubscribeMessagesRead` | — | `MessageReadEvent{chat_id, message_id, new_read_by: int64[]}` (полный список прочитавших) |
| `SubscribeMessagesEdited` | — | `MessageEditedEvent{chat_id, message: Message}` |
| `SubscribeMessagesDeleted` | — | `MessageDeletedEvent{chat_id, message_id}` |
| `SubscribeMessagesPinned` / `Unpinned` / `AllMessagesUnpinned` | — | `MessagePinnedEvent{chat_id, message_id, pinner_user_id, pinned_at}` / `MessageUnpinnedEvent{chat_id, message_id}` / `AllMessagesUnpinnedEvent{chat_id}` |
| `SubscribePrivateMessages` | — | `NewEncryptedMessageEvent{chat_id, message: EncryptedMessage}` |
| `SubscribePrivateMessageEdits` / `Deletes` | — | `EncryptedMessageEditedEvent` / `EncryptedMessageDeletedEvent{chat_id, message_id}` |
| `SubscribePrivateMessagesRead` | — | `PrivateMessagesReadEvent{chat_id, user_id, last_read_message_id}` |
| `SubscribePrivateChatInvites` | — | `PrivateChatInviteEvent{chat_id, inviter_user_id, kdf_salt, passphrase_verifier, invited_at}` |
| `SubscribePrivateChatInviteResolutions` | — | `PrivateChatInviteResolutionEvent{chat_id, invitee_user_id, accepted}` |
| `SubscribeSecretChatInvites` (device-scope) | — | `SecretChatInviteEvent{invite_id, sender_user_id, sender_device_id, initial_envelope, invited_at, expires_at}` |
| `SubscribeSecretChatResolutions` (device-scope) | — | `SecretChatInviteResolutionEvent{invite_id, recipient_user_id, recipient_device_id, accepted, response_envelope}` |
| `SubscribeSecretMessages` (device-scope) | — | `NewSecretMessageEvent{envelope: SecretEnvelope}` |

Все request-сообщения пустые (подписка — на всё, что относится к текущему пользователю/устройству по JWT). Секретные подписки — на конкретное устройство (`device_id` из сессии).

### 3.3 Onliner (порт 7009)

Онлайн-статусы и индикатор набора текста. Proto: `onliner_api.proto`.

#### `OnlinerApi` (клиентский)

| RPC | Вход | Выход |
|---|---|---|
| `SubscribeToOnlineStatus` | `user_ids: int64[]`, `user_uuids: string[]` | `stream UserOnlineStatus{user_id, status: StatusTypeId, last_seen, user_uuid}` |
| `SetOnlineStatus` | — | *(пусто)* |
| `GetOnlineStatus` | `user_ids: int64[]`, `user_uuids: string[]` | `users_statuses: UserOnlineStatus[]` |
| `ChangeUsersInSubscription` | `user_ids: int64[]`, `user_uuids: string[]` (новый полный набор) | *(пусто)* |
| `SetTypingStatus` | `chat_id: string`, `action: TypingAction{TYPING,CANCELLED}` | *(пусто)* |
| `SubscribeToTyping` | `chat_ids: string[]` | `stream TypingEvent{chat_id, user_id, action, user_uuid}` |
| `ChangeChatsInTypingSubscription` | `chat_ids: string[]` | *(пусто)* |

`StatusTypeId`: `UNKNOWN, ONLINE, OFFLINE`.

#### `OnlinerServerApi` (`Service`, для Federation)

| RPC | Вход | Выход |
|---|---|---|
| `UpsertRemoteStatus` | `user_uuid: string`, `status: StatusTypeId`, `last_seen: ts` | *(пусто)* |
| `InjectRemoteTyping` | `chat_id, user_uuid: string`, `action: TypingAction` | *(пусто)* |
| `GetLocalPresence` | `user_ids: int64[]` | `statuses: UserOnlineStatus[]` (privacy уже применена) |

### 3.4 Files (порт 7005, + HTTP-порт 7006 для upload)

Файлы, S3/Minio, стикеры. Proto: `files_api.proto`.

#### `FilesApi` (клиентский)

| RPC | Вход | Выход |
|---|---|---|
| `GetUploadUrl` | `file_type: UploadFileType` | `url, file_id: string` |
| `GetTempDownloadUrl` | `file_ids: string[]`, `fed_files: FedFileRef[]{origin_server, file_id}` | `file_urls: {file_id, url, preview_url}[]` |
| `CheckFileHash` | `file_hash: string` (SHA256 hex) | `file_id: string` (пусто если не найден) |
| `GetUserStorageInfo` | — | `total_used_storage, storage_limit: int64`, `storage_by_types: {file_type, used_storage}[]` |
| `ListStickerPacks` | `pagination: PageRequest` | `packs: StickerPackInfo[]`, `total_count: int32` |
| `GetStickerPack` | `pack_id: string` | `pack: StickerPackInfo`, `stickers: StickerInfo[]` |

`UploadFileType` enum: `USER_AVATAR, MESSAGE_ATTACHMENT_{IMAGE,VIDEO,GIF,DOCUMENT,AUDIO,VOICE,STICKER}, CHAT_PICTURE, USER_PROFILE_POSTER`.
Фактическая загрузка байтов — HTTP `POST /api/files/upload/{uploadId}` (не gRPC), на порт 7006 (или через Web-прокси).

#### `FilesServerApi` (`Service` — AdminPanel/Bots/Federation)

| RPC | Вход | Выход |
|---|---|---|
| `GetFileData` / `GetFilesData` | `file_id: string` / `file_ids: string[]` | `file_info: UploadFileInfo` / `files_infos: UploadFileInfo[]` |
| `UploadBadgeImage` | `image_data: bytes`, `filename: string` | `badge_image_id, permanent_url: string` |
| `GetUserStorageInfoServer` | `user_id: int64` | `GetUserStorageInfoResponse` (как в клиентском) |
| `UploadAvatarServer` | `image_data: bytes`, `filename: string`, `user_id: int64` | `file_url, preview_url, file_id: string` |
| `UploadPosterServer` | `image_data: bytes`, `filename: string`, `user_id: int64` | `file_url, file_id: string` |
| `UploadFileServer` | `data: bytes`, `filename: string`, `file_type: UploadFileType`, `owner_user_id: int64` | `file_id, preview_url: string`, `file_size: int64` |
| `GetTempDownloadUrlServer` | как `GetTempDownloadUrl` | как `GetTempDownloadUrl` |
| `CreateStickerPack` / `UpdateStickerPack` / `DeleteStickerPack` | `creator_user_id, name, description` / `pack_id, name, description, cover_sticker_id` / `pack_id` | `pack: StickerPackInfo` / `pack: StickerPackInfo` / *(пусто)* |
| `AddSticker` / `RemoveSticker` / `UpdateSticker` / `GetStickers` | `pack_id, file_id, emoji` / `sticker_id` / `sticker_id, emoji` / `sticker_ids: string[]` | `sticker: StickerInfo` / *(пусто)* / `sticker: StickerInfo` / `stickers: StickerInfo[]` |
| `UploadStickerImage` | `image_data: bytes`, `filename: string` | `file_id, file_url: string` |
| `FetchFileStream` | `file_id: string`, `range_from/to: int64` | `stream FileStreamChunk{data, total_size, content_type, file_name}` |
| `CheckFedAvatarAccess` | `file_id: string` | `allowed: bool` |

`StickerPackInfo`: `id, cover_sticker_id, name, description: string`, `creator_user_id: int64`, `created_at: ts`, `sticker_count: int32`.
`StickerInfo`: `id, sticker_pack_id, file_id, preview_file_id, emoji: string`, `added_at: ts`, `file_url, preview_url: string`.
`UploadFileInfo`: `id: string`, `uploaders: int64[]`, `created_at/uploaded_at: ts`, `etag, file_name, file_url, preview_url: string`, `type: UploadFileType`, `file_size: int64`, `preview_file_id: string`, `image_width/height: int32`.

---

## 4. Звонки

### 4.1 Calls (порт 7025 gRPC + 7026 HTTP webhooks)

Аудио/видео звонки (1-на-1 и групповые) поверх LiveKit SFU. Proto: `calls_api.proto` → **`CallsApi`** (все методы — `User`).

| RPC | Вход | Выход |
|---|---|---|
| `InitiateCall` | `oneof(callee_user_id: int64, chat_id: string)`, `media_type: CallMediaType{AUDIO,VIDEO}` | `call_id, livekit_url, access_token: string`, `audio_quality: CallAudioQuality` |
| `JoinCall` | `call_id: string` | `livekit_url, access_token: string`, `audio_quality` |
| `AcceptCall` | `call_id: string` | `livekit_url, access_token: string`, `audio_quality` |
| `RejectCall` / `EndCall` | `call_id: string` | *(пусто)* |
| `SetCallAudioQuality` | `call_id: string`, `quality: CallAudioQuality{AUTO,LOW,MEDIUM,HIGH}` | *(пусто)* |
| `SubscribeCallEvents` (device-scope) | — | `stream CallEvent` — oneof(`IncomingCallEvent`, `CallAcceptedEvent`, `CallRejectedEvent`, `CallEndedEvent`, `ParticipantEvent`, `CallAudioQualityChangedEvent`) |
| `ListCallHistory` | `filter: CallHistoryFilter{ALL,MISSED}`, `limit: int32` (≤50), `before_started_at: ts` (курсор) | `items: CallHistoryItem[]`, `has_more: bool` |
| `GetActiveCalls` | `chat_ids: string[]` | `calls: ActiveCallItem[]{call_id, chat_id, media_type, started_at, participant_user_ids}` |

`CallHistoryItem`: `call_id, chat_id: string`, `peer_user_id: int64`, `is_group: bool`, `media_type: CallMediaType`, `direction: CallDirection{INCOMING,OUTGOING}`, `end_reason: CallEndReason{HANGUP,REJECTED,MISSED,BUSY,FAILED}`, `started_at/answered_at/ended_at: ts`, `duration_seconds: int64`.

Webhooks (HTTP 7026) — приём от LiveKit (`room_finished`, `participant_joined/left`), не публичный API сервиса.

---

## 5. Боты

### 5.1 Bots (порт 7027 gRPC + 7028 HTTP REST `/bot/{method}`)

Bot API по образцу Telegram. Proto: `bots_api.proto`.

#### `BotsServerApi` (`Service`, для AdminPanel)

| RPC | Вход | Выход |
|---|---|---|
| `CreateSystemBot` | `username, name: string` | `bot_id: int64`, `token: string` (показывается один раз) |
| `ListBots` | — | `bots: BotInfo[]{id, username, name, owner_user_id, system_role, created_at}` |
| `DeleteBot` | `bot_id: int64` | *(пусто)* |
| `RegenerateToken` | `bot_id: int64` | `token: string` |

#### `BotsExternalApi` (`Bot`-токен, для внешних программ)

| RPC | Вход | Выход |
|---|---|---|
| `GetMe` | — | `id: int64`, `is_bot: bool`, `first_name, username: string` |
| `SendMessage` | `oneof(chat_id: string, user_id: int64)`, `text: string` (≤4096), `file_ids: string[]` | `message_id: int64`, `chat_id: string`, `sent_at: ts` |
| `GetUserInfo` | `oneof(user_id: int64, username: string)` | `id: int64`, `username, first_name, last_name, bio, avatar_url: string`, `is_bot: bool` |
| `GetFile` | `file_id: string` | `file_id, file_name: string`, `file_size: int64`, `file_url: string` (временная ссылка) |
| `EditMessage` | `message_id: int64`, `text: string`, `file_ids: string[]` | `message_id: int64`, `text: string`, `edited_at: ts` |
| `DeleteMessage` | `message_id: int64` | *(пусто)* |
| `SetMyCommands` | `commands: BotCommand[]{command, description}` (≤100, заменяет целиком) | *(пусто)* |
| `GetMyCommands` | — | `commands: BotCommand[]` |
| `SubscribeUpdates` | `offset: int64` (подтвердить/удалить < offset) | `stream BotUpdate{update_id, message: BotIncomingMessage}` |

`BotIncomingMessage`: `message_id: int64`, `chat_id: string`, `from_user_id: int64`, `from_username, from_first_name: string`, `date: ts`, `text: string`, `attachments: BotAttachment[]{file_id, type, preview_url, file_size}`.

#### HTTP REST (порт 7028, `/bot/{method}`, bot-JWT в заголовке `x-auth-token`, ответы `{"ok":bool,"result"/"error_code","description"}`)

| Метод | Путь | Вход | `result` |
|---|---|---|---|
| GET | `/bot/getMe` | — | `{id, is_bot, first_name, username}` |
| POST | `/bot/sendMessage` (JSON) | `chat_id`/`user_id`, `text` (≤4096) | объект сообщения |
| POST | `/bot/sendPhoto`, `/bot/sendDocument` (multipart) | `file` + `chat_id`/`user_id` + `caption?` | сообщение + вложение |
| POST | `/bot/editMessage` (JSON) | `message_id, text, file_ids?` | `{message_id, text, edited_at}` |
| POST | `/bot/deleteMessage` (JSON) | `message_id` | `true` |
| GET | `/bot/getFile` | `file_id=` | `{file_id, file_name, file_size, file_url}` |
| POST | `/bot/setMyCommands` (JSON) | `commands: [{command, description}]` | `true` |
| GET | `/bot/getMyCommands` | — | `[{command, description}]` |
| GET | `/bot/getUpdates` | `offset?, limit?≤100, timeout?≤50` (long-poll) | массив update'ов |
| GET | `/bot/getUserInfo` | `user_id=`/`username=` | публичный профиль |

---

## 6. Федерация

### 6.1 Federation (порт 7030 gRPC + 7031 well-known HTTP)

Единственная точка входа/выхода межсерверного (S2S) трафика ноды. Proto: `federation_api.proto` (S2S) + `federation_internal_api.proto` (internal).

#### `FederationS2SApi` (публичный S2S, авторизация — **не XAuth**, Ed25519-подпись XFed, не JWT)

| RPC | Вход | Выход |
|---|---|---|
| `Ping` | `origin_server: string`, `protocol_versions: int32[]` | `server_name: string`, `protocol_versions: int32[]`, `server_time: ts`, `capabilities: string[]` (напр. `presence`, `typing`) |
| `GetServerKeys` | — | `server_name: string`, `keys: SigningKey[]{key_id, public_key, expired_at}` |
| `GetUserProfile` | `oneof(username, uuid): string` | `found: bool`, `uuid, username, first_name, last_name, bio: string`, `avatar: FederatedFileRef?` |
| `DeliverEvents` | `events: FederationEvent[]` (батч, лимит по размеру) | `results: EventResult[]{event_id, status: EventStatus, error_code}` |
| `FetchChatHistory` | `chat_id: string`, `since_ts_ms: int64`, `limit: int32` | `events: FederationEvent[]`, `has_more: bool` |
| `FetchFile` | `file_id: string`, `range_from/to: int64` | `stream FetchFileChunk{data, total_size, content_type}` |
| `SubscribePresence` | `user_uuids: string[]` | `stream PresenceEvent{user_uuid, status: PresenceStatus, last_seen}` |
| `DeliverTyping` | `chat_id, sender_uuid: string`, `action: int32` | *(пусто)* |

`FederationEvent`: `event_id, origin_server: string`, `origin_ts_ms: int64`, `origin_signature: bytes`, `origin_key_id: string`, `oneof payload`(`ChatCreatedPayload`, `NewMessagePayload`, `MessageEditedPayload`, `MessageDeletedPayload`, `MessagesReadPayload`, `UserProfileChangedPayload`, `UserDeactivatedPayload`).
`EventStatus`: `OK, ALREADY_PROCESSED, REJECTED, RETRY`.

`GET /.well-known/barkfluff` (HTTP, порт 7031) — JSON-документ discovery (ключи, endpoint, подпись), без XAuth.

#### `FederationInternalApi` (`Service`, для AdminPanel и других сервисов ноды)

| RPC | Вход | Выход |
|---|---|---|
| `ResolveRemoteUser` | `oneof(fid, uuid): string`, `server_name: string?` | `found: bool`, `profile: GetUserProfileResponse`, `server_name: string` |
| `FetchRemoteFile` | `server_name, file_id: string`, `range_from/to: int64` | `stream FetchFileChunk` |
| `FetchRemoteChatHistory` | `server_name, chat_id: string`, `since_ts_ms: int64`, `limit: int32` | `FetchChatHistoryResponse` |
| `GetKnownServers` | — | `servers: KnownServerInfo[]{server_name, federation_endpoint, source, status, first_seen_at, last_seen_at, keys}` |
| `UpsertManualPeer` | `server_name, federation_endpoint: string`, `keys: SigningKey[]`, `tls_spki_sha256: string[]` | *(пусто)* |
| `SetServerBlocked` | `server_name: string`, `blocked: bool` | *(пусто)* |
| `GetFederationStatus` | — | `server_name: string`, `enabled: bool`, `own_keys: SigningKey[]`, `outbox_pending/deadletter: int64`, `known_servers_active: int32` |
| `RotateSigningKey` | — | `new_key_id, old_key_id: string`, `old_key_expires_at: ts` |
| `EnqueueOutbound` | `event: FederationEvent`, `destinations: string[]` | `event_id: string`, `enqueued: int32` |
| `SetPresenceInterest` | `instance_id: string`, `user_uuids: string[]` (полный набор) | `accepted_count: int32` |
| `DeliverTypingOutbound` | `chat_id, sender_uuid: string`, `action: int32`, `destination_servers: string[]` | *(пусто)* |

---

## 7. Уведомления

### 7.1 Notification (порт 7004)

**Нет gRPC/REST API** — только consumer RabbitMQ. Обрабатывает `EmailNotification` из очереди `notifications-email-handler`, шлёт email по SMTP. Типы: `ConfirmationRegistration`, `ConfirmationOtpEmail`, `ConfirmationAuth`, `ResetPassword`, `FailedLogin`, `SuccessfulRegistration`, `SuccessfulLogin`, `PasswordChanged`, `PasswordChangedByAdmin`, `TwoFactorMethodChanged`.

### 7.2 CloudMessaging

**Нет gRPC API** — background worker, consumer RabbitMQ, отправляет push через Firebase Cloud Messaging (data-only, кроме admin-рассылки).

| Событие (RabbitMQ) | Источник | FCM `type` | Ключевые payload-поля |
|---|---|---|---|
| `PushNotificationEvent` | Messages | `new_message` | `chat_id, sender_id, sender_name, avatar_url, chat_title, chat_avatar_url, is_group_chat, content_type, image_url, message_id, message_text, attachment_count` |
| `DismissPushEvent` | Updates | `dismiss_chat_notifications` | `chat_id` |
| `AdminBroadcastNotificationEvent` | AdminPanel (`POST /api/notifications/broadcast/*`) | `admin_broadcast` (native `Notification`-блок: title/body/imageUrl) | — |
| `IncomingCallPushEvent` | Calls | `incoming_call` | `call_id, caller_user_id, chat_id, media_type, started_at, caller_name, avatar_url, chat_title` |
| `CallDismissPushEvent` | Calls | `dismiss_call` | `call_id, reason` |
| `PrivateChatInviteEvent` | Messages | `private_chat_invite` | (текст локализуется на клиенте) |

---

## 8. Порталы и вспомогательные HTTP-сервисы

### 8.1 Developers (порт 7020, gRPC-Web напрямую, без Web-прокси)

Портал документации для клиентских разработчиков. Proto: `developers_api.proto` → **`DevelopersApi`** (все методы — `User`).

| RPC | Вход | Выход |
|---|---|---|
| `GetDocumentationSections` | — | `sections: DocumentationSection[]{key, title, type, order, content}` |
| `GetDocumentationSection` | `key: string` | `DocumentationSection` |
| `GetProtoFiles` | — | `files: ProtoFileInfo[]{file_name, display_name, slug, order, rpc_descriptions}` |
| `GetProtoFileContent` | `file_name: string` | `content: string`, `metadata: ProtoFileInfo` |
| `GetErrorCodes` | — | `error_codes: ErrorCodeEntry[]{code, exception_name, description, domain}` |

### 8.2 AdminPanel (порт 51888, REST + Telegram-бот, не подключён к XAuth-платформе)

ASP.NET Minimal API дашборд администратора. Аутентификация — cookie `auth_token` через Telegram-подтверждение (не XAuth-JWT).

| Метод | Путь | Вход | Выход |
|---|---|---|---|
| POST | `/api/auth/request` | `username` | инициирует запрос подтверждения в Telegram |
| GET | `/api/auth/status/{requestId}` | — | статус подтверждения (polling) |
| GET | `/api/auth/me` / `/api/auth/me/avatar` | — | профиль/аватар текущего Telegram-админа |
| GET | `/api/configuration/all` | — | все строки Settings (legacy route; проксирует wire-compatible `ConfigurationApi.GetAllConfigurations`) |
| POST | `/api/configuration/update` | `{section, key, serviceId, value}` | обновляет Settings через wire-compatible `ConfigurationApi.UpdateConfiguration` |
| GET | `/api/configuration/s3-configuration` | — | конфигурация S3-бакетов |
| POST | `/api/configuration/s3/update` | конфиг S3 | — |
| POST | `/api/notifications/broadcast/all` | `{title, body, imageUrl?, confirm: true}` | `{enqueued: true}` → публикует `AdminBroadcastNotificationEvent` |
| POST | `/api/notifications/broadcast/devices` | `{title, body, imageUrl?, deviceIds: string[]}` | `{enqueued: true, deviceCount}` |
| POST | `/api/seq/compress-metrics/run?date=YYYY-MM-DD` | — | ручной запуск сжатия метрик-логов Seq |
| WS | `/api/remote/servers/{serverId}/console` | — | интерактивная SSH-консоль (PTY) через WebSocket |
| — | `/federation`, `/api/federation/status\|peers\|keys/rotate` | — | статус федерации, ротация ключей, управление пирами (проксирует `FederationInternalApi`) |
| — | `Endpoints/BotsEndpoints`, `UsersEndpoints`, и др. | — | проксируют `BotsServerApi`, `UsersServerApi`, `FilesServerApi`, `IdentityServerApi` для соответствующих страниц (`/bots`, `/users`, `/badges`, `/stickers`, `/services`, `/logs`, `/dashboard`) |

Полный список внутренних эндпоинтов дашборда — в `Backend/Barkfluff.AdminPanel/Endpoints/*.cs` (по одному extension-методу `Map{Name}Endpoints()` на группу); в проекте это не публичный API для клиентов, а внутренняя админ-плоскость.

### 8.3 WebServer (порт 64641, публичный REST/HTML)

| Контроллер | Маршрут | Вход | Выход |
|---|---|---|---|
| `HomeController` | `GET /` | — | `html/barkfluff.html` |
| `InstallController` | `GET /install.ps1`, `/installbeta.ps1`, `/install.sh`, `/installbeta.sh` | — | скрипты установки |
| `DownloadController` | `GET /download/installer` | — | `Barkfluff.Updater.CLI.exe` |
| `UserApiController` | `GET /api/user/{username}` | `username` (path, regex `^[a-zA-Z0-9_]{3,32}$`) | `{found, firstName, lastName, username, bio, profilePicture, profilePosterUrl}` |
| `VersionApiController` | `GET /api/versions` | — | версии клиентов (Android/Windows/macOS, release+beta) |
| `SupportChatController` | `POST /api/support/send` | текст сообщения + `chatId` (cookie) | статус отправки в Telegram-чат поддержки |
| | `GET /api/support/messages/{chatId}` | `chatId` | история сообщений чата |
| `AssetsController` | `GET /assets/{filename}` | `filename` (whitelist) | статический файл |
| `FaviconController` | `GET /favicon.ico` | — | иконка |
| `FallbackController` | `GET /{**catchAll}` | путь | `legal/*` → юр. документы; `selfhosted` → инструкция; иначе → страница профиля пользователя или `404.html` |

### 8.4 ClientStorage (без указанного порта в конфиге; автономный, вне XAuth/Settings)

Хранилище клиентских дистрибутивов (Windows/WinUI/Android/macOS/iOS). REST на ASP.NET Core, S3 + SQLite.

| Метод | Путь | Вход | Выход |
|---|---|---|---|
| GET | `/get/barkfluff{windows\|winui\|kotlin\|macos\|ios}[/{channel}]` | `channel?` (`release`/`beta`/`dev`/`nightly`) | файл (streaming, поддержка `Range`) |
| GET | `/get/barkfluff{platform}[/{channel}]/version` | `channel?` | версия строкой |
| GET | `/get/barkfluffwindows[/{channel}]/bitsurl` | `channel?` | `{url, fileName, fileSize, checksum, version, uploadedAt}` (presigned S3 URL, TTL 6ч, для BITS) |
| POST | `/set/barkfluff{platform}[/{channel}]` | Bearer `UPLOAD_TOKEN`, заголовок `X-App-Version`, multipart `file` (≤512 МБ) | статус загрузки |

### 8.5 Web (порт 7016, gRPC-Web reverse proxy + статика; не собственный бизнес-API, а прокси)

| Маршрут | Backend | Протокол |
|---|---|---|
| `GET /` | статика `wwwroot/` (веб-мессенджер) | HTTP |
| `GET /ping`, `GET /health` | локальный liveness/readiness | HTTP |
| `GET /ping/{service}` (`Identity, Users, Messages, Files, Updates, Onliner, FastAuth, Calls, Beacon, Navigator`) | проксирует `/ping` нужного сервиса | HTTP, анонимно (только режим `Node`) |
| `/barkfluff.{identity,messages,users,files,updates,onliner,fast.auth,calls,beacon,navigator}.*Api/{**catch-all}` | gRPC-Web → HTTP/2 gRPC прокси на соответствующий сервис (YARP) | gRPC-Web |
| `POST /api/files/upload/{uploadId}` | Files (HTTP-порт 7006), лимит тела поднят до 512 МБ | HTTP/1.1 |
| `GET /node-config.js`, `GET /pwa-config.js` | конфигурация клиента (режим ноды/шелла, публичные Firebase/VAPID параметры) | HTTP |

Собственных бизнес-эндпоинтов у Web нет — все RPC, доступные через него, описаны в соответствующих разделах выше (Identity, Users, Messages, Files, Updates, Onliner, FastAuth, Calls, Beacon, Navigator).

---

## Не включено в справочник

- **`BarkFluff.GrpcServer`** — shared-библиотека инфраструктуры (XAuth, interceptors, Serilog, метрики), не самостоятельный сервис с собственными эндпоинтами.
- **`BarkFluff.Users.Rust`** — экспериментальный drop-in порт Users (не в основном compose), контракт идентичен `users_api.proto`.
- **`dev-federation-testbed`** — стенд для разработки федерации, не production-сервис.
