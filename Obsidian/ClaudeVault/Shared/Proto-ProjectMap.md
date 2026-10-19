# BarkFluff.Proto — карта контрактов

Исходники: `Shared/BarkFluff.Proto/` (`net10.0`). Это набор protobuf wire contracts; рукописного C# нет. `Grpc.Tools` генерирует stubs в consumer projects. `BarkFluff.Proto.csproj` также напрямую codegen-ит `federation_api.proto` и `federation_internal_api.proto`; актуальные consumers указаны в соответствующих backend `.csproj`.

Имена RPC перечислены по текущим `.proto`. Граница аутентификации определяется host/interceptors/policies в сервисе: суффиксы `Api` и `ServerApi` сами по себе не гарантируют тип токена. См. [[Backend/GrpcServer]] и [[Shared/Proto]].

## `beacon_api.proto` — `BarkFluff.Proto.Beacon`

- `BeaconApi`: `GetServerInfo`.

## `bots_api.proto` — `BarkFluff.Proto.Bots`

- `BotsServerApi`: `CreateSystemBot`, `ListBots`, `UpdateBotProfile`, `GetBotToken`, `DeleteBot`, `RegenerateToken`.

- `BotsExternalApi`: `GetMe`, `SendMessage`, `GetUserInfo`, `GetFile`, `EditMessage`, `DeleteMessage`, `SetMyCommands`, `GetMyCommands`, `SubscribeUpdates`.

## `calls_api.proto` — `BarkFluff.Proto.Calls`

- `CallsApi`: `InitiateCall`, `JoinCall`, `AcceptCall`, `RejectCall`, `EndCall`, `SetCallAudioQuality`, `SubscribeCallEvents`, `ListCallHistory`, `GetActiveCalls`.

## `configuration_api.proto` — `BarkFluff.Proto.Configuration`

- `ConfigurationApi`: `GetConfiguration`, `GetAllConfigurations`, `UpdateConfiguration`, `GetConfigurationHistory`, `RollbackConfiguration`, `GetReservedNames`, `AddReservedName`, `UpdateReservedName`, `DeleteReservedName`.

## `developers_api.proto` — `BarkFluff.Proto.Developers`

- `DevelopersApi`: `GetDocumentationSections`, `GetDocumentationSection`, `GetProtoFiles`, `GetProtoFileContent`, `GetErrorCodes`.

## `fast_auth_api.proto` — `BarkFluff.Proto.FastAuth`

- `FastAuthApi`: `GenerateFastAuthToken`, `SubscribeFastAuthResult`, `ScanFastAuth`, `AcceptFastAuth`, `RejectFastAuth`.

- `FastAuthServerApi`: `GetFastAuthInfo`.

## `federation_api.proto` — `BarkFluff.Proto.Federation`

- `FederationS2SApi`: `Ping`, `GetServerKeys`, `GetUserProfile`, `DeliverEvents`, `FetchChatHistory`, `FetchFile`, `SubscribePresence`, `DeliverTyping`.

## `federation_internal_api.proto` — `BarkFluff.Proto.FederationInternal`

- `FederationInternalApi`: `ResolveRemoteUser`, `FetchRemoteFile`, `FetchRemoteChatHistory`, `GetKnownServers`, `UpsertManualPeer`, `SetServerBlocked`, `GetFederationStatus`, `RotateSigningKey`, `EnqueueOutbound`, `SetPresenceInterest`, `DeliverTypingOutbound`.

## `files_api.proto` — `BarkFluff.Proto.Files`

- `FilesApi`: `GetUploadUrl`, `GetTempDownloadUrl`, `CheckFileHash`, `GetUserStorageInfo`, `ListStickerPacks`, `GetStickerPack`, `GetStickerPackByFile`.

- `FilesServerApi`: `GetFileData`, `GetFilesData`, `UploadBadgeImage`, `GetUserStorageInfoServer`, `UploadAvatarServer`, `UploadPosterServer`, `UploadFileServer`, `GetTempDownloadUrlServer`, `CreateStickerPack`, `UpdateStickerPack`, `DeleteStickerPack`, `ListStickerPacks`, `GetStickerPack`, `AddSticker`, `RemoveSticker`, `UpdateSticker`, `GetStickers`, `UploadStickerImage`, `FetchFileStream`, `CheckFedAvatarAccess`.

## `identity_api.proto` — `BarkFluff.Proto.Identity`

- `IdentityApi`: `Auth`, `FastAuth`, `CreateToken`, `CreateAccount`, `ConfirmAccount`, `GetActiveSessions`, `RemoveActiveSession`, `EnableOtpVerification`, `ConfirmOtpVerification`, `DisableOtpVerification`, `ListOtpVerification`, `ResetPassword`, `ConfirmResetPassword`, `SetPassword`, `Logout`, `GetAuthCapabilities`, `BeginRegistration`, `BeginSignIn`, `GetAuthChallenge`, `CompleteAuthChallenge`, `CancelAuthChallenge`, `ResendAuthChallenge`, `GetSecuritySettings`, `GetLoginNotificationSettings`, `SetLoginNotificationChannel`, `BeginReauthentication`, `UpdateSecuritySettings`, `BeginTelegramBinding`, `UnlinkTelegram`, `BeginEmailBinding`, `GenerateRecoveryCodes`, `BeginPasswordRecovery`, `SetRecoveredPassword`.

- `IdentityServerApi`: `ListOtpVerificationServer`, `DisableOtpVerificationServer`, `GetActiveSessionsServer`, `RemoveActiveSessionServer`, `CreateSessionForUserServer`, `ForceSetPasswordServer`, `CreateBotTokenServer`, `GetBotTokenServer`.

## `messages_api.proto` — `BarkFluff.Proto.Messages`

- `MessagesApi`: `ListChats`, `ListMessages`, `SearchMessages`, `ListChatMembers`, `SendMessage`, `CreateGroupChat`, `KickUser`, `AddUser`, `DeleteChat`, `LeaveChat`, `UpdateGroupChat`, `MarkAsRead`, `ListChatAttachments`, `GetPersonChatId`, `GetChatInfo`, `GetChatDraft`, `UpsertChatDraft`, `DeleteChatDraft`, `EditMessage`, `DeleteMessage`, `PinMessage`, `UnpinMessage`, `ListPinnedMessages`, `UnpinAll`, `CreatePrivateChat`, `AcceptPrivateChat`, `RejectPrivateChat`, `SendPrivateMessage`, `ListPrivateMessages`, `EditPrivateMessage`, `DeletePrivateMessage`, `MarkPrivateMessagesAsRead`, `SendSecretChatInvite`, `AcceptSecretChatInvite`, `RejectSecretChatInvite`, `SendSecretMessage`, `AckSecretMessage`.

- `MessagesServerApi`: `GetUserAllMessages`, `CheckChatMembership`, `GetChatMemberIds`, `GetChatInfoServer`, `PostCallSystemMessage`, `SendMessageServer`, `EditMessageServer`, `DeleteMessageServer`, `ImportFederatedChat`, `ImportFederatedMessage`, `ApplyFederatedEdit`, `ApplyFederatedDelete`, `ApplyFederatedRead`, `ExportChatEvents`, `CheckFileFederationAccess`, `CheckFederatedPresenceAccess`, `CheckFedFileUserAccess`.

## `navigator_api.proto` — `BarkFluff.Proto.Navigator`

- `NavigatorApi`: `ListServers`, `RegisterServer`, `GetServerByName`.

## `onliner_api.proto` — `BarkFluff.Proto.Onliner`

- `OnlinerApi`: `SubscribeToOnlineStatus`, `SetOnlineStatus`, `GetOnlineStatus`, `ChangeUsersInSubscription`, `SetTypingStatus`, `SubscribeToTyping`, `ChangeChatsInTypingSubscription`.

- `OnlinerServerApi`: `UpsertRemoteStatus`, `InjectRemoteTyping`, `GetLocalPresence`.

## `settings_setup_api.proto` — `BarkFluff.Proto.SettingsSetup`

- `SettingsSetupApi`: `GetSetupState`, `SaveSetupGroup`, `CompleteSetup`.

## `shared.proto` — `BarkFluff.Proto.Shared`
Общие типы без RPC.

## `updates_api.proto` — `BarkFluff.Proto.Updates`

- `UpdatesApi`: `SubscribeNewMessages`, `SubscribeMessagesRead`, `SubscribeMessagesEdited`, `SubscribeMessagesDeleted`, `SubscribeMessagesPinned`, `SubscribeMessagesUnpinned`, `SubscribeAllMessagesUnpinned`, `SubscribeChatHidden`, `SubscribePrivateMessages`, `SubscribePrivateMessageEdits`, `SubscribePrivateMessageDeletes`, `SubscribePrivateMessagesRead`, `SubscribePrivateChatInvites`, `SubscribePrivateChatInviteResolutions`, `SubscribeSecretChatInvites`, `SubscribeSecretChatResolutions`, `SubscribeSecretMessages`.

## `users_api.proto` — `BarkFluff.Proto.Users`

- `UsersApi`: `GetUser`, `SetProfilePicture`, `CheckExistUsername`, `CheckExistEmail`, `ChangeName`, `ChangeUsername`, `ChangeBio`, `SearchUsers`, `ResolveFederatedUser`, `GetUserBadges`, `GetDevices`, `GetCurrentDevice`, `RenameDevice`, `SetFirebaseToken`, `ClearFirebaseToken`, `SetNotificationsEnabled`, `SetChatMuted`, `GetMutedChats`, `GetPrivacySettings`, `UpdatePrivacySettings`, `AcceptLegalConsent`, `GetPersonalization`, `UpdatePersonalization`, `GetUserSettings`, `SetGlobalChatBackground`, `SetChatBackground`, `GetProfilePoster`, `SetProfilePoster`, `GetChatFolders`, `CreateChatFolder`, `UpdateChatFolder`, `DeleteChatFolder`, `AddChatToFolder`, `RemoveChatFromFolder`, `ReorderChatFolders`, `RegisterPrekeyBundle`, `FetchPrekeyBundle`, `ListPeerDevices`, `ReplenishOneTimePrekeys`, `RotateSignedPrekey`.

- `UsersServerApi`: `SetVerifiedEmail`, `FindByLogin`, `CheckExistUsername`, `CheckExistEmail`, `AddDraftUser`, `OverrideDraftUser`, `ConfirmUser`, `GetById`, `GetUserContacts`, `ListByIds`, `AssignUserBadge`, `RemoveUserBadge`, `UpdateUserBadgePriority`, `CreateBadge`, `GetAllBadges`, `UpdateBadge`, `DeleteBadge`, `ExportData`, `RegisterDevice`, `GetUserDevices`, `DeleteUserDevice`, `UpdateDeviceAppInfo`, `GetUserByUsername`, `SearchUsersServer`, `UpdateStorageLimit`, `SetProfilePictureServer`, `GetDevicesWithFirebaseTokens`, `GetDevicesWithFirebaseTokensByDeviceIds`, `GetAllDevicesWithFirebaseTokens`, `GetUserPrivacy`, `GetMutedChatIds`, `UpdateProfileServer`, `SetProfilePosterServer`, `GetProfilePosterServer`, `CreateBotUser`, `DeleteBotUser`, `UpsertRemoteUsers`, `GetUsersByUuid`, `GetFederatedProfile`, `IsAvatarVisibleToFederation`, `CheckRemoteAvatarRef`.

## Подключение

- `Backend/BarkFluff.Federation/BarkFluff.Federation.csproj` компилирует S2S API (Both) и Internal API (Server). `Backend/Barkfluff.AdminPanel/`, `Backend/BarkFluff.Files/`, `Backend/BarkFluff.Onliner/` и `Backend/BarkFluff.Users/` включают Internal API как Client, а S2S types — как None; `Backend/dev-federation-testbed/fedping/` использует S2S Client.
- Остальные contracts включаются напрямую в service projects с `GrpcServices=Server`, `Client` или `None` в зависимости от роли.
- Основные field-number invariants, idempotency tokens и data-scoping правила сведены в [[Shared/Proto]].
