//
//  SettingsViewModel.swift
//  Barkfluff
//
//  ViewModel для настроек
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

    // Зависимости (инжектятся извне)
    weak var dependencyContainer: DependencyContainer?

    init() {
        // Загружаем настройки хранилища
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
    /// Если соединение ещё не установлено — оставляет `serverInfo` как есть (UI покажет «Не подключено»).
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

        do {
            currentDeviceId = await dc.tokenProvider.deviceID
            sessions = try await dc.identityRepository.getActiveSessions()
        } catch {
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

        let otherSessions = sessions.filter { $0.deviceId != currentDeviceId }
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

