# BarkFluff Android

Parent: [[Index]]

Kotlin-клиент в Android/Barkfluff.Client.Android. Поддерживаемая основная версия — V1; общий сетевой и доменный слой вынесен в Android/core. Этот обзор хранит текущие контракты и ограничения. Навигационная карта потоков: [[Clients/Android-ProjectMap|Android-ProjectMap]]. Указатель по модулям и точкам входа: [[Clients/Android-FileIndex|Android-FileIndex]]. UI-референс: [[Clients/DesignDocument]].

## Сборка

Единственный Gradle root — Android. Он включает :core и :app-v1; каталог приложения физически находится в Android/Barkfluff.Client.Android/app.

| Параметр | Значение | Источник |
|---|---|---|
| Gradle | 9.2.1 | Android/gradle/wrapper/gradle-wrapper.properties |
| Android Gradle Plugin | 8.9.1 | Android/gradle/libs.versions.toml |
| Kotlin | 2.2.20 | Android/gradle/libs.versions.toml |
| KSP | 2.2.20-2.0.3 | Android/gradle/libs.versions.toml |
| min / compile / target SDK | 31 / 36 / 36 | Android/Barkfluff.Client.Android/app/build.gradle.kts |
| Java bytecode | 17; Gradle daemon toolchain — JDK 21; CI — JDK 21 | Android/Barkfluff.Client.Android/app/build.gradle.kts, Android/gradle/gradle-daemon-jvm.properties, .github/workflows/build-client-android.yml, .github/workflows/test-clients.yml |
| RPC transport | gRPC-OkHttp 1.60.0; gRPC Kotlin 1.4.1 | Android/core/build.gradle.kts |

Сборка debug-варианта из Android: ./gradlew :app-v1:assembleStableDebug; короткие имена задач неоднозначны из-за flavors dev/nightly/stable (compileDebugKotlin → compileStableDebugKotlin и т.п.). На Windows обёртку вызывают через gradlew.bat: скрипт gradlew имеет CRLF и завершается до старта Gradle. Для flavors stable, dev и nightly заданы отдельные applicationId; стабильный канал публикуется как release в ClientStorage. Release использует R8, resource shrinking и только arm64-v8a.

## Границы и основные потоки

- :app-v1 — Activities и Fragments на ViewBinding/XML; пользовательский и чат-поиск — Compose. Android/core — gRPC, proto, gateways, репозитории, cache utilities и доменные модели.
- SplashActivity проверяет сохранённую ноду и токен, затем направляет на Welcome/SelectServer/Login или MainActivity. SelectServerActivity читает каталог через Navigator, получает адреса сервисов через Beacon и сохраняет их через GlobalParam.applyServerInfo.
- Hilt wiring находится в Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/di/AppModule.kt. UI и background-компоненты получают typed gateways; gRPC stubs и protobuf mapping остаются в transport/repository слое.
- Calls V1 использует CallsApi для сигналинга и LiveKit для аудио/видео. UI звонка — calls/CallActivity.kt; жизненным циклом звонковых событий владеет calls/CallEventsService.kt.
- Источники UI-референсов не являются спецификацией сетевых контрактов. Для точной реализации ориентируйся на Kotlin, manifest, Gradle, proto и gateway-контракты.

## Онбординг и юридические документы

- WelcomeActivity показывает hero без иконки приложения. Под основной кнопкой шеврон 36dp с областью касания 48dp раскрывает ссылки на документы свайпом вверх или нажатием; свайп вниз скрывает их. Нижняя область фиксирована, поэтому hero и CTA не сдвигаются. Скрытые ссылки исключены из фокуса и accessibility; состояние раскрытия сохраняется при пересоздании Activity, отключение системных анимаций учитывается.
- LegalConsentBottomSheet использует общую Material 3 панель с заголовком и иконкой текущего документа, скруглённой областью чтения и тональной кнопкой закрытия. Режим forReading(tab) разрешает переключать вкладки; обязательное согласие сохраняет последовательное чтение двух документов до конца и финальный чекбокс. Тексты загружаются LegalDocsRepository из локализованных assets/legal; принятая редакция хранится в GlobalParam.acceptedLegalRevision.
- На SelectServerActivity предупреждение о доверии ноде и подсказка размещены в отдельной плавающей Material-карточке с цветом colorSurfaceContainerHigh и скруглением 24dp. Прокручиваемая форма заканчивается над карточкой с зазором и плавным затуханием края; нижние действия своей ноды доступны при прокрутке.

## Transport и авторизация

- GrpcClientRegistry владеет gRPC channels/stubs. Navigator и Beacon создаются явно для выбора ноды; пользовательские сервисы создаются лениво по endpoint-ам GlobalParam. Пока registry активен, смена endpoint или конфигурации interceptors заменяет слот на следующем чтении. shutdown() терминален: lazy getters возвращают null, create-методы — failure; новые каналы после него не создаются.
- GrpcApiTransport — реализация для gateway/repository, а не UI API. Публичные доменные порты объявлены в Android/core/src/main/java/com/barkfluff/client/domain/gateway/DomainGateways.kt, production mapping — в Android/core/src/main/java/com/barkfluff/client/domain/gateway/GrpcGatewayAdapters.kt.
- TokenCoordinator сериализует refresh общим process-wide mutex. Токен считается свежим при запасе более пяти минут. Успешное обновление сохраняет новые access и refresh tokens. Непринятый refresh token даёт REJECTED; недоступность Identity и транспортные ошибки дают UNAVAILABLE, чтобы запрос можно было повторить без преждевременного logout. ChatsViewModel проверяет токен через AuthGateway.tokenValidity(): REJECTED ведёт на LoginActivity без очистки кеша, UNAVAILABLE — статус offline.
- AuthInterceptor добавляет x-auth-token, DeviceInfoInterceptor добавляет metadata устройства. Anonymous Navigator/Beacon каналы работают без auth.
- RealtimeService (Android/core/src/main/java/com/barkfluff/client/grpc/RealtimeService.kt) держит Updates/Onliner стримы через streamWithReconnect. Ошибка стрима пересоздаёт все каналы GrpcClientRegistry только если поколение каналов не менялось с момента подключения: пересоздание закрывает старые каналы и роняет остальные стримы, которые затем просто переподключаются без повторного пересоздания.
- Стрим SubscribeChatHidden (DeleteChat на любом устройстве) убирает чат из ChatsViewModel и кеша; при content_wiped удаляются и кешированные сообщения чата. На нодах без этого RPC UNIMPLEMENTED отключает только этот стрим. Выход из группы ChatHidden не порождает (см. [[Backend/Messages|Messages]]). При неполном снимке списка (больше трёх страниц) reconcileChatList отбрасывает кешированные чаты, которые новее последнего загруженного, но отсутствуют в ответе сервера.
- MediaHttpTransport применяет TLS к файловому HTTP и заменяет origin на socketFilesMedia, когда Beacon/Navigator объявил отдельный Files media endpoint. Пустой endpoint сохраняет исходный Files URL.

## Состояние обычного чата и durable отправка

Единственная state/effect/intent граница обычного чата — ChatViewModel.state: StateFlow<ChatUiState>, effects: Flow<ChatEffect>, dispatch(ChatIntent). ChatUiState содержит session, timeline, composer, selection и presence. ChatActivity — lifecycle/rendering shell. RegularChatSession владеет cache-first чтением и курсорами истории; MessageRowProjector строит immutable строки, которые MessageAdapter отображает через MessageRowEventSink. Основные файлы: Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/ChatViewModel.kt, ChatStateModules.kt, chat/RegularChatSession.kt, adapter/MessageRowProjector.kt и adapter/MessageAdapter.kt.

Стикер-панель чата управляется `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/stickers/StickerPanelController.kt`: он показывает кеш до сетевого обновления, атомарно публикует снимок только после загрузки всех паков и различает пустой успешный ответ и ошибку с повтором. `ChatActivity` связывает controller с `lifecycleScope`, `StickerGateway`, `StickerCache` и `StickerPanelAdapter`; закрытие панели не отменяет запрос, уничтожение Activity отменяет его вместе со scope.

Черновик текста и reply синхронизируются через ChatDraftRepository. Reply и edit взаимно исключаются. Pending reply/edit, выбранные сообщения и target-навигация восстанавливаются через SavedStateHandle. Presence подписки принадлежат ViewModel и пересоздаются вместе с ней.

ChatCacheDatabase — SQLCipher Room v5 в offline_chat_cache.db. Scope кэша включает endpoint выбранной ноды и userId; ключ БД хранится в EncryptedSharedPreferences. ComposerAttachmentStore копирует принятые preview-вложения в noBackupFilesDir/composer/<scope>/<chatId>/ и сохраняет метаданные в Room. Запись временного файла атомарно переименовывается до фиксации metadata. draftGeneration связывает черновик, preview и outbox, чтобы восстановление после process death не дублировало отправку и не удаляло более новые вложения.

OutgoingMessageQueue — единственная публичная граница обычной text/media отправки. enqueue сначала копирует входные данные в приватное durable staging и записывает QUEUED в SQLCipher; только после этого задача принята UI. WorkManager будит очередь при сети/старте, стабильные operation IDs обеспечивают повторяемость upload/send, порядок FIFO сохраняется внутри чата. ChatCacheRepository.clearAll() удаляет БД, её ключ и composer staging; LogoutHelper дополнительно останавливает активные realtime/call потоки и удаляет FCM token. Кнопка «Очистить кеш» в StorageSettingsActivity destructive-вайп больше не вызывает: открывается модал с мультивыбором (медиа-кеш, история чатов, секретные чаты, исходящие в очереди, черновики), необратимые категории требуют второго подтверждения, в котором предупреждается, что без сети удаление серверных черновиков может не выполниться. Очистка: ChatCacheRepository.clearHistoryCache()/clearSecretHistory(), исходящие — OutgoingMessageQueue.cancelPendingOutgoing(), черновики — ChatDraftRepository.wipeAll(scope) и ComposerAttachmentStore.clearScope(scope) только для выбранного account/server scope. Снимок черновиков, RPC и фиксация ответа сериализованы с wipe; очистка ждёт выполняющийся upsert и удаляет его ревизию. Ответ upsert сохраняет ревизию и при более новом локальном редактировании, не заменяя текст и sync state. Новые правки во время серверного удаления сохраняются в памяти и БД. Эти методы не эмитят clearedEvents и не вакуумят сами; список чатов в памяти остаётся валидным, а экран после выбранных категорий вызывает compact() один раз (wal_checkpoint TRUNCATE с логом BUSY + VACUUM). «Исходящие в очереди» удаляют не-SENT строки и каталоги удалённых операций; SENT-строки (защита от дублей повторной отправки) и журнал pending_message_reads сохраняются всегда; после завершения отмены WorkManager очередь заново планирует worker по оставшимся попыткам, включая read receipts. clearAll() остаётся только logout-путём и единственным источником clearedEvents.

## Security инварианты

- Release запрещает cleartext; network_security_config использует системные trust anchors. TLS-сокеты создаёт TlsTransportFactory: platform trust store и проверка hostname остаются стандартным путём.
- Самоподписанный сертификат может получить пользовательский pin только после явного подтверждения fingerprint. Pin ограничен конкретным hostname и SPKI SHA-256; смена ключа блокирует соединение. TlsCertificateProbe выполняет TLS handshake без HTTP/gRPC данных и токенов. См. Android/core/src/main/java/com/barkfluff/client/security/TlsTransportFactory.kt, TlsTrustStore.kt и TlsCertificateProbe.kt.
- Для сборок обновления допускается резервный CA storage, применяемый только после ошибки системной TLS-проверки; hostname verification сохраняется. CA задаётся при сборке в app/build.gradle.kts и используется Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/utils/UpdateServerTls.kt.
- Challenge reference и security proof остаются в памяти AuthenticationChallengeViewModel; их нельзя помещать в Bundle, SavedStateHandle, логи или persistent storage. LoginActivity сохраняет токены только после завершённого challenge.
- Private/Secret push-маркеры содержат только metadata, без текста или шифротекста для показа. NotificationHelper показывает нейтральное уведомление о новом зашифрованном сообщении.
- Release удаляет Log.v/d/i/w/println через R8. Аргументы Log.e и proto toString() не должны содержать PII, текст, presigned URL, content URI или FCM token.
- network_security_config намеренно остаётся base-config с системными trust anchors. По комментарию в UpdateServerTls.kt, добавление domain-config несовместимо с hostname-unaware вызовом checkServerTrusted внутри PinnedTrustManager.

## Возможности клиента

- LoginActivity сначала вызывает Identity `ResolveSignIn`: режим аккаунта `PASSWORD`/`PASSWORD_SECOND_FACTOR` открывает поле пароля, `TELEGRAM_LOGIN` сразу начинает Telegram challenge. Каждый `BeginSignIn` отправляет `report_login_mode_mismatch`, чтобы обнаружить смену режима после resolution; подтверждение и сохранение токенов остаются в challenge flow.
- Регистрация и восстановление пароля используют Identity challenge API: RegisterActivity, ResetPasswordActivity и AuthenticationChallengeGateway.
- RegisterActivity открывает Photo Picker для аватара без разрешения на доступ к медиатеке; выбранное изображение проходит через UCrop, сохраняется в `avatarBytes` и загружается при продолжении регистрации.
- Поиск пользователей находится в SearchActivity; поиск чатов и сообщений — в ChatSearchActivity. Оба экрана на Compose, остальные основные экраны — XML/ViewBinding.
- Обычные чаты поддерживают realtime сообщения, read receipts, durable drafts/outbox, файлы, голосовые сообщения и звонки.
- Private chat шифрует содержимое через passphrase-derived key (Argon2id + AES-GCM). Secret-chat слой на libsignal сейчас не проходит handshake: SecretChatRepository.toLibsignal() бросает UnsupportedOperationException, потому что серверный Users prekey bundle не содержит Kyber fields, требуемых libsignal 0.86+. Не описывай secret chats как доступную функцию до расширения протокола.
- Direct Reply из уведомления помещает ответ в тот же durable outbox; receiver проверяет account/server scope перед постановкой задачи.
- Строки приложения поддерживают ru, en, de, es и zh-CN. Пользовательский текст хранится в ресурсах, notification channel IDs остаются стабильными.

## Исходники и тестовое покрытие

Подробная карта расположена в [[Clients/Android-ProjectMap|Android-ProjectMap]], список областей исходников и тестов — в [[Clients/Android-FileIndex|Android-FileIndex]]. Значимые проверки находятся в Android/core/src/test для registry/token/TLS/auth, Android/Barkfluff.Client.Android/app/src/test для chat state/drafts/search/outbox policy и app/src/androidTest для SQLCipher migration, durable queue, выборочной очистки кеша, notifications, voice и Compose UI. Скрипт локальной проверки UI ресурсов — Android/tools/check_android_ui.py.
