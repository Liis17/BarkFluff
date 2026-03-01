//
//  DependencyContainer.swift
//  Barkfluff
//
//  Контейнер зависимостей для всего приложения
//

import Foundation
import SwiftUI
import BFNetworking
import BFCore

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
    let messagesRepository: MessagesRepository
    let filesRepository: FilesRepository
    let updatesRepository: UpdatesRepository
    let fastAuthRepository: FastAuthRepository
    let navigatorRepository: NavigatorRepository
    let onlinerRepository: OnlinerRepository

    // MARK: - Cache

    let userCache: UserCache
    let chatCache: ChatCache
    let fileCacheService: FileCacheService
    let onlineStatusCache: OnlineStatusCache

    // MARK: - Services (BFCore)

    let authService: AuthService
    let chatService: ChatService
    let messageService: MessageService
    let userService: UserService
    let fileService: FileService
    let updatesService: UpdatesService
    let fastAuthService: FastAuthService
    let serverDiscoveryService: ServerDiscoveryService
    let sharedMediaService: SharedMediaService
    let onlineStatusService: OnlineStatusService

    // MARK: - Streaming

    let updatesStreamManager: UpdatesStreamManager
    let onlinerStreamManager: OnlinerStreamManager

    // MARK: - Settings

    /// ViewModel для настроек
    var settingsViewModel: SettingsViewModel {
        let vm = SettingsViewModel()
        vm.dependencyContainer = self
        return vm
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
        self.messagesRepository = MessagesRepository(connectionManager: connectionManager)
        self.filesRepository = FilesRepository(connectionManager: connectionManager)
        self.updatesRepository = UpdatesRepository(connectionManager: connectionManager)
        self.fastAuthRepository = FastAuthRepository(connectionManager: connectionManager)
        self.navigatorRepository = NavigatorRepository(
            host: "navigator.barkfluff.com",
            port: 64646
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
        self.fileCacheService = FileCacheService()
        self.onlineStatusCache = OnlineStatusCache()

        // Streaming
        self.updatesStreamManager = UpdatesStreamManager(
            updatesRepository: updatesRepository,
            tokenRefreshCoordinator: tokenRefreshCoordinator
        )
        self.onlinerStreamManager = OnlinerStreamManager(
            onlinerRepository: onlinerRepository,
            tokenRefreshCoordinator: tokenRefreshCoordinator
        )

        // Services
        self.authService = AuthService(
            identityRepository: identityRepository,
            tokenProvider: tokenProvider,
            connectionManager: connectionManager
        )

        self.chatService = ChatService(messagesRepository: messagesRepository)

        self.messageService = MessageService(
            messagesRepository: messagesRepository,
            updatesRepository: updatesRepository
        )

        self.userService = UserService(
            usersRepository: usersRepository,
            userCache: userCache
        )

        self.fileService = FileService(filesRepository: filesRepository)

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

    /// Загрузить текущего пользователя и сохранить его ID
    func loadCurrentUser() async {
        do {
            let user = try await userService.getCurrentUser()
            currentUserID = user.id
            currentUser = user
        } catch {
            // Если не удалось получить — останется 0
        }
    }

    // MARK: - Helpers

    /// Сбросить все данные (при выходе из аккаунта)
    func reset() async {
        await onlineStatusService.stop()
        await tokenProvider.clearAll()
        await userCache.removeAll()
        await chatCache.removeAll()
        await onlineStatusCache.removeAll()
        await fileCacheService.clearCache()
        await serverDiscoveryService.disconnect()
        currentUserID = 0
        currentUser = nil
    }
}
