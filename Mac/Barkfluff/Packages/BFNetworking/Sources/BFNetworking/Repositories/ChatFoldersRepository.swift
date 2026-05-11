//
//  ChatFoldersRepository.swift
//  BFNetworking
//
//  gRPC-репозиторий для работы с папками чатов (Users API).
//

import Foundation
import GRPCCore
import BFProto

public actor ChatFoldersRepository: ChatFoldersRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func getChatFolders() async throws -> [ChatFolderInfo] {
        let req = Barkfluff_Users_GetChatFoldersRequest()

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.getChatFolders(req)
                return response.folders.map(Self.map)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func createChatFolder(name: String, icon: String) async throws -> ChatFolderInfo {
        var request = Barkfluff_Users_CreateChatFolderRequest()
        request.folderName = name
        request.folderIcon = icon
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.createChatFolder(req)
                return Self.map(response.folder)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func updateChatFolder(
        folderID: String,
        name: String?,
        icon: String?,
        chatIDs: [String]?
    ) async throws -> ChatFolderInfo {
        var request = Barkfluff_Users_UpdateChatFolderRequest()
        request.folderID = folderID
        if let name { request.folderName = name }
        if let icon { request.folderIcon = icon }
        if let chatIDs {
            request.hasChatListUpdate_p = true
            request.chatList = chatIDs
        }
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.updateChatFolder(req)
                return Self.map(response.folder)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func deleteChatFolder(folderID: String) async throws {
        var request = Barkfluff_Users_DeleteChatFolderRequest()
        request.folderID = folderID
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.deleteChatFolder(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func addChatToFolder(folderID: String, chatID: String) async throws -> ChatFolderInfo {
        var request = Barkfluff_Users_AddChatToFolderRequest()
        request.folderID = folderID
        request.chatID = chatID
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.addChatToFolder(req)
                return Self.map(response.folder)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func removeChatFromFolder(folderID: String, chatID: String) async throws -> ChatFolderInfo {
        var request = Barkfluff_Users_RemoveChatFromFolderRequest()
        request.folderID = folderID
        request.chatID = chatID
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                let response = try await usersClient.removeChatFromFolder(req)
                return Self.map(response.folder)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func reorderChatFolders(orders: [(folderID: String, sortOrder: Int32)]) async throws {
        var request = Barkfluff_Users_ReorderChatFoldersRequest()
        request.orders = orders.map { pair in
            var order = Barkfluff_Users_ChatFolderOrder()
            order.folderID = pair.folderID
            order.sortOrder = pair.sortOrder
            return order
        }
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .users) { client in
                let usersClient = Barkfluff_Users_UsersApi.Client(wrapping: client)
                _ = try await usersClient.reorderChatFolders(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Mapping

    private static func map(_ proto: Barkfluff_Users_ChatFolderData) -> ChatFolderInfo {
        ChatFolderInfo(
            id: proto.folderID,
            name: proto.folderName,
            icon: proto.folderIcon,
            chatIDs: proto.chatList,
            sortOrder: proto.sortOrder
        )
    }
}
