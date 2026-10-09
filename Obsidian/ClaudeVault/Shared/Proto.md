# BarkFluff.Proto

Protobuf wire contracts лежат в `Shared/BarkFluff.Proto/`. .NET 10 stubs генерируются `Grpc.Tools` в service projects; прямой `BarkFluff.Proto.csproj` включает генерацию для federation contracts. Карта актуальных файлов, namespaces и RPC: [[Shared/Proto-ProjectMap]].

Контрактный инвариант: не менять уже опубликованные field numbers, типы и семантику; новые поля добавлять с новыми номерами. Авторизация определяется кодом конкретного host/service, а не именами `Api` и `ServerApi`; см. [[Backend/GrpcServer]].

## Identity и Settings

`identity_api.proto` содержит legacy Auth/OTP/password/session RPC и challenge-based потоки одновременно. `OtpTypeId.Telegram=3` добавлен без перенумерации `Authenticator=1` и `Email=2`. `AuthRequest` и `FindByLoginRequest` задают username ИЛИ email через `oneof login`. `CreateTokenResponse.access_token` — field 2. `AuthChallengeReference` состоит из `id` и secret capability; не подменять её Telegram deep-link данными. `AuthChallengeResponse.error_code=8` может вернуть `login_mode_disabled`; для неизвестного/недоступного Telegram login возвращается одинаковый ожидающий challenge. `CompleteAuthChallengeResponse` выдаёт session для sign-in/registration и scoped `security_proof` для reauthentication/password recovery; коды восстановления появляются только в ответах, где они выпускаются/заменяются.

`configuration_api.proto` сохраняет API Settings, включая историю значений и rollback: rollback восстанавливает выбранный `previous_value` как новую запись истории. Setup идёт через `settings_setup_api.proto`: только в `SETTINGS_SETUP_MODE=true`, каждый вызов содержит `x-settings-setup-token`; secret field values в snapshot не возвращаются.

## Users, Messages и Files

`users_api.proto`: `AddDraftUserRequest.registration_id=5` — idempotency key регистрации; `User.is_bot=12`, `User.uuid=13`; `PrivacySettings.deny_federated_dm=7` — флаг запрета входящих федеративных DM. Публичный профиль имеет `profile_poster_url=7`, `is_bot=8`, `id=9`. Firebase token включает `PushPlatform` (Android/Web), чтобы consumers строили разные payload. Prekey-bundle RPC принадлежат Users и конкретному устройству.

`shared.Message` сохраняет поля `federated_id=9`, `sender_uuid=10`, `federated_read_by=11`, `reply_to=12`, `client_operation_id=13`. `SendMessageRequest.source_id` — `oneof chat_id/user_id`; `client_operation_id=5` связывает retry с одной отправкой, а `shared.Message.client_operation_id=13` возвращает её ID в ответах/history. Файловый `GetUploadUrlRequest.client_operation_id=2` идемпотентен для резерва слота. `ListMessagesRequest.count` устарел; для пагинации используйте `offset_before/offset_after`.

В `OutgoingMessage` старое `forwarded_message_id=3` оставлено для совместимости как forward; новые поля — `reply_to_message_id=4` и `forwarded_message_ids=5` (до 20, порядок сохраняется). Не смешивать старое поле с 4/5: сервер возвращает InvalidArgument. Reply `Message.reply_to=12` разрешается из живого оригинала при каждой выдаче; forward в `MessageAttachment.forwarded_message=10` — снимок. `FORWARDED_MESSAGE=8` исключается из списка медиа-вложений при пустом фильтре.

`SearchMessages` ищет обычные сообщения по тексту, автору (локальный ID или UUID), полуоткрытому диапазону времени `[sent_from, sent_before)`, вложениям и cursor `(sent_at,message_id)`; последний ответ не содержит `next_cursor`. `MessageAttachmentType` определён в `shared.proto`; `UploadFileType` — отдельный enum. Вложение несёт размеры картинки `image_width=8`/`image_height=9`; forwarded snapshot — field 10, `origin_server=11`. `UploadFileInfo` хранит размеры как fields 12/13.

## Чаты, события и данные узла

`ChatType`: regular=0, private=1, secret=2. Private chat передаёт `kdf_salt=10` и `passphrase_verifier=11`; `last_activity_at=14`, `private_invite_state=15`, а `CreatePrivateChatResponse.created=2` различает создание и возврат существующей пары. `EncryptedMessage` хранится отдельно от обычных сообщений и сервер не имеет ключа для расшифровки. `SecretEnvelope` — opaque: Messages буферизует его в Redis до 24 часов, не сохраняет в БД; Updates маршрутизирует secret events по устройству, private events — пользователю.

`updates_api.proto` включает обычные message/read/edit/delete/pin, скрытие чата, private-chat updates и secret-device events; в очередном `MessageReadEvent` `NewReadBy` — полный снимок, `NewReaders` — delta. `calls_api.proto.SubscribeCallEvents` также адресуется устройству.

Поля `beacon_api.proto.GetServerInfoResponse`: `livekit_url=13`, `calls=14`, `bots=15`, `server_name=16`, `federation_enabled=17`, `files_media_endpoint=18`. В `navigator_api.proto.ServerInfo` адрес Web gateway — field 13, отдельный media origin — field 14.

## Federation и боты

Federation contracts активны. `FederationS2SApi` подписывает межнодовые запросы Ed25519, а не XAuth; `FederationInternalApi` — service-to-service API. Internal methods обслуживают Federation, AdminPanel, Files, Users и Onliner. Federation DTO в `messages_api.proto` плоские и не импортируют `federation_api.proto`, сохраняя границу Messages/Federation. `BotsExternalApi` принимает bot JWT через `x-auth-token`; `IdentityServerApi.CreateBotTokenServer/GetBotTokenServer` выпускают token с идентификатором для отзыва.
