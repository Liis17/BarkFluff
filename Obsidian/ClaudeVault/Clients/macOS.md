# BarkFluff macOS

SwiftUI-клиент для macOS. Исходники приложения — Mac/Barkfluff/Barkfluff/, общие Swift-пакеты с iOS — Mac/Barkfluff/Packages/.

> UI/UX-сценарии: [[Clients/DesignDocument]]
> Карта точек входа и исходников: [[Clients/macOS-ProjectMap]]
> Общие контракты с iOS: [[Clients/iOS]]

## Платформа и пакеты

- App target: macOS 26.0, Swift 6.0; локальные Swift-пакеты используют tools-version 6.2 и объявляют macOS/iOS 26.
- BFNetworking/Package.swift задаёт grpc-swift-2 от 2.3.0, grpc-swift-protobuf от 2.0.0 и grpc-swift-nio-transport от 2.0.0. BFProto также требует swift-protobuf от 1.28.0. Это нижние границы Package.swift, не точные версии lock-файла.
- BFNetworking управляет gRPC-транспортом и репозиториями; BFCore содержит сервисы, модели и GRDB-кеш; BFMarkdown рендерит общий Markdown; BFCalls связывает сигнализацию с LiveKit.
- Зависимости пакетов: BFNetworking → BFProto, BFCore → BFNetworking, BFMarkdown → BFCore, BFCalls → BFNetworking.

## Запуск, состояние и кеш

BarkfluffApp создаёт DependencyContainer и AppCoordinator. Coordinator ведёт состояния loading → serverSelection → authentication → main.

- Без сохранённого Beacon host клиент открывает выбор сервера. С сохранённым host, но без refresh-токена, пробует переподключиться и показывает вход либо выбор сервера.
- При наличии refresh-токена приложение сразу открывает main и в фоне выполняет Beacon reconnect и tryRestoreSession. isConnectionReady становится true только после готовых service endpoints и действующего access-токена; истёкшая сессия возвращает на вход.
- ChatListView ждёт waitForConnectionReady до 30 секунд с опросом каждые 100 мс и лишь затем запускает загрузку чатов, папок и текущего пользователя. На успешном соединении ChatListViewModel сначала показывает SQLite-кеш, затем обновляет его с сервера. DependencyContainer.loadCurrentUser аналогично поднимает cached_current_user и запускает сетевую ревалидацию отдельно.
- Ограничение текущего потока: если gate истёк или Beacon недоступен, загрузчики не запускаются и локальный список из SQLite не читается. Индикатор снимается, но отдельная ошибка подключения при этом не задаётся.
- BFCore хранит локальный кеш в GRDB/SQLite в Application Support. Миграции v1–v6 создают кеш файлов, чатов, сообщений, стикеров, папок и текущего пользователя. Файловый и оперативный кеши управляются через MediaCacheManager и CacheProtocol.
- OnlineStatusService — единственный источник статуса для наблюдаемых пользователей. UI сначала читает currentStatus(for:), затем вызывает track и подписывается на личный statusStream; каждому track должен соответствовать untrack. Строка списка чатов использует .task(id:) и снимает tracking при отмене.
- UpdatesService передаёт новые, изменённые и удалённые сообщения независимым подписчикам. stop завершает потоки; последующий start создаёт новые AsyncStream, поэтому потребители должны подписаться заново.

## Сеть и безопасность

- NavigatorRepository подключается к navigator.barkfluff.com:443 через TLS.
- ConnectionManager.bootstrap сначала запрашивает Beacon по plaintext и при ошибке пробует TLS. Для остальных сервисов TLS выбирается по tlsEnabled из ответа Beacon.
- Авторизованные gRPC-запросы получают x-auth-token и метаданные устройства через интерсепторы; публичный клиент пропускает auth-интерсептор. AuthInterceptor обновляет скоро истекающий токен через сериализованный TokenRefreshCoordinator.
- Beacon может объявить отдельный files_media_endpoint. ConnectionManager.rewriteHost подменяет только origin файловых URL; репозитории сообщений, пользователей, обновлений, стикеров и файлов должны применять его ко всем URL-файлам из ответа сервиса.
- В клиенте нет реализации E2E-шифрования/secret chat или проверки собственного certificate pin. Транспортная защита — конфигурация gRPC TLS; она не заменяет E2E.
- Тип хранения токенов выбирается в Settings → Безопасность. Начальное значение — UserDefaults; доступны Keychain и Keychain+iCloud. При переключении клиент переносит экспортированные данные, очищает старое хранилище (сохраняя device ID) и требует перезапуска. Keychain использует after-first-unlock доступ. Локальной блокировки приложения PIN/Face ID/Touch ID нет. GRDB открывает обычный SQLite DatabasePool без SQLCipher.
- Переключатель серверной 2FA показан, но `enable2FA` и `disable2FA` пока TODO в SettingsViewModel на macOS и iOS; переключатель сейчас не включает серверную защиту.
- PrivacySettingsViewModel оптимистично меняет отображаемые настройки и откатывает их при ошибке сохранения на сервере.
- Logout сначала вызывает серверный Identity.Logout; при ошибке локальные данные остаются. После успеха AuthService вызывает purgeAll, который удаляет токены, device_id и сохранённый адрес сервера. DependencyContainer.reset очищает базы и кеши, сбрасывает тему, персонализацию и язык. Поэтому текущий код возвращает к выбору сервера. Force logout выполняет тот же локальный wipe без серверного вызова; удалённая сессия остаётся до её истечения или отзыва на сервере. Настройка типа token storage сохраняется.

## Сообщения и ввод

- ConversationViewModel редактирует только подтверждённые сообщения текущего пользователя. При редактировании исходные fileID обычных вложений сохраняются; изменение применяется оптимистично и повторно загружает сообщения при ошибке. Удаление также оптимистично, с rollback при ошибке.
- Realtime-события редактирования и удаления обновляют диалог, список чатов и локальный кеш.
- Ответ использует идентификатор исходного сообщения. Пересылка отправляет Message.forwardSourceID; повторная пересылка вложения сворачивается к id оригинала, а сервер создаёт пересланный snapshot.
- Исходный Markdown-текст не переписывается при сохранении/отправке. BFCore строит AST, BFMarkdown отображает его; URL и HTML-изображения проверяются allowlist-санитайзером.
- Перед отправкой оптимизируются только вложения-изображения: длинная сторона ограничивается 2500 px, данные кодируются в JPEG с качеством 0.9 → 0.5 до 2 MiB. Имя становится .jpg, чтобы multipart Content-Type соответствовал байтам. Видео, GIF, аудио и документы этот путь обходят.
- UpdatePersonalization записывает вместе posterFileID и полный список chatBackgroundFileIDs. При изменении только фонов клиент обязан передать текущий posterFileID, иначе серверная запись постера будет потеряна.
- Папки чатов и их GRDB-кеш общие с iOS; выбор папки фильтрует sidebar и бейджи непрочитанных.

## UI и функции macOS

- RootView показывает MainSplitView с боковым списком чатов и detail-панелью. Settings categories встроены в основной sidebar.
- FastAuthViewModel создаёт QR и слушает результат на macOS; после одобрения он сохраняет полученные токены. Авторизованный iOS-сканер отправляет Accept/Reject с confirmationCode.
- AppearanceSettings применяет выбранную тему через NSApplication.appearance, чтобы AppKit-заголовки окна, sidebar и SwiftUI-контент переключались вместе.
- ImageCropperView принимает NSImage: аватар — 1:1/1024×1024, постер — 3:1/1500×500. Кроп ограничивает pan так, чтобы изображение всегда покрывало выделенную область.
- NotificationService публикует локальные уведомления из Updates stream только в состоянии main и скрывает активный открытый чат. При logout снимает уже доставленные уведомления; remote push для macOS-клиента не реализован.
- Звонки используют BFCalls, CallsRepository и LiveKit. Оверлей плавающий, сворачиваемый и перетаскиваемый. Камера начинается выключенной и включается пользователем. В App Sandbox нужны audio-input, camera и network.server: последний требуется WebRTC UDP-медиа.
- Правила Liquid Glass — `Mac/Barkfluff/LiquidGlassGuide.md`.

## Локализация

App catalog: Mac/Barkfluff/Barkfluff/Resources/Localizable.xcstrings (sourceLanguage en, переводы ru/en). BFCore catalog находится в Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Resources/Localizable.xcstrings; Swift Package обрабатывает его как ресурс, поэтому внутри пакета используется Bundle.module.

LocalizationSettings применяется через environment locale на WindowGroup и Settings. Picker сейчас предлагает system, ru, en, es, zh-Hans и de, однако app catalogs содержат только переводы ru/en; для es/zh/de пользовательские строки берутся из английского source fallback. При system код языка ru/es/de/zh мапится на соответствующую Locale, остальные — на en; наличие Locale для форматирования не добавляет отсутствующий перевод текста. После logout настройка сбрасывается на system.

## Сборка

Из корня репозитория: cd Mac/Barkfluff && xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build
