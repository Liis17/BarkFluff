//
//  SettingsViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel для настроек.
//

import SwiftUI
import Observation
import BFNetworking
import BFCore

@Observable
final class SettingsViewModel {
    var sessions: [SessionInfo] = []
    var currentDeviceId: String = ""
    var serverInfo: ServerInfo?
    var twoFactorEnabled = false
    var isLoading = false
    var isSessionsLoading = false
    var errorMessage: String?

    // MARK: - Token Storage

    var selectedStorageType: TokenStorageType = .userDefaults
    var isMigratingStorage = false
    var migrationError: String?
    var showRestartRequired = false

    /// Доступные типы хранилищ токенов
    var availableStorageTypes: [TokenStorageType] {
        [.userDefaults, .keychain, .keychainICloud]
    }

    /// Текущий тип хранилища (для совместимости с прежним API)
    var currentStorageType: TokenStorageType {
        dependencyContainer?.tokenStorageType ?? .userDefaults
    }

    // Зависимости (инжектятся извне)
    weak var dependencyContainer: DependencyContainer?

    init() {
        let settings = TokenStorageSettings()
        self.selectedStorageType = settings.storageType
    }

    func loadSettings() async {
        isLoading = true
        await loadSessions()
        await loadServerInfo()
        isLoading = false
    }

    /// Подтянуть актуальные данные о сервере из ServerDiscoveryService.
    func loadServerInfo() async {
        guard let dc = dependencyContainer else { return }
        if let info = await dc.serverDiscoveryService.getCurrentServer() {
            self.serverInfo = info
        }
    }

    // MARK: - Sessions

    func loadSessions() async {
        guard let dc = dependencyContainer else { return }

        isSessionsLoading = true
        errorMessage = nil

        let localDeviceId = await dc.tokenProvider.deviceID

        async let sessionsTask = dc.identityRepository.getActiveSessions()
        async let currentDeviceTask = dc.usersRepository.getCurrentDevice()

        do {
            sessions = try await sessionsTask
            let serverDevice: DeviceInfo? = (try? await currentDeviceTask) ?? nil
            if let id = serverDevice?.deviceId, !id.isEmpty {
                currentDeviceId = id
            } else {
                currentDeviceId = localDeviceId
            }
        } catch {
            currentDeviceId = localDeviceId
            errorMessage = "Не удалось загрузить сессии"
        }

        isSessionsLoading = false
    }

    func terminateSession(deviceID: String) async {
        guard let dc = dependencyContainer else { return }

        do {
            try await dc.identityRepository.removeActiveSession(deviceID: deviceID)
            sessions.removeAll { $0.deviceId == deviceID }
        } catch {
            errorMessage = "Не удалось завершить сессию"
        }
    }

    func terminateAllOtherSessions() async {
        guard let dc = dependencyContainer else { return }

        let otherSessions = sessions.filter {
            $0.deviceId.caseInsensitiveCompare(currentDeviceId) != .orderedSame
        }
        for session in otherSessions {
            do {
                try await dc.identityRepository.removeActiveSession(deviceID: session.deviceId)
                sessions.removeAll { $0.deviceId == session.deviceId }
            } catch {
                errorMessage = "Не удалось завершить сессию"
                break
            }
        }
    }

    func enable2FA() async {
        // TODO: Implement via IdentityService
    }

    func disable2FA() async {
        // TODO: Implement via IdentityService
    }

    // MARK: - Token Storage Management

    /// Переключить хранилище токенов.
    func switchTokenStorage(to newType: TokenStorageType) async {
        guard newType != selectedStorageType else { return }

        isMigratingStorage = true
        migrationError = nil

        do {
            guard let dc = dependencyContainer else {
                migrationError = "DependencyContainer не настроен"
                isMigratingStorage = false
                return
            }

            let currentProvider = dc.tokenProvider

            let exportData = await currentProvider.exportAllData()

            let newProvider: any TokenProvider
            switch newType {
            case .userDefaults:
                newProvider = UserDefaultsTokenProvider()
            case .keychain:
                newProvider = KeychainTokenProvider(configuration: .default)
            case .keychainICloud:
                newProvider = KeychainTokenProvider(configuration: .withICloud)
            }

            try await newProvider.importAllData(exportData)

            let settings = TokenStorageSettings()
            settings.storageType = newType
            settings.migrationCompleted()

            selectedStorageType = newType

            await currentProvider.clearAll()

            showRestartRequired = true

        } catch {
            migrationError = "Ошибка миграции: \(error.localizedDescription)"
        }

        isMigratingStorage = false
    }
}
