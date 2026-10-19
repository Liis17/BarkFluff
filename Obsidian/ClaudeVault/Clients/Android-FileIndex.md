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
| domain/gateway | DomainGateways contracts и GrpcGatewayAdapters production mapping |
| domain/auth, domain/model | Challenge controller и доменные DTO/policies |
| security | TlsTransportFactory, TlsTrustStore, TlsCertificateProbe, endpoint policy |
| data | GlobalParam и локальные настройки сети |
| repository | ChatRepository, PrivateChatRepository, SecretChatRepository |
| crypto | PrivateChatCrypto, BarkFluffSignalStore, PrekeyManager |
| calls | CallsApi repository/event service |
| src/main/proto | Beacon, Navigator, Identity, Users, Files, Messages, Updates, Calls и общие типы |

## :app-v1

| Пакет / вход | Назначение |
|---|---|
| Корень package com.barkfluff.client | Activities/Fragments и composition root BarkFluffApplication |
| di/AppModule.kt | Hilt wiring для transport, gateways, cache и фоновых сервисов |
| ChatViewModel.kt, ChatStateModules.kt | State/effect/intent и reducers обычного чата |
| chat | RegularChatSession, MessageNavigator и timeline coordination |
| adapter | MessageRowProjector, MessageAdapter и UI lists |
| cache | SQLCipher Room v5, outbox records и migration |
| drafts | ChatDraftRepository и ComposerAttachmentStore |
| send | OutgoingMessageQueue, OutgoingMessageWorker и retry policy |
| auth | Activity-retained challenge state и диалог |
| search | Compose user/chat/message search и view models |
| notifications | FCM service, NotificationHelper, receivers, realtime side effects |
| calls | LiveKit engine, call screens, Telecom bridge, foreground service |
| voice, audio | Voice capture, playback, waveform and MediaSession |
| share | ACTION_SEND receiver and confirmation |
| widget | Pinned chats App Widget |
| editor | Image drawing and video trimming screens |

## Проверки

| Тип | Путь | Примеры покрытия |
|---|---|---|
| :core unit | Android/core/src/test | ClientSlotRegistry, token refresh, endpoint/hostname/pin checks, auth challenge, download bounds |
| :core variant unit | Android/core/src/testDebug, Android/core/src/testRelease | TLS cleartext and variant policy |
| :core instrumentation | Android/core/src/androidTest | Registry/settings storage |
| :app unit | Android/Barkfluff.Client.Android/app/src/test | ChatStateModules, ChatDraftJournal, MessageRowProjector, MessageNavigator, search, retry, notification policies |
| :app instrumentation | Android/Barkfluff.Client.Android/app/src/androidTest | SQLCipher migration/outbox, notification actions, voice, media, Compose UI, Markdown |

Representative test files:
- Android/core/src/test/java/com/barkfluff/client/grpc/ClientSlotRegistryTest.kt
- Android/core/src/test/java/com/barkfluff/client/grpc/GrpcTokenCoordinatorTest.kt
- Android/core/src/test/java/com/barkfluff/client/security/PinnedTrustManagerTest.kt
- Android/Barkfluff.Client.Android/app/src/test/java/com/barkfluff/client/ChatStateModulesTest.kt
- Android/Barkfluff.Client.Android/app/src/test/java/com/barkfluff/client/drafts/ChatDraftJournalTest.kt
- Android/Barkfluff.Client.Android/app/src/androidTest/java/com/barkfluff/client/cache/OutgoingMessageMigrationTest.kt

Локальный UI-resource checker: Android/tools/check_android_ui.py. Полный актуальный список файлов можно получить командой rg --files Android; каталог не должен превращаться в копию этого вывода.
