# Android — карта проекта

Parent: [[Clients/Android]]

Эта заметка показывает, как связаны основные точки входа и модули. Обзор контрактов и инвариантов — [[Clients/Android]]; перечень областей кода и тестов — [[Clients/Android-FileIndex|Android-FileIndex]].

Для краткости app/… обозначает Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/…, core/… — Android/core/src/main/java/com/barkfluff/client/…. Пути Gradle, manifest, resources и proto указаны от корня репозитория.

## Gradle-модули

| Путь | Ответственность |
|---|---|
| Android/settings.gradle.kts | Единственный Gradle root; подключает :core и :app-v1 |
| Android/core | gRPC/proto, gateway contracts/adapters, security, repositories и общие data utilities |
| Android/Barkfluff.Client.Android/app | V1: UI, Hilt composition root, Room cache/outbox, notifications, widgets, calls orchestration |

## Потоки

### Запуск и выбор ноды

AndroidManifest.xml → SplashActivity → WelcomeActivity при первом запуске или SelectServerActivity → LoginActivity. При наличии сессии переход идёт в MainActivity. SelectServerActivity использует ServerDiscoveryGateway: Navigator возвращает каталог и Files media origin; Beacon возвращает service endpoints. ServerInfoPrefs.applyServerInfo сохраняет актуальную конфигурацию в GlobalParam.

Основные исходники: Android/Barkfluff.Client.Android/app/src/main/AndroidManifest.xml, app/SplashActivity.kt, app/SelectServerActivity.kt, app/LoginActivity.kt, app/utils/ServerInfoPrefs.kt, core/domain/gateway/DomainGateways.kt.

### RPC и TLS

Activities/ViewModels → typed gateway → production gateway adapter → GrpcApiTransport / repository → GrpcClientRegistry → gRPC stub. RPC registry не передаётся UI. RealtimeService использует registry для стримов и публикует events через RealtimeGateway; app-слой подключает NotificationHelper и WidgetUpdater через RealtimeSideEffects.

MediaHttpTransport обрабатывает Files upload/download отдельно от gRPC. TLS политика сосредоточена в core/security/TlsTransportFactory.kt, core/security/TlsTrustStore.kt и core/security/TlsCertificateProbe.kt.

Точки входа: core/grpc/GrpcClientRegistry.kt, core/grpc/GrpcApiTransport.kt, core/grpc/TokenCoordinator.kt, core/grpc/MediaHttpTransport.kt; core/domain/gateway/DomainGateways.kt и core/domain/gateway/GrpcGatewayAdapters.kt; app/di/AppModule.kt.

### Обычный чат

MainActivity → ChatsFragment → ChatActivity. ChatActivity создаёт ChatViewModel; ViewModel управляет ChatUiState через intents/effects и делегирует историю RegularChatSession. MessageRowProjector строит immutable rows для MessageAdapter.

ComposerAttachmentStore сохраняет preview-файлы и metadata. ChatDraftRepository синхронизирует текстовый черновик; OutgoingMessageQueue передаёт принятый текст/media в SQLCipher outbox. WorkManager запускает OutgoingMessageWorker, который использует ChatRepository через MessageGateway/FileMediaGateway. Идемпотентность send/upload обеспечивается стабильным client operation ID.

Основные исходники: app/ChatActivity.kt, app/ChatViewModel.kt, app/ChatStateModules.kt, app/chat/RegularChatSession.kt, app/adapter/MessageRowProjector.kt, app/adapter/MessageAdapter.kt, app/drafts/ChatDraftRepository.kt, app/drafts/ComposerAttachmentStore.kt, app/send/OutgoingMessageQueue.kt, app/send/OutgoingMessageWorker.kt, app/cache/ChatCacheRepository.kt.

### Фоновые события и уведомления

BarkFluffApplication задаёт process lifecycle. RealtimeService при возобновлении поддерживает серверные стримы; FCM service обрабатывает data payload при фоне/убитом процессе. RealtimeSideEffectsImpl разделяет обработку событий и app UI побочные эффекты. Уведомления имеют receiver-ы с explicit PendingIntent; Direct Reply передаётся через scope-checked durable outbox.

Основные исходники: app/BarkFluffApplication.kt, core/grpc/RealtimeService.kt, app/notifications/RealtimeSideEffectsImpl.kt, app/notifications/NotificationHelper.kt, app/notifications/BarkFluffFirebaseMessagingService.kt, app/notifications/NotificationActionReceiver.kt, app/send/OutgoingMessageWorker.kt.

### Другие области

- Calls: CallsFragment → CallActivity → LiveKitCallEngine; CallsApi и события принадлежат core/calls и app/calls orchestration. Incoming-call service связывает FCM/realtime, Android Telecom и foreground UI.
- E2E: PrivateChatRepository обрабатывает passphrase-based private chat; SecretChatRepository содержит libsignal scaffold, но текущий handshake заблокирован отсутствующими Kyber-полями в server prekey proto.
- Поиск: SearchActivity → SearchViewModel/SearchScreen; ChatSearchActivity → ChatSearchViewModel/MessageSearchViewModel/ChatSearchScreen.
- Share: внешний ACTION_SEND → ShareReceiverActivity → ShareConfirmBottomSheet → OutgoingMessageQueue.

## Границы ответственности

- ChatViewModel — внешний state/effect/intent контракт регулярного чата; ChatActivity не владеет основным timeline/composer/presence state.
- MessageAdapter только отображает MessageRowUi и поднимает события через MessageRowEventSink. Projector определяет rows, отдельные контроллеры загружают вложения и воспроизводят audio.
- UI/background adapters зависят от gateways, не от generated RPC stubs.
- Room v5 владеет scoped cache, draft metadata, composer metadata, outbox и независимым журналом pending read actions. Файлы composer/outbox находятся в app-private noBackup staging.
- Clear cache/logout обязаны удалять соответствующие scoped staging files вместе с базой и останавливать фоновые сетевые задачи по scope.
