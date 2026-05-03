//
//  SharedMediaService.swift
//  BFCore
//
//  Реализация сервиса общего медиа
//

import Foundation
import BFNetworking

/// Реализация сервиса для получения общего медиа в чате
public actor SharedMediaService: SharedMediaServiceProtocol {

    private let messagesRepository: MessagesRepositoryProtocol
    private let fileService: FileServiceProtocol

    /// Размер страницы для загрузки
    private let pageSize: Int32 = 50

    public init(
        messagesRepository: MessagesRepositoryProtocol,
        fileService: FileServiceProtocol
    ) {
        self.messagesRepository = messagesRepository
        self.fileService = fileService
    }

    // MARK: - SharedMediaServiceProtocol

    public func loadSharedMedia(
        chatID: String,
        offset: Int32,
        filter: SharedMediaFilter
    ) async throws -> (items: [SharedMediaItem], hasMore: Bool) {
        // Определяем фильтр по типу вложения
        let attachmentFilter: BFNetworking.AttachmentType? = switch filter {
        case .media:
            nil // для media загружаем все типы (image, video, gif) - фильтруем на клиенте
        case .documents:
            .document
        }

        // Вызываем новый API
        let result = try await messagesRepository.listChatAttachments(
            chatID: chatID,
            offset: offset,
            size: pageSize,
            filter: attachmentFilter,
            sortDescending: true
        )

        // Маппим в доменные модели
        var items: [SharedMediaItem] = []

        for att in result.attachments {
            // Для фильтра .media пропускаем документы на клиенте
            if filter == .media {
                guard att.type == .image || att.type == .video || att.type == .gif else {
                    continue
                }
            }

            items.append(SharedMediaItem(
                id: att.id,
                messageID: att.messageID,
                type: mapAttachmentType(att.type),
                fileID: att.fileID,
                previewURL: att.previewURL,
                previewFileID: att.previewFileID,
                fileName: att.fileName,
                fileSize: att.fileSize,
                sentAt: att.sentAt,
                senderID: att.senderID
            ))
        }

        // Определяем, есть ли ещё данные
        let hasMore = offset + Int32(items.count) < result.totalCount

        return (items: items, hasMore: hasMore)
    }

    // MARK: - Private Helpers

    private nonisolated func mapAttachmentType(_ type: BFNetworking.AttachmentType) -> AttachmentType {
        switch type {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        case .audio: return .audio
        case .voice: return .voice
        case .sticker: return .sticker
        case .forwardedMessage: return .forwardedMessage
        }
    }
}
