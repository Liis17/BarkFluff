# Android — указатель по исходникам

Parent: [[Clients/Android]]

Индекс помогает перейти к нужной области, а не пересказывает каждый класс и helper. Актуальные потоки — [[Clients/Android-ProjectMap|Android-ProjectMap]]; поведение и ограничения — [[Clients/Android]]. Исходники и конфигурация — источник истины. Пакеты ниже указаны относительно Android/core/src/main/java/com/barkfluff/client/ и Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/.

## Модули и конфигурация

| Область | Основные пути |
|---|---|
| Gradle root | Android/settings.gradle.kts, Android/build.gradle.kts, Android/gradle/libs.versions.toml |
| V1 UI | Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client |
| Shared Android core | Android/core/src/main/java/com/barkfluff/client |
| Android manifest/resources | Android/Barkfluff.Client.Android/app/src/main/AndroidManifest.xml, Android/Barkfluff.Client.Android/app/src/main/res |
| RPC schemas | Android/core/src/main/proto |
| App Room schemas | Android/Barkfluff.Client.Android/app/schemas |

## :core

| Пакет / вход | Назначение |
|---|---|
| grpc | GrpcClientRegistry, GrpcApiTransport, TokenCoordinator, MediaHttpTransport, RealtimeService |
| domain/gateway | DomainGateways contracts, включая UserBadgeGateway, и GrpcGatewayAdapters production mapping |
| domain/auth, domain/model | Challenge controller, AuthenticationChallengeModels, AccountStatus и UserBadgeInfo с маппингом серверных статусов |
| security | TlsTransportFactory и TlsCallFactory, TlsTrustStore, TlsCertificateProbe, endpoint policy |
| bft | BftProxyManager, BftServiceRouter, BftReconnectPolicy, BftTunnel и виртуальные сокеты |
| data | GlobalParam, включая стабильные socket* endpoints и BFT service map |
| repository | ChatRepository, PrivateChatRepository, SecretChatRepository |
| crypto | PrivateChatCrypto, BarkFluffSignalStore, PrekeyManager |
| calls | CallsApi repository/event service |
| src/main/proto | Beacon, Navigator, Identity, Users, Files, Messages, Updates, Calls и общие типы |

## :app-v1

| Пакет / вход | Назначение |
|---|---|
| Корень package com.barkfluff.client | Activities/Fragments, DeepLinkActivity/BftProxyLinkImporter, ProxySettingsActivity и composition root BarkFluffApplication |
| di/AppModule.kt | Hilt wiring для transport, gateways, cache и фоновых сервисов |
| ChatViewModel.kt, ChatStateModules.kt | State/effect/intent и reducers обычного чата |
| chat | RegularChatSession, MessageNavigator и timeline coordination |
| adapter | MessageRowProjector, MessageAdapter и UI lists |
| cache | SQLCipher Room v8, статус peer в cached_chat_displays, UserBadgeRepository, migrations 6→7→8, outbox records и выборочная очистка (ClearCacheOptions, clearHistoryCache/clearSecretHistory/clearOutgoingKeepReads, compact) |
| badges | BadgeViewerSheet, BadgeIconAdapter, BadgeGridAdapter и Coil image loading для бейджей |
| drafts | ChatDraftRepository и ComposerAttachmentStore |
| send | OutgoingMessageQueue, OutgoingMessageWorker и retry policy; GrpcOutgoingFailure помечает UserUnavailable постоянной ошибкой |
| auth | Activity-retained challenge state и диалог; AccountRestrictionUi обрабатывает статус входа и завершение сессии |
| search | Compose user/chat/message search и view models; ограниченные профили локально маскируются |
| notifications | FCM service, NotificationHelper, receivers, realtime side effects |
| calls | LiveKit engine, call screens, Telecom bridge, foreground service |
| voice, audio | Voice capture, playback, waveform and MediaSession |
| share | ACTION_SEND receiver and confirmation |
| widget | Pinned chats App Widget |
| editor | Image drawing and video trimming screens |

## Проверки

| Тип | Путь | Примеры покрытия |
|---|---|---|
| :core unit | Android/core/src/test | ClientSlotRegistry, BFT routing/reconnect/Exit interop, token refresh, endpoint/hostname/pin checks, auth challenge, download bounds |
| :core variant unit | Android/core/src/testDebug, Android/core/src/testRelease | TLS cleartext and variant policy |
| :core instrumentation | Android/core/src/androidTest | Registry/settings storage and production BFT tunnel transports |
| :app unit | Android/Barkfluff.Client.Android/app/src/test | ChatStateModules, ChatDraftJournal, MessageRowProjector, MessageNavigator, search, retry, notification policies |
| :app instrumentation | Android/Barkfluff.Client.Android/app/src/androidTest | SQLCipher migration/outbox, notification actions, voice, media, Compose UI, Markdown |

Representative test files:
- Android/core/src/test/java/com/barkfluff/client/grpc/ClientSlotRegistryTest.kt
- Android/core/src/test/java/com/barkfluff/client/grpc/GrpcTokenCoordinatorTest.kt
- Android/core/src/test/java/com/barkfluff/client/security/PinnedTrustManagerTest.kt
- Android/core/src/test/java/com/barkfluff/client/domain/model/UserBadgeInfoTest.kt
- Android/Barkfluff.Client.Android/app/src/test/java/com/barkfluff/client/ChatStateModulesTest.kt
- Android/Barkfluff.Client.Android/app/src/test/java/com/barkfluff/client/drafts/ChatDraftJournalTest.kt
- Android/Barkfluff.Client.Android/app/src/androidTest/java/com/barkfluff/client/cache/OutgoingMessageMigrationTest.kt
- Android/Barkfluff.Client.Android/app/src/androidTest/java/com/barkfluff/client/cache/UserBadgeMigrationTest.kt
- Android/Barkfluff.Client.Android/app/src/androidTest/java/com/barkfluff/client/cache/ChatCacheRepositoryClearTest.kt

Локальный UI-resource checker: Android/tools/check_android_ui.py. Полный актуальный список файлов можно получить командой rg --files Android; каталог не должен превращаться в копию этого вывода.
