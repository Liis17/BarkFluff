# BarkFluff.Shared.Exceptions — реестр ошибок

Исходники: `Shared/BarkFluff.Shared.Exceptions/`. Публичный `ErrorCode` уходит в gRPC trailer `x-error-code`; `ErrorMessage` — в gRPC detail. Сохраняйте коды как wire contract. Базовые значения `BaseGrpcException`: `BDF4009D-24D0-4E0C-A10C-AEF33E0D0022`, «Неизвестная ошибка», `FailedPrecondition`.

На unary RPC `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` сериализует `BaseGrpcException`; client `ExceptionClientInterceptor` сопоставляет trailer с классом по коду. Для этого класс ошибки должен иметь parameterless constructor. Несовпавший код остаётся `RpcException`; client interceptor оборачивает только async unary calls.

Ниже — текущие коды, взятые из деклараций наследников `BaseGrpcException`. Статус по умолчанию — `FailedPrecondition`; исключения с override перечислены после таблиц.

## Bots

| Исключение | `ErrorCode` |
|---|---|
| `BotNotFoundException` | `4F8A2D1C-9B3E-47A6-8C5D-1E7F0B2A9D34` |
| `BotPollingConflictException` | `79870B49-B14A-43D6-B693-A767F8F3ECAF` |
| `BotStorageQuotaExceededException` | `DFB49E4B-1933-4FD4-9C5B-74D3AA4E67AF` |
| `NotValidBotUserIdException` | `68D641B8-A231-40C3-AFAB-7ED0C08D0BD3` |

## FastAuth

| Исключение | `ErrorCode` |
|---|---|
| `FastAuthInvalidConfirmationCodeException` | `7B3F8E92-5D14-4C68-A7E2-1F9B6D3C8A45` |
| `FastAuthInvalidStateException` | `3C8A1E5F-7D29-4B83-9E16-5A2C8F4B7D31` |
| `FastAuthSessionExpiredException` | `D2F71E8A-3C5B-4197-8A6D-4E9B27C5F1A8` |
| `FastAuthSessionNotFoundException` | `A5E94C7D-1B82-4F36-9CDE-78B1F4A7E2C5` |

## Federation

| Исключение | `ErrorCode` |
|---|---|
| `ClockSkewDetectedException` | `B29A587C-095D-436D-BFDC-FD94D7203C23` |
| `FederationNotConfiguredException` | `AC6E038B-96E8-417A-A91B-5DEF9E1ADB4D` |
| `FederationPeerNotFoundException` | `52EF0116-D1CB-4CDD-A5DD-99DD900D729B` |
| `FederationServerBlockedException` | `213152EB-1B6C-40C5-BA1D-0FD88DF1752A` |
| `InvalidServernameException` | `BD255D17-12CB-4D02-A26A-43F3E876C7D9` |
| `XFedUnauthenticatedException` | `34AD5E00-4852-435F-89B0-96B6CE99834C` |

## Files

| Исключение | `ErrorCode` |
|---|---|
| `ClientOperationIdNotValidException` | `670F9884-187C-444B-873B-1B7FB00E3DD7` |
| `FileNotFoundException` | `91E25C73-FC80-43C1-893D-F26F39726F03` |
| `NotValidFileIdException` | `D10BD126-48EA-4D11-9CFF-4C2FDD6F9899` |
| `UploadOperationTypeMismatchException` | `4FCAC2EF-4915-43D6-BB2B-74B29387858F` |

## Identity

| Исключение | `ErrorCode` |
|---|---|
| `ConfirmationCodeExpiredException` | `7AABF347-1210-4B14-A93B-2BA8574D74E7` |
| `ConfirmationCodeIncorrectException` | `4396D597-D605-4040-AF0F-D9168F0CA034` |
| `ConfirmationCodeNotFoundException` | `56D9BB63-DA40-40DE-9C56-7487A1A437D0` |
| `EmailExistException` | `7599F3F1-C2EC-4D05-BF38-A1A60D40BA4E` |
| `IdentityLockoutException` | `B95A5B58-6A7F-43A2-A9B9-D9D8F8B4B1E4` |
| `IdentityProtectionUnavailableException` | `A7B3D2F1-4C6E-4E6D-8A6B-2F0A9C7D5E11` |
| `IdentityRateLimitExceededException` | `7D1CBF0E-2C85-4A2A-9B2D-6B2A6CF5A1E2` |
| `InvalidLoginOrPasswordException` | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |
| `InvalidOldPasswordException` | `A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04` |
| `InvalidRefreshTokenException` | `7E6A31C5-3C4D-412E-87BC-0A387617A5D3` |
| `NotSetUsernameOrEmailException` | `55872FA3-4F77-4C5A-B471-C25699BA20C0` |
| `NotValidOtpCodeException` | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| `OtpCodeNeedException` | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| `OtpNotCreatedException` | `A0D92E59-DC33-4072-BFDE-12E7E26FAAD0` |
| `ResetIdExpiredException` | `9F3D1B82-8E55-4C71-BD2A-3D7FAC2E6AE1` |
| `ResetIdHasIsApprovedException` | `BE708516-BF40-44F9-A6D1-A7F30AB02BED` |
| `ResetIdNotFoundException` | `5B9A8269-617E-4D4C-9696-A554C59E3A86` |
| `SessionNotFoundException` | `011BF29A-2DE6-4A63-BF8D-3F36AE730D9D` |
| `UserNotFoundException` | `A4DAB334-1067-4838-A782-C4257DC838F7` |
| `UsernameBotSuffixReservedException` | `B0741D3A-6C2E-4E9F-9A1B-2F5C7D8E0A11` |
| `UsernameExistException` | `DB157CD8-98A3-4A35-9857-33821813D422` |
| `UsernameInvalidFormatException` | `E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47` |
| `UsernameOrEmailIsEmptyException` | `84EC96DA-2A1A-499E-ACED-C1444E07E0E6` |
| `UsernameReservedException` | `A3F1B2C4-7D8E-4F5A-9B6C-1E2D3F4A5B6C` |
| `XAppInfoIsRequiedException` | `FFE79950-5668-4786-A834-6B490650FE62` |
| `XDeviceNameIsRequiredException` | `4E98408C-C969-4737-936B-A2AABB05B88D` |
| `XOsNameIsRequiredException` | `575EBB8D-5687-40F5-BFBB-93CD46D7564B` |

## Messages

| Исключение | `ErrorCode` |
|---|---|
| `ChatIdNotValidException` | `91CB4758-151F-4BF7-8C6D-435923CDF1AF` |
| `ChatNotFoundException` | `7506386A-8940-4F3B-87B8-315DD0A7AB08` |
| `ChatNotPrivateException` | `9F8E2C84-3B4D-4A7A-8F1C-5B2C0E77AC11` |
| `ChatNotRegularException` | `CF4654C7-856B-480A-BCE1-8A76B1C328C8` |
| `ChatTypeMismatchException` | `D0AC27C0-3381-401D-8EF4-E35610C372AB` |
| `ChatUnknownException` | `D4B6F9A3-2C8E-4F7D-BD3A-4C9E2F6A3D04` |
| `ClientOperationIdNotValidException` | `8C426DE2-CB07-485B-A80E-7C9C866E8AE4` |
| `ConflictingForwardFieldsException` | `8F2D6A05-91C4-4B7E-A3D8-6E0B9C5F1A72` |
| `DeviceIdRequiredException` | `8E0F3D90-6F4B-4D4D-B5C8-3F6E1D2A0B43` |
| `DuplicateFederatedDmException` | `C3A5E8F2-1B7D-4E6F-AC29-3B8D1E5F2C03` |
| `EncryptedMessageNotFoundException` | `FA3C1B6E-5D9F-4A48-AB12-DD3F62E3C481` |
| `FederatedAttachmentInvalidException` | `C2F7A93E-5B41-4D8A-9E63-1F0D7B2C4A55` |
| `FederatedChatNotActiveException` | `C42639DE-4619-48BE-ADE4-EA9B5DEA7E43` |
| `FederatedDmRejectedException` | `FederatedDmRejected` |
| `FederatedForwardInvalidException` | `5D3E8B1C-46A7-42F9-8C05-B7E29D4A6013` |
| `FederatedGroupsNotSupported` | `F6D8B1C5-4EAF-4F9D-DF5C-6EB1A8C5F06` |
| `FederatedMessageUnknownException` | `E5C7A0B4-3D9F-4F8E-CE4B-5DA0F7B4E05` |
| `FederatedOriginMismatchException` | `F1D8B2C6-4E0A-4B9F-9D1E-6C2A8F0B3E06` |
| `FileHasNotGroupPictureTypeException` | `1ED8FF46-1CD7-4EFA-9CDD-8A07D18B2EE8` |
| `FileNotSupportedException` | `DE755405-706A-4471-B3CA-5E0A3DCF8566` |
| `GroupChatTitleIsEmptyException` | `0071F324-D75B-4AE9-93B4-BA62BD61AAEF` |
| `GroupChatUsersIsEmptyException` | `DB4FBD5D-30D5-44F6-B0AD-5AF13239523F` |
| `InvalidEncryptedPayloadException` | `9C82E2A7-5E2C-49EA-B12E-A1F70E64D3C7` |
| `IsNotGroupChatException` | `DF5F9672-E0D6-4D6D-AC68-9D0B666ADD1E` |
| `MessageNotContainContextException` | `C45A0486-AEBE-4A7F-B9BD-42BCEA4F843F` |
| `MessageNotFoundException` | `C0EEF1D9-BE99-4645-9EBD-95FF36A2BF45` |
| `MessageTextTooLongException` | `9F8B5C2A-7F1D-4E5A-9C3B-1F0E2D4A8B6C` |
| `NoAccessToChatException` | `604DD334-0484-4C6B-8113-354B9D2FDF2A` |
| `NoPermissionException` | `AD582481-BAA9-4715-B3B9-825C886DFEC3` |
| `PrivateChatAlreadyAcceptedException` | `5E2D3B6F-7C89-4D2A-8C71-44E6A12F0C20` |
| `PrivateChatInviteNotFoundException` | `7B19D8C2-1E2F-4F90-9F13-DCE8CC1A7F22` |
| `RemoteProfileRejectedException` | `B8FAD4E7-6AB1-4FBA-F17E-80D3CA0EB08` |
| `RemoteUserNotResolvedException` | `A7E9C3D6-5FA0-4FAF-E06D-7FC2B9D6A07` |
| `SecretInviteNotFoundException` | `B12F7A45-3D8E-4B27-AE33-1C5E0F4F2A8E` |
| `SourceForSendMessageNotSetException` | `2E70077F-D3C6-41A4-9D4D-49A0004CD54D` |
| `TimestampInFutureException` | `E1A8B514-3C4E-4F2B-9A1D-7C5E2B8F1A01` |
| `TooManyAttachmentsException` | `B3A4D7F2-5C6E-4A8B-9D1F-3E2C7B8A0F4D` |
| `TooManyForwardedMessagesException` | `C4B9E2D1-7A63-4E58-9F0C-2D8B5A1E6047` |
| `TooManyPinnedMessagesException` | `F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23` |
| `UnknownInviteeException` | `B2F4D7E1-9A6C-4D3E-8B15-2A7C9F4D1B02` |
| `UserAlreadyMemberChatException` | `7D3F0A52-8C41-4E96-9B0D-2E5F8A1C4B77` |
| `UserNotMemberChatException` | `CA1008EF-9487-4E37-A74A-C9B921F1D6CE` |

## Navigator

| Исключение | `ErrorCode` |
|---|---|
| `BeaconHostEmptyException` | `8BD06066-81A5-43BF-84B6-A4112775E124` |
| `BeaconPortEmptyException` | `F6E5D4C3-B2A1-4C5D-8B7A-9E0F1A2B3C4D` |
| `FederationRegistrationRejectedException` | `1CD8A150-5943-4C72-8AA5-7829C72823D1` |
| `InvalidBeaconHostException` | `B7C4D8E2-3F1A-4D6B-9C7E-2A8B5D6F1C3E` |
| `InvalidFilesMediaEndpointException` | `3B6C0D18-7E52-4A91-8C4F-2D5B9A17E604` |
| `InvalidHexColorException` | `E1F2A3B4-5C6D-4E7F-8A9B-0C1D2E3F4A5B` |
| `InvalidWebEndpointException` | `5D9E1F42-8A3C-4B7E-9F26-C1D0A4B85E37` |
| `NameEmptyException` | `1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D` |

## Users

| Исключение | `ErrorCode` |
|---|---|
| `BioTooLongException` | `1A652492-87A4-4B8B-B758-E7FBE1F39DDF` |
| `ChatFolderInvalidNameException` | `8C1A6F4D-1B22-4E1B-8E4D-7E9A5B6C2A11` |
| `ChatFolderNotFoundException` | `5F0B7B2E-3F6E-4D2B-9B9E-9B7E7C2B9D8A` |
| `ProfilePictureHasNotValidType` | `7097703F-977C-4E28-8C85-1A287B3FF8AD` |
| `UserIsDraftException` | `91D75288-8314-4658-AA0E-EC1D01779D58` |

## Особые gRPC-статусы

- `ClockSkewDetectedException` — `Unauthenticated`
- `FederationPeerNotFoundException` — `NotFound`
- `FederationServerBlockedException` — `PermissionDenied`
- `InvalidServernameException` — `InvalidArgument`
- `XFedUnauthenticatedException` — `Unauthenticated`
- `IdentityLockoutException` — `ResourceExhausted`
- `IdentityProtectionUnavailableException` — `Unavailable`
- `IdentityRateLimitExceededException` — `ResourceExhausted`
- `ChatUnknownException` — `NotFound`
- `ConflictingForwardFieldsException` — `InvalidArgument`
- `FederatedMessageUnknownException` — `NotFound`

- `FederatedDmRejectedException` использует строковый код `FederatedDmRejected`, а не GUID.
- В `Identity/InvalidRefreshTokenException.cs` `ErrorMessage` равен имени класса; у `Messages/GroupChatTitleIsEmptyException.cs` не переопределено сообщение, поэтому действует базовое.
