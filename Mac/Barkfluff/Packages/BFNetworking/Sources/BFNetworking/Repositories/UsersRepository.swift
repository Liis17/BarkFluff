//
//  UsersRepository.swift
//  BFNetworking
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

public actor UsersRepository: UsersRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func getUser(userID: Int64) async throws -> UserInfo {
        var request = Barkfluff_Users_GetUserRequest()
        request.userID = userID
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getUser(req)
                return self.mapUser(response.user)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getCurrentUser() async throws -> UserInfo {
        return try await getUser(userID: 0)
    }

    public func changeName(firstName: String, lastName: String) async throws {
        var request = Barkfluff_Users_ChangeNameRequest()
        request.firstName = firstName
        request.lastName = lastName
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.changeName(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func changeUsername(newUsername: String) async throws {
        var request = Barkfluff_Users_ChangeUsernameRequest()
        request.username = newUsername
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.changeUsername(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func changeBio(newBio: String) async throws {
        var request = Barkfluff_Users_ChangeBioRequest()
        request.bio = newBio
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.changeBio(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func setProfilePicture(fileID: String) async throws {
        var request = Barkfluff_Users_SetProfilePictureRequest()
        request.fileID = fileID
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.setProfilePicture(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func checkExistUsername(username: String) async throws -> Bool {
        var request = Barkfluff_Users_CheckExistUsernameRequest()
        request.username = username
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.checkExistUsername(req)
                return response.exist
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func checkExistEmail(email: String) async throws -> Bool {
        var request = Barkfluff_Users_CheckExistEmailRequest()
        request.email = email
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.checkExistEmail(req)
                return response.exist
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func searchUsers(query: String, offset: Int32, size: Int32) async throws -> (users: [UserInfo], totalCount: Int32) {
        var request = Barkfluff_Users_SearchUsersRequest()
        request.query = query
        var pagination = Barkfluff_Shared_PageRequest()
        pagination.offset = offset
        pagination.size = size
        request.pagination = pagination
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.searchUsers(req)
                let users = response.users.map { self.mapUser($0) }
                return (users: users, totalCount: response.totalCount)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getUserBadges(userID: Int64) async throws -> [UserBadgeInfo] {
        var request = Barkfluff_Users_GetUserBadgesRequest()
        request.userID = userID
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getUserBadges(req)
                return response.badges.map { userBadge in
                    UserBadgeInfo(
                        id: String(userBadge.badge.id),
                        name: userBadge.badge.name,
                        description: userBadge.badge.description_p,
                        iconURL: userBadge.badge.imageURL.isEmpty ? nil : userBadge.badge.imageURL
                    )
                }
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Mapping Helpers

    private nonisolated func mapUser(_ user: Barkfluff_Users_User) -> UserInfo {
        let registrationDate: Date
        if user.hasRegistrationDate {
            let ts = user.registrationDate
            registrationDate = Date(timeIntervalSince1970: TimeInterval(ts.seconds) + TimeInterval(ts.nanos) / 1_000_000_000)
        } else {
            registrationDate = Date()
        }

        return UserInfo(
            id: user.id,
            firstName: user.firstName,
            lastName: user.lastName,
            username: user.username,
            email: nil,
            bio: user.bio.isEmpty ? nil : user.bio,
            profilePictureURL: user.profilePicture.isEmpty ? nil : user.profilePicture,
            profilePicturePreviewURL: user.profilePicturePreview.isEmpty ? nil : user.profilePicturePreview,
            registrationDate: registrationDate
        )
    }
}
