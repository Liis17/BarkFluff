//
//  DependencyContainer.swift
//  Barkfluff
//
//  Контейнер зависимостей для всего приложения (iOS версия)
//

import Foundation
import SwiftUI
import BFNetworking
import BFCore
import BFCalls
import Nuke

/// Контейнер зависимостей для всего приложения
@Observable
final class DependencyContainer {

    // MARK: - Connection Manager

    let connectionManager: ConnectionManager

    // MARK: - Token Provider

    let tokenProvider: any TokenProvider

    /// Текущий тип хранилища токенов
    let tokenStorageType: TokenStorageType

    // MARK: - Auth Infrastructure

    let tokenRefreshCoordinator: TokenRefreshCoordinator

    // MARK: - Repositories

    let beaconRepository: BeaconRepository
    let identityRepository: IdentityRepository
    let usersRepository: UsersRepository
    let chatFoldersRepository: ChatFoldersRepository
    let messagesRepository: MessagesRepository
    let filesRepository: FilesRepository
    let stickersRepository: StickersRepository
    let updatesRepository: UpdatesRepository
    let fastAuthRepository: FastAuthRepository
    let navigatorRepository: NavigatorRepository
    let onlinerRepository: OnlinerRepository

    // MARK: - Cache

    let userCache: UserCache
    let chatCache: ChatCache
    let mediaCacheManager: MediaCacheManager
    let onlineStatusCache: OnlineStatusCache
    let fileURLCache: FileURLCache

    // MARK: - Local Persistence

    let database: Database
    let localChatRepository: LocalChatRepository
    let localChatFolderRepository: LocalChatFolderRepository
    let localMessageRepository: LocalMessageRepository
    let localCurrentUserRepository: LocalCurrentUserRepository

    // MARK: - Image Pipeline

    let imagePipeline: ImagePipeline

    // MARK: - Services (BFCore)

    let authService: AuthService
    let chatService: ChatService
    let chatFolderService: ChatFolderService
    let messageService: MessageService
    let userService: UserService
    let fileService: FileService
    let stickersService: StickersService
    let updatesService: UpdatesService
    let fastAuthService: FastAuthService
    let serverDiscoveryService: ServerDiscoveryService
    let sharedMediaService: SharedMediaService
    let onlineStatusService: OnlineStatusService

    // MARK: - Streaming

    let updatesStreamManager: UpdatesStreamManager
    let onlinerStreamManager: OnlinerStreamManager

    // MARK: - Calls

    /// gRPC-сигнализация звонков (BarkFluff.Calls).
    let callsRepository: CallsRepository
    /// Фоновый стрим событий звонков (ринг/принят/завершён/...).
    let callEventsStreamManager: CallEventsStreamManager

    // MARK: - Personalization

    /// Локальные настройки персонализации (UserDefaults-backed).
    let personalizationSettings: PersonalizationSettings

    // MARK: - Appearance

    /// Локальные настройки внешнего вида (тема приложения).
    let appearanceSettings: AppearanceSettings

    // MARK: - Localization

    /// Локальные настройки языка интерфейса.
    let localizationSettings: LocalizationSettings

    // MARK: - Developer

    /// Локальные отладочные флаги (показ ID пользователей/чатов в профиле).
    let developerSettings: DeveloperSettings

    // MARK: - Stickers

    /// Список недавно использованных стикеров (UserDefaults).
    let recentStickersStore: RecentStickersStore

    // MARK: - Settings

    /// ViewModel для настроек
    var settingsViewModel: SettingsViewModel {
        let vm = SettingsViewModel()
        vm.dependencyContainer = self
        return vm
    }

    // MARK: - Calls Factory

    /// Создаёт CallController. Владелец — корневая вью (живёт всю сессию,
    /// чтобы входящие ловились app-wide). Сам стартует/останавливает
    /// `callEventsStreamManager` через `start()`/`stop()`.
    @MainActor
    func makeCallController() -> CallController {
        CallController(callsRepository: callsRepository, eventsManager: callEventsStreamManager)
    }

    // MARK: - Current User

    /// ID текущего пользователя (загружается при первом обращении)
    var currentUserID: Int64 = 0

    /// Текущий пользователь (обновляется при логине и редактировании профиля)
    var currentUser: User?

    /// Инициалы текущего пользователя
    var currentUserInitials: String {
        currentUser?.initials ?? "?"
    }

    /// URL аватара текущего пользователя
    var currentUserAvatarURL: String? {
        currentUser?.profilePicturePreviewURL
    }

    // MARK: - Init

    init() {
        // Connection Manager
        self.connectionManager = ConnectionManager()

        // Token Provider - выбор по настройкам
        let storageType = TokenStorageSettings.initialStorageType()
        self.tokenStorageType = storageType

        switch storageType {
        case .userDefaults:
            self.tokenProvider = UserDefaultsTokenProvider()
        case .keychain:
            self.tokenProvider = KeychainTokenProvider(configuration: .default)
        case .keychainICloud:
            self.tokenProvider = KeychainTokenProvider(configuration: .withICloud)
        }

        // Repositories
        self.beaconRepository = BeaconRepository(connectionManager: connectionManager)
        self.identityRepository = IdentityRepository(connectionManager: connectionManager)
        self.usersRepository = UsersRepository(connectionManager: connectionManager)
        self.chatFoldersRepository = ChatFoldersRepository(connectionManager: connectionManager)
        self.messagesRepository = MessagesRepository(connectionManager: connectionManager)
        self.filesRepository = FilesRepository(connectionManager: connectionManager)
        self.stickersRepository = StickersRepository(connectionManager: connectionManager)
        self.updatesRepository = UpdatesRepository(connectionManager: connectionManager)
        self.fastAuthRepository = FastAuthRepository(connectionManager: connectionManager)
        self.navigatorRepository = NavigatorRepository(
            host: "navigator.barkfluff.com",
            port: 443
        )
        self.onlinerRepository = OnlinerRepository(connectionManager: connectionManager)

        // Token Refresh Coordinator
        self.tokenRefreshCoordinator = TokenRefreshCoordinator(
            tokenProvider: tokenProvider,
            identityRepository: identityRepository
        )

        // Auth Interceptor — устанавливаем в ConnectionManager
        let authInterceptor = AuthInterceptor(
            tokenProvider: tokenProvider,
            refreshCoordinator: tokenRefreshCoordinator,
            onSessionExpired: { [weak connectionManager] in
                // Будет обработано через AppCoordinator
                Task { await connectionManager?.shutdown() }
            }
        )

        // Cache
        self.userCache = UserCache()
        self.chatCache = ChatCache()
        self.onlineStatusCache = OnlineStatusCache()
        self.fileURLCache = FileURLCache()

        // Local persistence (SQLite через GRDB).
        // Если открыть БД не удалось — падаем сразу, чтобы не работать с битым стейтом.
        do {
            self.database = try Database()
        } catch {
            fatalError("Failed to open local cache database: \(error)")
        }
        self.localChatRepository = LocalChatRepository(database: database)
        self.localChatFolderRepository = LocalChatFolderRepository(database: database)
        self.localMessageRepository = LocalMessageRepository(database: database)
        self.localCurrentUserRepository = LocalCurrentUserRepository(database: database)

        // Image Pipeline — disk cache, не зависит от HTTP cache headers
        self.imagePipeline = ImagePipeline {
            let dataCache = try? DataCache(name: "com.barkfluff.imageCache")
            $0.dataCache = dataCache
            $0.dataCachePolicy = .storeAll
            $0.isStoringPreviewsInMemoryCache = true
        }

        // Streaming
        self.updatesStreamManager = UpdatesStreamManager(
            updatesRepository: updatesRepository,
            tokenRefreshCoordinator: tokenRefreshCoordinator
        )
        self.onlinerStreamManager = OnlinerStreamManager(
            onlinerRepository: onlinerRepository,
            tokenRefreshCoordinator: tokenRefreshCoordinator
        )

        // Calls
        self.callsRepository = CallsRepository(connectionManager: connectionManager)
        self.callEventsStreamManager = CallEventsStreamManager(
            callsRepository: callsRepository,
            tokenRefreshCoordinator: tokenRefreshCoordinator
        )

        // Services
        self.authService = AuthService(
            identityRepository: identityRepository,
            tokenProvider: tokenProvider,
            connectionManager: connectionManager
        )

        self.chatService = ChatService(messagesRepository: messagesRepository)

        self.chatFolderService = ChatFolderService(
            repository: chatFoldersRepository,
            localRepository: localChatFolderRepository
        )

        self.messageService = MessageService(
            messagesRepository: messagesRepository,
            updatesRepository: updatesRepository
        )

        self.userService = UserService(
            usersRepository: usersRepository,
            userCache: userCache
        )

        self.fileService = FileService(filesRepository: filesRepository, fileURLCache: fileURLCache)

        self.mediaCacheManager = MediaCacheManager(
            database: database,
            fileService: fileService
        )

        self.stickersService = StickersService(
            repository: stickersRepository,
            mediaCacheManager: mediaCacheManager,
            database: database
        )

        self.updatesService = UpdatesService(
            updatesRepository: updatesRepository,
            streamManager: updatesStreamManager
        )

        self.fastAuthService = FastAuthService(fastAuthRepository: fastAuthRepository)

        self.serverDiscoveryService = ServerDiscoveryService(
            beaconRepository: beaconRepository,
            navigatorRepository: navigatorRepository,
            connectionManager: connectionManager,
            tokenProvider: tokenProvider
        )

        self.sharedMediaService = SharedMediaService(
            messagesRepository: messagesRepository,
            fileService: fileService
        )

        self.onlineStatusService = OnlineStatusService(
            onlinerRepository: onlinerRepository,
            streamManager: onlinerStreamManager,
            cache: onlineStatusCache
        )

        // Personalization (локальные настройки чата)
        self.personalizationSettings = PersonalizationSettings()

        // Appearance (тема приложения)
        self.appearanceSettings = AppearanceSettings()

        // Localization (язык интерфейса)
        self.localizationSettings = LocalizationSettings()

        // Developer (отладочные флаги)
        self.developerSettings = DeveloperSettings()

        // Stickers
        self.recentStickersStore = RecentStickersStore()

        // Устанавливаем интерсепторы после создания всех зависимостей
        // (нужен Task т.к. connectionManager — actor)
        let deviceMetadataInterceptor = DeviceMetadataInterceptor(
            tokenProvider: tokenProvider
        )

        Task {
            await connectionManager.setDeviceMetadataInterceptor(deviceMetadataInterceptor)
            await connectionManager.setAuthInterceptor(authInterceptor)
        }
    }

    // MARK: - Current User Methods

    /// Stale-first загрузка: мгновенно вытаскиваем кеш, ревалидацию запускаем
    /// в фоне fire-and-forget. Возвращается **сразу** после чтения из SQLite,
    /// чтобы `ChatListView.task` не подвисал на сетевом запросе getCurrentUser.
    func loadCurrentUser() async {
        // 1. Stale — мгновенно показываем последнего сохранённого юзера.
        if currentUser == nil,
           let cached = try? await localCurrentUserRepository.loadCachedCurrentUser() {
            currentUser = cached
            currentUserID = cached.id
        }
        // 2. Revalidate в фоне — не блокирует caller.
        Task { await revalidateCurrentUser() }
    }

    /// Сетевой запрос за актуальным профилем + write-through в SQLite.
    /// Дёргается из `loadCurrentUser` и из write-through-точек редактирования
    /// профиля (changeName, setProfilePicture, setProfilePoster, ...).
    func revalidateCurrentUser() async {
        do {
            let user = try await userService.getCurrentUser()
            currentUserID = user.id
            currentUser = user
            try? await localCurrentUserRepository.save(user)
        } catch {
            // Сеть упала — оставляем кешированного юзера видимым.
        }
    }

    // MARK: - Helpers

    /// Очистка всех персональных данных при logout.
    /// Стирает: стримы, in-memory кеши, файловые кеши, локальную БД, токены, device_id,
    /// локальные UserDefaults-настройки клиента (тема, персонализация, developer-флаги).
    /// **Сохраняет**: host/port beacon-сервера, чтобы после logout пользователь сразу
    /// попал на экран логина того же сервера.
    func reset() async {
        // 1. Останавливаем все стримы и фоновые подписки
        await updatesService.stop()
        await onlineStatusService.stop()
        await callEventsStreamManager.stop()

        // 2. Чистим in-memory кеши
        await userCache.removeAll()
        await chatCache.removeAll()
        await onlineStatusCache.removeAll()

        // 3. Чистим файловый и URL-кеши
        await mediaCacheManager.clearAll()
        await fileURLCache.clear()
        await stickersService.clearCache()
        recentStickersStore.clear()
        imagePipeline.cache.removeAll()

        // 4. Чистим локальную БД
        try? await database.truncateAll()

        // 5. Стираем токены и device_id, СОХРАНЯЕМ host/port сервера
        await tokenProvider.purgeForLogout()

        // 5a. Стираем локальные настройки клиента
        await MainActor.run {
            personalizationSettings.reset()
            appearanceSettings.reset()
            localizationSettings.reset()
            developerSettings.reset()
        }

        // 6. Закрываем gRPC-соединения (эндпоинты будут перевыбраны через beacon при логине)
        await connectionManager.shutdown()

        // 7. Сбрасываем флаги текущего пользователя
        currentUserID = 0
        currentUser = nil
    }
}
