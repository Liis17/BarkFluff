//
//  UserServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса пользователей
//

import Foundation
import BFNetworking

/// Протокол сервиса пользователей
public protocol UserServiceProtocol: Sendable {
    /// Получить пользователя по ID
    func getUser(userID: Int64) async throws -> User

    /// Получить текущего пользователя
    func getCurrentUser() async throws -> User

    /// Изменить имя
    func changeName(firstName: String, lastName: String) async throws

    /// Изменить username
    func changeUsername(newUsername: String) async throws

    /// Изменить био
    func changeBio(newBio: String) async throws

    /// Установить аватар
    func setProfilePicture(fileID: String) async throws

    /// Получить персонализацию текущего пользователя (постер + список фонов чата)
    func getPersonalization() async throws -> PersonalizationInfo

    /// Обновить персонализацию: пишутся оба поля сразу,
    /// поэтому передавай актуальный `posterFileID`, иначе сервер обнулит постер.
    func updatePersonalization(posterFileID: String, backgroundFileIDs: [String]) async throws

    /// Получить fileID постера текущего пользователя (пустая строка = нет постера)
    func getProfilePoster() async throws -> String

    /// Установить постер текущего пользователя. Пустая строка = удалить постер.
    func setProfilePoster(fileID: String) async throws

    /// Получить настройки приватности текущего пользователя
    func getPrivacySettings() async throws -> PrivacySettingsInfo

    /// Обновить настройки приватности текущего пользователя
    func updatePrivacySettings(_ settings: PrivacySettingsInfo) async throws

    /// Проверить существование username
    func checkUsernameExists(username: String) async throws -> Bool

    /// Проверить существование email
    func checkEmailExists(email: String) async throws -> Bool

    /// Поиск пользователей
    func searchUsers(query: String, offset: Int32, size: Int32) async throws -> PagedResult<User>

    /// Получить бейджи пользователя
    func getUserBadges(userID: Int64) async throws -> [UserBadge]
}
