//
//  SettingsViewModel.swift
//  Barkfluff
//
//  ViewModel для настроек
//

import SwiftUI
import Observation
import BFNetworking

@Observable
final class SettingsViewModel {
    var sessions: [Session] = []
    var storageInfo: StorageInfo?
    var serverInfo: ServerInfo?
    var twoFactorEnabled = false
    var isLoading = false
    var errorMessage: String?

    // MARK: - Token Storage

    var selectedStorageType: TokenStorageType = .userDefaults
    var isMigratingStorage = false
    var migrationError: String?
    var showRestartRequired = false

    // Зависимости (инжектятся извне)
    weak var dependencyContainer: DependencyContainer?

    // TODO: Inject services
    // private let identityService: IdentityServiceProtocol
    // private let fileService: FileServiceProtocol

    init() {
        // Загружаем настройки хранилища
        let settings = TokenStorageSettings()
        self.selectedStorageType = settings.storageType

        // Placeholder data
        self.sessions = [
            Session(
                id: "1",
                deviceName: "MacBook Pro",
                deviceType: .mac,
                lastActive: Date(),
                isCurrent: true
            ),
            Session(
                id: "2",
                deviceName: "iPhone 15",
                deviceType: .iphone,
                lastActive: Date().addingTimeInterval(-3600),
                isCurrent: false
            )
        ]
        self.storageInfo = StorageInfo(usedGB: 2.5, limitGB: 10)
        self.serverInfo = ServerInfo(
            name: "BarkFluff Server",
            version: "1.0.0",
            description: "Основной сервер BarkFluff"
        )
    }

    func loadSettings() async {
        isLoading = true

        // TODO: Load from services
        // sessions = try await identityService.getActiveSessions()
        // storageInfo = try await fileService.getUserStorageInfo()

        try? await Task.sleep(for: .seconds(0.3))
        isLoading = false
    }

    func terminateSession(_ sessionID: String) async {
        // TODO: Implement via IdentityService
        sessions.removeAll { $0.id == sessionID }
    }

    func enable2FA() async {
        // TODO: Implement via IdentityService
    }

    func disable2FA() async {
        // TODO: Implement via IdentityService
    }

    // MARK: - Token Storage Management

    /// Переключить хранилище токенов
    func switchTokenStorage(to newType: TokenStorageType) async {
        guard newType != selectedStorageType else { return }

        isMigratingStorage = true
        migrationError = nil

        do {
            // Получаем текущий провайдер
            guard let dc = dependencyContainer else {
                migrationError = "DependencyContainer не настроен"
                isMigratingStorage = false
                return
            }

            let currentProvider = dc.tokenProvider

            // Экспортируем данные
            let exportData = await currentProvider.exportAllData()

            // Создаём новый провайдер
            let newProvider: any TokenProvider
            switch newType {
            case .userDefaults:
                newProvider = UserDefaultsTokenProvider()
            case .keychain:
                newProvider = KeychainTokenProvider(configuration: .default)
            case .keychainICloud:
                newProvider = KeychainTokenProvider(configuration: .withICloud)
            }

            // Импортируем данные
            try await newProvider.importAllData(exportData)

            // Обновляем настройки
            let settings = TokenStorageSettings()
            settings.storageType = newType
            settings.migrationCompleted()

            // Обновляем UI
            selectedStorageType = newType

            // Очищаем старые данные (кроме deviceId)
            await currentProvider.clearAll()

            // Требуется перезапуск
            showRestartRequired = true

        } catch {
            migrationError = "Ошибка миграции: \(error.localizedDescription)"
        }

        isMigratingStorage = false
    }
}

// Preview data models
struct StorageInfo {
    let usedGB: Double
    let limitGB: Int

    var usedPercentage: Double {
        usedGB / Double(limitGB)
    }
}

struct ServerInfo {
    let name: String
    let version: String
    let description: String?
}
