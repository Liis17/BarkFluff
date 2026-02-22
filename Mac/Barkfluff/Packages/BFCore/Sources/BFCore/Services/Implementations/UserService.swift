//
//  UserService.swift
//  BFCore
//
//  Реализация сервиса пользователей
//

import Foundation
import BFNetworking

/// Реализация сервиса пользователей
public actor UserService: UserServiceProtocol {

    private let usersRepository: UsersRepositoryProtocol
    private let userCache: UserCache

    public init(usersRepository: UsersRepositoryProtocol, userCache: UserCache) {
        self.usersRepository = usersRepository
        self.userCache = userCache
    }

    // MARK: - UserServiceProtocol

    public func getUser(userID: Int64) async throws -> User {
        // Сначала проверяем кеш
        if let cached = await userCache.getUser(userID: userID) {
            return cached
        }

        let userInfo = try await usersRepository.getUser(userID: userID)
        let user = toDomainUser(userInfo)
        await userCache.setUser(user)
        return user
    }

    public func getCurrentUser() async throws -> User {
        let userInfo = try await usersRepository.getCurrentUser()
        let user = toDomainUser(userInfo)
        await userCache.setUser(user)
        return user
    }

    public func changeName(firstName: String, lastName: String) async throws {
        try await usersRepository.changeName(firstName: firstName, lastName: lastName)
        // Обновляем кеш текущего пользователя
        // TODO: Получить ID текущего пользователя и обновить кеш
    }

    public func changeUsername(newUsername: String) async throws {
        try await usersRepository.changeUsername(newUsername: newUsername)
    }

    public func changeBio(newBio: String) async throws {
        try await usersRepository.changeBio(newBio: newBio)
    }

    public func setProfilePicture(fileID: String) async throws {
        try await usersRepository.setProfilePicture(fileID: fileID)
    }

    public func checkUsernameExists(username: String) async throws -> Bool {
        try await usersRepository.checkExistUsername(username: username)
    }

    public func checkEmailExists(email: String) async throws -> Bool {
        try await usersRepository.checkExistEmail(email: email)
    }

    public func searchUsers(query: String, offset: Int32, size: Int32) async throws -> PagedResult<User> {
        let result = try await usersRepository.searchUsers(query: query, offset: offset, size: size)
        let users = result.users.map { toDomainUser($0) }
        return PagedResult(items: users, totalCount: result.totalCount, offset: offset, size: size)
    }

    public func getUserBadges(userID: Int64) async throws -> [UserBadge] {
        let badges = try await usersRepository.getUserBadges(userID: userID)
        return badges.map { UserBadge(id: $0.id, name: $0.name, description: $0.description, iconURL: $0.iconURL) }
    }

    // MARK: - Private helpers

    private func toDomainUser(_ info: UserInfo) -> User {
        User(
            id: info.id,
            firstName: info.firstName,
            lastName: info.lastName,
            username: info.username,
            email: info.email,
            bio: info.bio,
            registrationDate: info.registrationDate,
            profilePictureURL: info.profilePictureURL,
            profilePicturePreviewURL: info.profilePicturePreviewURL,
            badges: []
        )
    }
}
