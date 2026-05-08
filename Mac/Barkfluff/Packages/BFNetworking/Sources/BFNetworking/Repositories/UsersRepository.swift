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

    public func getCurrentDevice() async throws -> DeviceInfo? {
        let req = Barkfluff_Users_GetCurrentDeviceRequest()

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getCurrentDevice(req)
                guard response.hasDevice else { return nil }
                return self.mapDevice(response.device)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
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

    public func getPersonalization() async throws -> PersonalizationInfo {
        let req = Barkfluff_Users_GetPersonalizationRequest()

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getPersonalization(req)
                return PersonalizationInfo(
                    profilePosterFileID: response.personalization.profilePosterFileID,
                    chatBackgroundFileIDs: response.personalization.chatBackgroundFileIds
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func updatePersonalization(posterFileID: String, backgroundFileIDs: [String]) async throws {
        var data = Barkfluff_Users_UserPersonalizationData()
        data.profilePosterFileID = posterFileID
        data.chatBackgroundFileIds = backgroundFileIDs

        var request = Barkfluff_Users_UpdatePersonalizationRequest()
        request.personalization = data
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.updatePersonalization(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getProfilePoster() async throws -> String {
        let req = Barkfluff_Users_GetProfilePosterRequest()

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getProfilePoster(req)
                return response.profilePosterFileID
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func setProfilePoster(fileID: String) async throws {
        var request = Barkfluff_Users_SetProfilePosterRequest()
        request.profilePosterFileID = fileID
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.setProfilePoster(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getPrivacySettings() async throws -> PrivacySettingsInfo {
        let req = Barkfluff_Users_GetPrivacySettingsRequest()

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getPrivacySettings(req)
                return self.mapPrivacySettings(response.settings)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func updatePrivacySettings(_ settings: PrivacySettingsInfo) async throws {
        var proto = Barkfluff_Users_PrivacySettings()
        proto.profileVisibleOnSite = settings.profileVisibleOnSite
        proto.avatarVisibility = mapVisibilityToProto(settings.avatarVisibility)
        proto.bioVisibility = mapVisibilityToProto(settings.bioVisibility)
        proto.emailVisibility = mapVisibilityToProto(settings.emailVisibility)
        proto.searchVisible = settings.searchVisible
        proto.onlineVisibility = mapVisibilityToProto(settings.onlineVisibility)

        var request = Barkfluff_Users_UpdatePrivacySettingsRequest()
        request.settings = proto
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.updatePrivacySettings(req)
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

    private nonisolated func mapPrivacySettings(_ p: Barkfluff_Users_PrivacySettings) -> PrivacySettingsInfo {
        PrivacySettingsInfo(
            profileVisibleOnSite: p.profileVisibleOnSite,
            avatarVisibility: mapVisibilityFromProto(p.avatarVisibility),
            bioVisibility: mapVisibilityFromProto(p.bioVisibility),
            emailVisibility: mapVisibilityFromProto(p.emailVisibility),
            searchVisible: p.searchVisible,
            onlineVisibility: mapVisibilityFromProto(p.onlineVisibility)
        )
    }

    private nonisolated func mapVisibilityToProto(_ v: ProfileFieldVisibility) -> Barkfluff_Users_ProfileFieldVisibility {
        switch v {
        case .all: return .all
        case .friends: return .friends
        case .none: return .none
        }
    }

    private nonisolated func mapVisibilityFromProto(_ v: Barkfluff_Users_ProfileFieldVisibility) -> ProfileFieldVisibility {
        switch v {
        case .all: return .all
        case .friends: return .friends
        case .none: return .none
        case .UNRECOGNIZED: return .all
        }
    }

    private nonisolated func mapDevice(_ device: Barkfluff_Users_Device) -> DeviceInfo {
        let authorizedAt: Date
        if device.hasAuthorizedAt {
            let ts = device.authorizedAt
            authorizedAt = Date(timeIntervalSince1970: TimeInterval(ts.seconds) + TimeInterval(ts.nanos) / 1_000_000_000)
        } else {
            authorizedAt = Date()
        }

        return DeviceInfo(
            deviceId: device.deviceID,
            userId: device.userID,
            originalName: device.originalName,
            customName: device.customName,
            authorizedAt: authorizedAt,
            appName: device.appName,
            operationSystem: device.operationSystem,
            location: device.location,
            notificationsEnabled: device.notificationsEnabled
        )
    }

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
            profilePosterFileID: user.profilePosterFileID.isEmpty ? nil : user.profilePosterFileID,
            registrationDate: registrationDate
        )
    }
}
