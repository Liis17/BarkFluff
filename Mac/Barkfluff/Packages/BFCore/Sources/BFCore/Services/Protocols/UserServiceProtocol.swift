//
//  UserServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса пользователей
//

import Foundation

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

    /// Проверить существование username
    func checkUsernameExists(username: String) async throws -> Bool

    /// Проверить существование email
    func checkEmailExists(email: String) async throws -> Bool

    /// Поиск пользователей
    func searchUsers(query: String, offset: Int32, size: Int32) async throws -> PagedResult<User>

    /// Получить бейджи пользователя
    func getUserBadges(userID: Int64) async throws -> [UserBadge]
}
