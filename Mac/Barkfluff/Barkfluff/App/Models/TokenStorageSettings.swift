//
//  TokenStorageSettings.swift
//  Barkfluff
//
//  Настройки хранилища токенов
//

import Foundation

/// Тип хранилища токенов
enum TokenStorageType: String, CaseIterable, Identifiable, Codable {
    case userDefaults = "userDefaults"
    case keychain = "keychain"
    case keychainICloud = "keychainICloud"

    var id: String { rawValue }

    /// Отображаемое название
    var displayName: String {
        switch self {
        case .userDefaults:
            return String(localized: "token_storage.user_defaults.name")
        case .keychain:
            return String(localized: "token_storage.keychain.name")
        case .keychainICloud:
            return String(localized: "token_storage.keychain_icloud.name")
        }
    }

    /// Описание для пользователя
    var description: String {
        switch self {
        case .userDefaults:
            return String(localized: "token_storage.user_defaults.desc")
        case .keychain:
            return String(localized: "token_storage.keychain.desc")
        case .keychainICloud:
            return String(localized: "token_storage.keychain_icloud.desc")
        }
    }

    /// Предупреждение (если есть)
    var warning: String? {
        switch self {
        case .userDefaults:
            return nil
        case .keychain:
            return String(localized: "token_storage.keychain.warning")
        case .keychainICloud:
            return String(localized: "token_storage.keychain_icloud.warning")
        }
    }

    /// Иконка
    var iconName: String {
        switch self {
        case .userDefaults:
            return "folder"
        case .keychain:
            return "key"
        case .keychainICloud:
            return "icloud"
        }
    }
}

/// Менеджер настроек хранилища токенов
@Observable
final class TokenStorageSettings {

    /// Ключ в UserDefaults для сохранения выбора
    private static let storageTypeKey = "com.barkfluff.tokenStorage.type"

    /// Текущий выбранный тип хранилища
    var storageType: TokenStorageType {
        get { _storageType }
        set {
            if newValue != _storageType {
                pendingMigration = true
            }
            _storageType = newValue
            UserDefaults.standard.set(newValue.rawValue, forKey: Self.storageTypeKey)
        }
    }

    private var _storageType: TokenStorageType

    /// Флаг: нужна ли миграция (тип изменился)
    private(set) var pendingMigration: Bool = false

    /// Тип хранилища ДО изменения (для миграции)
    private(set) var previousStorageType: TokenStorageType

    init() {
        // Загружаем сохранённый тип
        let initialType: TokenStorageType
        if let savedRaw = UserDefaults.standard.string(forKey: Self.storageTypeKey),
           let saved = TokenStorageType(rawValue: savedRaw) {
            initialType = saved
        } else {
            // По умолчанию - UserDefaults
            initialType = .userDefaults
        }
        self._storageType = initialType
        self.previousStorageType = initialType
    }

    /// Сбросить флаг миграции после успешной миграции
    func migrationCompleted() {
        previousStorageType = _storageType
        pendingMigration = false
    }

    /// Получить начальный тип хранилища (для DependencyContainer)
    static func initialStorageType() -> TokenStorageType {
        if let savedRaw = UserDefaults.standard.string(forKey: storageTypeKey),
           let saved = TokenStorageType(rawValue: savedRaw) {
            return saved
        }
        return .userDefaults
    }
}
