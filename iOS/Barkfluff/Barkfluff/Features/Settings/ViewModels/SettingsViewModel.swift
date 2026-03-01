//
//  SettingsViewModel.swift
//  Barkfluff
//
//  ViewModel для настроек (iOS версия)
//

import SwiftUI
import Observation
import BFCore
import BFNetworking

@Observable
final class SettingsViewModel {

    // MARK: - Dependencies

    weak var dependencyContainer: DependencyContainer?

    // MARK: - Token Storage

    /// Доступные типы хранилищ токенов
    var availableStorageTypes: [TokenStorageType] {
        [.userDefaults, .keychain, .keychainICloud]
    }

    /// Текущий тип хранилища
    var currentStorageType: TokenStorageType {
        dependencyContainer?.tokenStorageType ?? .userDefaults
    }

    // MARK: - Sessions

    var sessions: [Session] = []
    var isLoadingSessions = false
    var sessionsError: String?

    // MARK: - Init

    init() {}

    // MARK: - Sessions

    func loadSessions() async {
        // TODO: Реализовать при добавлении метода в AuthService
        isLoadingSessions = false
    }

    func terminateSession(_ session: Session) async {
        // TODO: Реализовать при добавлении метода в AuthService
    }

    func terminateAllOtherSessions() async {
        // TODO: Реализовать при добавлении метода в AuthService
    }
}
