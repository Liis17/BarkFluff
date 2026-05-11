//
//  ChatFolderService.swift
//  BFCore
//
//  Сервис папок чатов (stale-while-revalidate).
//

import Foundation
import BFNetworking

public actor ChatFolderService: ChatFolderServiceProtocol {

    private let repository: ChatFoldersRepositoryProtocol
    private let localRepository: LocalChatFolderRepository

    public init(
        repository: ChatFoldersRepositoryProtocol,
        localRepository: LocalChatFolderRepository
    ) {
        self.repository = repository
        self.localRepository = localRepository
    }

    // MARK: - Read

    public func loadCached() async -> [ChatFolder] {
        (try? await localRepository.loadCachedFolders()) ?? []
    }

    public func refresh() async throws -> [ChatFolder] {
        let dtos = try await repository.getChatFolders()
        let folders = dtos
            .map(Self.toDomain)
            .sorted { $0.sortOrder < $1.sortOrder }
        try? await localRepository.replaceAll(folders)
        return folders
    }

    // MARK: - Write

    public func create(name: String, icon: String) async throws -> ChatFolder {
        let dto = try await repository.createChatFolder(name: name, icon: icon)
        let folder = Self.toDomain(dto)
        try? await localRepository.upsert(folder)
        return folder
    }

    public func update(folderID: String, name: String?, icon: String?, chatIDs: [String]?) async throws -> ChatFolder {
        let dto = try await repository.updateChatFolder(
            folderID: folderID,
            name: name,
            icon: icon,
            chatIDs: chatIDs
        )
        let folder = Self.toDomain(dto)
        try? await localRepository.upsert(folder)
        return folder
    }

    public func delete(folderID: String) async throws {
        try await repository.deleteChatFolder(folderID: folderID)
        try? await localRepository.delete(id: folderID)
    }

    public func addChat(folderID: String, chatID: String) async throws -> ChatFolder {
        let dto = try await repository.addChatToFolder(folderID: folderID, chatID: chatID)
        let folder = Self.toDomain(dto)
        try? await localRepository.upsert(folder)
        return folder
    }

    public func removeChat(folderID: String, chatID: String) async throws -> ChatFolder {
        let dto = try await repository.removeChatFromFolder(folderID: folderID, chatID: chatID)
        let folder = Self.toDomain(dto)
        try? await localRepository.upsert(folder)
        return folder
    }

    public func reorder(_ orders: [(folderID: String, sortOrder: Int32)]) async throws {
        try await repository.reorderChatFolders(orders: orders)
        // Обновим локальный sort_order, перетянув свежий снапшот.
        _ = try? await refresh()
    }

    // MARK: - Mapping

    private static func toDomain(_ dto: ChatFolderInfo) -> ChatFolder {
        ChatFolder(
            id: dto.id,
            name: dto.name,
            icon: dto.icon,
            chatIDs: dto.chatIDs,
            sortOrder: dto.sortOrder
        )
    }
}
