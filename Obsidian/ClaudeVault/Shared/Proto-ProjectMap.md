# BarkFluff.Proto — Карта проекта

Расположение: `Shared/BarkFluff.Proto/`
Целевой фреймворк: `net10.0`
Нет рукописного C# — только `.proto` файлы. Код генерируется `Grpc.Tools` при сборке.

← [[Shared/Proto]]

---

## Файлы проекта

### `BarkFluff.Proto.csproj`
Конфигурация проекта. `TargetFramework: net10.0`, `Nullable: enable`. Не содержит явных `<Protobuf>` items — proto-файлы подключаются из сервисов-потребителей напрямую через относительные пути.

---

### `shared.proto`
**Namespace:** `BarkFluff.Proto.Shared`
**Общие типы**, импортируемые в несколько proto-файлов.

| Тип | Описание |
|-----|----------|
| `PageRequest` | Пагинация: `offset` + `size` |
| `Message` | Сообщение: id, sender_id, read_by, sent_at, content, type |
| `MessageContent` | Текст + список `MessageAttachment` |
| `MessageAttachment` | Вложение: id, type, file_id, preview_url, size, preview_file_id, file_name, image_width (field 8), image_height (field 9) |
| `MessageAttachmentType` | Enum: UNKNOWN, IMAGE, VIDEO, GIF, DOCUMENT, AUDIO, VOICE, STICKER |
| `MessageContentType` | Enum: UNKNOWN, GENERIC, SYSTEM |
| `PinnedMessageInfo` | Закреплённое сообщение: `Message` + pinner_user_id + pinned_at |
| `ChatType` | Enum: REGULAR, PRIVATE (E2E через passphrase), SECRET (Signal Double Ratchet — НЕ хранится на сервере, не возвращается ListChats) |
| `EncryptedMessage` | Шифрованное сообщение приватного чата: id, chat_id, sender_id, sender_device_id, sent_at, ciphertext, nonce, associated_data, is_edited, edited_at, is_deleted. Сервер хранит как opaque blob и не имеет ключа для расшифровки |
| `SecretEnvelope` | Конверт секретного сообщения: message_id, sender_user_id, sender_device_id, recipient_device_id, envelope (libsignal opaque), sent_at. Сервер только релэит — в БД не сохраняется |

> Импортируется в: `messages_api.proto`, `updates_api.proto`, `users_api.proto`, `files_api.proto`

---

### `identity_api.proto`
**Namespace:** `BarkFluff.Proto.Identity`
**Auth, JWT, 2FA, сессии, регистрация, сброс пароля.**

#### `IdentityApi` (публичный, User token)
| RPC | Описание |
|-----|----------|
| `Auth` | Авторизация по username/email + пароль + OTP (oneof login) |
| `FastAuth` | Быстрый вход по fast_auth_id |
| `CreateToken` | Обновить access token по refresh token. `access_token` — field_number **2** (не 1)! |
| `CreateAccount` | Регистрация: имя, фамилия, username, email |
| `ConfirmAccount` | Подтверждение регистрации по коду |
| `GetActiveSessions` | Список активных сессий текущего пользователя |
| `RemoveActiveSession` | Удалить сессию по device_id |
| `EnableOtpVerification` | Включить 2FA (Authenticator или Email). Возвращает QR-код |
| `ConfirmOtpVerification` | Подтвердить включение 2FA по OTP-коду |
| `DisableOtpVerification` | Отключить 2FA метод |
| `ListOtpVerification` | Статус 2FA текущего пользователя |
| `ResetPassword` | Запрос сброса пароля (oneof login: username/email) |
| `ConfirmResetPassword` | Подтвердить сброс по reset_id + OTP; возвращает токены |
| `SetPassword` | Установить новый пароль (требует old_password если уже задан) |
| `Logout` | Разлогиниться с текущего устройства |

#### `IdentityServerApi` (внутренний, Service token)
| RPC | Описание |
|-----|----------|
| `ListOtpVerificationServer` | Статус 2FA пользователя по user_id |
| `DisableOtpVerificationServer` | Принудительно отключить 2FA |
| `GetActiveSessionsServer` | Сессии пользователя по user_id |
| `RemoveActiveSessionServer` | Удалить сессию пользователя |
| `CreateSessionForUserServer` | Создать сессию (access+refresh) для пользователя из другого сервиса (FastAuth). Регистрирует устройство в Users, отправляет уведомление о входе. |

**Ключевые типы:** `Token` (value + expiration_date), `OtpTypeId` (Authenticator/Email), `AuthRequest` (oneof login)

---

### `users_api.proto`
**Namespace:** `BarkFluff.Proto.Users`
**Профили, устройства, бейджи, поиск, приватность, персонализация.**

#### `UsersApi` (публичный, User token)
| RPC | Описание |
|-----|----------|
| `GetUser` | Информация о пользователе |
| `SetProfilePicture` | Установить аватарку |
| `CheckExistUsername` / `CheckExistEmail` | Проверить занятость логина/почты |
| `ChangeName` | Изменить имя/фамилию |
| `ChangeUsername` | Изменить юзернейм |
| `ChangeBio` | Изменить биографию |
| `SearchUsers` | Поиск по имени/фамилии/юзернейму |
| `GetUserBadges` | Бейджи пользователя |
| `GetDevices` / `GetCurrentDevice` | Устройства текущего пользователя |
| `RenameDevice` | Переименовать устройство |
| `SetFirebaseToken` | Установить Firebase push-токен устройства |
| `SetNotificationsEnabled` | Вкл/выкл уведомления для текущего устройства |
| `GetPrivacySettings` / `UpdatePrivacySettings` | Настройки приватности |
| `GetPersonalization` / `UpdatePersonalization` | Персонализация (тема, язык и т.п.) |
| `GetProfilePoster` / `SetProfilePoster` | Постер профиля (горизонтальный баннер) |
| `GetChatFolders` / `CreateChatFolder` / `UpdateChatFolder` / `DeleteChatFolder` / `AddChatToFolder` / `RemoveChatFromFolder` / `ReorderChatFolders` | Папки чатов |
| `RegisterPrekeyBundle` | **(X3DH)** Зарегистрировать prekey-bundle текущего устройства: identity_pubkey, signed_prekey + signature, начальный пул one-time prekeys (рекомендуется 100) |
| `FetchPrekeyBundle` | **(X3DH)** Получить bundle устройства собеседника. Расходует одну one-time prekey. Возвращает `remaining_one_time_prekeys` для проактивного пополнения |
| `ListPeerDevices` | Список устройств пользователя с `has_bundle` для выбора целевого устройства секретного чата |
| `ReplenishOneTimePrekeys` | Пополнить пул one-time prekeys текущего устройства |
| `RotateSignedPrekey` | Сменить signed prekey текущего устройства |

#### `UsersServerApi` (внутренний, Service token)
| RPC | Описание |
|-----|----------|
| `FindByLogin` | Найти пользователя по username/email (oneof login) |
| `CheckExistUsername` / `CheckExistEmail` | Проверка |
| `AddDraftUser` / `OverrideDraftUser` | Создать/переопределить черновик пользователя |
| `ConfirmUser` | Подтвердить пользователя |
| `GetById` / `ListByIds` | Получить пользователей по id |
| `GetUserContacts` | Контакты пользователя |
| `AssignUserBadge` / `RemoveUserBadge` / `UpdateUserBadgePriority` | Управление бейджами пользователей |
| `CreateBadge` / `GetAllBadges` / `UpdateBadge` / `DeleteBadge` | CRUD бейджей |
| `ExportData` | Экспорт всех данных пользователя (GDPR) |
| `RegisterDevice` | Зарегистрировать устройство (вызывается Identity при авторизации) |
| `GetUserDevices` / `DeleteUserDevice` | Устройства пользователя по userId |
| `GetUserByUsername` | Публичная информация пользователя по юзернейму (для веб-сервера). Поле `profile_poster_url` (field 7) |
| `SearchUsersServer` | Поиск пользователей (для AdminPanel) |
| `UpdateStorageLimit` | Обновить лимит хранилища пользователя |
| `SetProfilePictureServer` | Установить аватарку (для AdminPanel) |
| `GetDevicesWithFirebaseTokens` | Устройства с Firebase токенами (для CloudMessaging) |

---

### `messages_api.proto`
**Namespace:** `BarkFluff.Proto.Messages`
**Чаты, сообщения, вложения.**
Импортирует: `shared.proto`

#### `MessagesApi` (один сервис, User token)
| RPC | Описание |
|-----|----------|
| `ListChats` | Список чатов с пагинацией. Возвращает `Chat` с last_message, count_unread, first_unread_message_id |
| `ListMessages` | Сообщения в чате. `count` устарело — использовать `offset_before`/`offset_after` |
| `ListChatMembers` | Участники чата с пагинацией |
| `SendMessage` | Отправить сообщение. `oneof source_id`: chat_id ИЛИ user_id |
| `CreateGroupChat` | Создать групповой чат |
| `KickUser` | Удалить пользователя из группового чата |
| `MarkAsRead` | Пометить сообщения прочитанными |
| `ListChatAttachments` | Вложения чата с фильтрацией по типу и пагинацией |
| `GetPersonChatId` | Получить ID личного чата с пользователем |
| `GetChatInfo` | Информация о чате (last_message_id, first_unread_message_id) |
| `EditMessage` / `DeleteMessage` | Редактирование/удаление обычного сообщения |
| `PinMessage` / `UnpinMessage` / `ListPinnedMessages` / `UnpinAll` | Закрепление сообщений в чате |

**Приватные чаты (E2E через passphrase):**
| RPC | Описание |
|-----|----------|
| `CreatePrivateChat` | Создать приватный чат: peer_user_id + kdf_salt (Argon2id) + passphrase_verifier. Собеседник присоединяется через AcceptPrivateChat |
| `AcceptPrivateChat` / `RejectPrivateChat` | Принять/отклонить приглашение в приватный чат |
| `SendPrivateMessage` | Отправить шифрованное сообщение (ciphertext+nonce+AAD). Сохраняется в `EncryptedMessages` |
| `ListPrivateMessages` | Двунаправленная пагинация шифрованных сообщений (offset_before/offset_after) |
| `EditPrivateMessage` / `DeletePrivateMessage` | Редактировать/удалить шифрованное сообщение (soft-delete очищает ciphertext) |

**Секретные чаты (Signal Double Ratchet, НЕ хранятся на сервере):**
| RPC | Описание |
|-----|----------|
| `SendSecretChatInvite` | Отправить инвайт с initial X3DH message (PreKeySignalMessage) на конкретное устройство собеседника. Буферизуется в Redis (24ч) |
| `AcceptSecretChatInvite` / `RejectSecretChatInvite` | Принять/отклонить инвайт. Опционально приложить response_envelope в Accept |
| `SendSecretMessage` | Отправить SignalMessage конкретному устройству. Буферизуется в Redis (24ч), push без содержимого |
| `AckSecretMessage` | Подтвердить доставку — сервер удаляет envelope из буфера |

**Ключевые типы:** `Chat` (поля 9-11: chat_type, kdf_salt, passphrase_verifier — последние два только для PRIVATE), `ChatMember`, `OutgoingMessage` (text + files_ids), `ChatAttachmentInfo`. Для приватных используется `barkfluff.shared.EncryptedMessage`, для секретных — `barkfluff.shared.SecretEnvelope`.

---

### `files_api.proto`
**Namespace:** `BarkFluff.Proto.Files`
**Upload/download URL, стикерпаки, бейджи, аватарки.**
Импортирует: `shared.proto`

#### `FilesApi` (публичный, User token)
| RPC | Описание |
|-----|----------|
| `GetUploadUrl` | Presigned URL для загрузки файла по `UploadFileType` |
| `GetTempDownloadUrl` | Временные URL для скачивания файлов |
| `CheckFileHash` | Проверить дедупликацию по хешу |
| `GetUserStorageInfo` | Информация о хранилище текущего пользователя |
| `ListStickerPacks` / `GetStickerPack` | Список стикерпаков и их содержимое |

#### `FilesServerApi` (внутренний, Service token)
| RPC | Описание |
|-----|----------|
| `GetFileData` / `GetFilesData` | Данные о загруженных файлах |
| `UploadBadgeImage` | Загрузить PNG для бейджа (постоянная ссылка, без сжатия) |
| `GetUserStorageInfoServer` | Хранилище пользователя (AdminPanel) |
| `UploadAvatarServer` | Загрузить аватарку (AdminPanel) |
| `CreateStickerPack` / `UpdateStickerPack` / `DeleteStickerPack` | CRUD стикерпаков |
| `AddSticker` / `RemoveSticker` / `UpdateSticker` / `GetStickers` | CRUD стикеров |
| `UploadStickerImage` | Загрузить изображение стикера (AdminPanel) |

**Ключевые типы:** `UploadFileInfo` (id, uploaders, etag, type, image_width field 12, image_height field 13), `UploadFileType` enum (включая `USER_PROFILE_POSTER = 10`)

---

### `updates_api.proto`
**Namespace:** `BarkFluff.Proto.Updates`
**Real-time Server Streaming событий.**
Импортирует: `shared.proto`

#### `UpdatesApi` (один сервис, User token)
| RPC | Тип | Описание |
|-----|-----|----------|
| `SubscribeNewMessages` | server streaming | Стрим новых сообщений. Событие: `NewMessageEvent` (message + chat_id) |
| `SubscribeMessagesRead` | server streaming | Стрим событий прочтения. `MessageReadEvent.new_read_by` — **полный** список прочитавших, не только новые |
| `SubscribeMessagesEdited` / `SubscribeMessagesDeleted` | server streaming | Редактирование/удаление обычных сообщений |
| `SubscribeMessagesPinned` / `SubscribeMessagesUnpinned` / `SubscribeAllMessagesUnpinned` | server streaming | События закрепления сообщений |

**Приватные чаты (user-scoped — получают все устройства пользователя):**
| RPC | Событие | Описание |
|-----|---------|----------|
| `SubscribePrivateMessages` | `NewEncryptedMessageEvent` | Новые шифрованные сообщения в приватных чатах |
| `SubscribePrivateMessageEdits` | `EncryptedMessageEditedEvent` | Редактирование шифрованных сообщений |
| `SubscribePrivateMessageDeletes` | `EncryptedMessageDeletedEvent` | Удаление шифрованных сообщений |
| `SubscribePrivateChatInvites` | `PrivateChatInviteEvent` | Приглашения в приватные чаты (для приглашённого) — содержит kdf_salt + passphrase_verifier |
| `SubscribePrivateChatInviteResolutions` | `PrivateChatInviteResolutionEvent` | accepted/rejected (для отправителя инвайта) |

**Секретные чаты (device-scoped — получает только конкретное устройство по recipient_device_id из JWT текущей сессии):**
| RPC | Событие | Описание |
|-----|---------|----------|
| `SubscribeSecretChatInvites` | `SecretChatInviteEvent` | Инвайты, адресованные текущему устройству — содержит initial_envelope (PreKeySignalMessage) |
| `SubscribeSecretChatResolutions` | `SecretChatInviteResolutionEvent` | accept/reject от собеседника на инвайты, отправленные текущим устройством |
| `SubscribeSecretMessages` | `NewSecretMessageEvent` | Секретные сообщения, адресованные текущему устройству — содержит `SecretEnvelope` |

---

### `onliner_api.proto`
**Namespace:** `BarkFluff.Proto.Onliner`
**Онлайн-статусы пользователей.**

#### `OnlinerApi` (один сервис, User token)
| RPC | Тип | Описание |
|-----|-----|----------|
| `SubscribeToOnlineStatus` | server streaming | Подписка на изменения статуса списка пользователей |
| `SetOnlineStatus` | unary | Установить статус "В сети" (heartbeat) |
| `GetOnlineStatus` | unary | Получить статусы нескольких пользователей |
| `ChangeUsersInSubscription` | unary | Обновить список пользователей в подписке (полная замена массива) |
| `SetTypingStatus` | unary | Heartbeat «печатает» в `chat_id` (`action`: TYPING/CANCELLED); ретранслируется участникам |
| `SubscribeToTyping` | server streaming | Подписка на `TypingEvent` по списку `chat_ids` |
| `ChangeChatsInTypingSubscription` | unary | Обновить список чатов в typing-подписке (полная замена массива) |

**Enum `StatusTypeId`:** UNKNOWN, STATUS_ONLINE, STATUS_OFFLINE
**Enum `TypingAction`:** TYPING_ACTION_UNKNOWN, TYPING_ACTION_TYPING, TYPING_ACTION_CANCELLED
**`UserOnlineStatus`:** user_id + status + last_seen timestamp
**`TypingEvent`:** chat_id + user_id + action

---

### `fast_auth_api.proto`
**Namespace:** `BarkFluff.Proto.FastAuth`
**QR/текстовая авторизация нового устройства через уже авторизованное.**

#### `FastAuthApi` — 4-шаговый флоу
| Шаг | RPC | Auth | Описание |
|-----|-----|------|----------|
| 1 | `GenerateFastAuthToken` | анонимно | Запросить QR или TEXT токен. Метаданные устройства — в gRPC headers |
| 2 | `SubscribeFastAuthResult` | анонимно | Server streaming: ждать ACCEPTED/REJECTED/EXPIRED |
| 3 | `ScanFastAuth` | User token | Сканировать QR. Возвращает метаданные устройства + `confirmation_code` |
| 4a | `AcceptFastAuth` | User token | Подтвердить вход (fast_auth_id + confirmation_code) |
| 4b | `RejectFastAuth` | User token | Отклонить вход |

#### `FastAuthServerApi` (внутренний, Service token)
| RPC | Описание |
|-----|----------|
| `GetFastAuthInfo` | Получить информацию о fast-auth сессии |

**Enum `TokenFormat`:** QR (PNG base64), TEXT
**Enum `FastAuthStatus`:** PENDING, SCANNED, ACCEPTED, REJECTED, EXPIRED

---

### `beacon_api.proto`
**Namespace:** `BarkFluff.Proto.Beacon`
**Точка входа клиента — описание сервера и адреса микросервисов.**

#### `BeaconApi` (один сервис)
| RPC | Описание |
|-----|----------|
| `GetServerInfo` | Имя, описание, цвета сервера + `Service` для каждого микросервиса (endpoint + tls_enabled + status) |

**Сервисы в ответе:** identity, users, files, messages, updates, onliner, fast_auth
**`ServiceStatus` enum:** Unknown, Healthy, Degraded, Unhealthy, Offline

---

### `navigator_api.proto`
**Namespace:** `BarkFluff.Proto.Navigator`
**Глобальный реестр серверов BarkFluff.**

#### `NavigatorApi` (один сервис)
| RPC | Описание |
|-----|----------|
| `ListServers` | Получить список всех зарегистрированных серверов |
| `RegisterServer` | Зарегистрировать сервер в реестре |

**`ServerInfo`:** name, description, accounts_count, beacon_uri, server_public_name, location, color

---

### `configuration_api.proto`
**Namespace:** `BarkFluff.Proto.Configuration`
**Централизованная конфигурация микросервисов + зарезервированные юзернеймы.**

#### `ConfigurationApi` (один сервис)
| RPC | Описание |
|-----|----------|
| `GetConfiguration` | Получить настройки сервиса по service_id |
| `UpdateConfiguration` | Обновить конкретный ключ (section + key + value) |
| `GetReservedNames` | Список зарезервированных юзернеймов |
| `AddReservedName` / `UpdateReservedName` / `DeleteReservedName` | CRUD зарезервированных юзернеймов |

**`ConfigurationItem`:** section, key, value, edited_at, edited_by, edited_from, service_id

---

### `developers_api.proto`
**Namespace:** `BarkFluff.Proto.Developers`
**Портал документации для разработчиков.**

#### `DevelopersApi` (один сервис)
| RPC | Описание |
|-----|----------|
| `GetDocumentationSections` | Список разделов документации |
| `GetDocumentationSection` | Получить раздел по key |
| `GetProtoFiles` | Список proto-файлов с метаданными |
| `GetProtoFileContent` | Содержимое proto-файла по имени |
| `GetErrorCodes` | Список кодов ошибок (code, exception_name, description, domain) |

---

## Паттерн Service Pairs

| Proto | Публичный сервис | Внутренний сервис |
|-------|-----------------|-------------------|
| `identity_api.proto` | `IdentityApi` | `IdentityServerApi` |
| `users_api.proto` | `UsersApi` | `UsersServerApi` |
| `messages_api.proto` | `MessagesApi` | — |
| `files_api.proto` | `FilesApi` | `FilesServerApi` |
| `fast_auth_api.proto` | `FastAuthApi` | `FastAuthServerApi` |
| `updates_api.proto` | `UpdatesApi` | — |
| `onliner_api.proto` | `OnlinerApi` | — |
| `beacon_api.proto` | `BeaconApi` | — |
| `navigator_api.proto` | `NavigatorApi` | — |
| `configuration_api.proto` | `ConfigurationApi` | — |
| `developers_api.proto` | `DevelopersApi` | — |
