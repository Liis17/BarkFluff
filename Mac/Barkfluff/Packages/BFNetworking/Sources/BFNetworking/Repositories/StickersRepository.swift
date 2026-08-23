//
//  StickersRepository.swift
//  BFNetworking
//
//  Репозиторий для работы со стикерпаками и стикерами (FilesApi).
//

import Foundation
import GRPCCore
import BFProto
import SwiftProtobuf

public actor StickersRepository: StickersRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    // MARK: - Public

    public func listStickerPacks(offset: Int32 = 0, size: Int32 = 200) async throws -> StickerPacksPage {
        var page = Barkfluff_Shared_PageRequest()
        page.offset = offset
        page.size = size

        var request = Barkfluff_Files_ListStickerPacksRequest()
        request.pagination = page
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { [self] client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.listStickerPacks(req)
                return StickerPacksPage(
                    packs: response.packs.map(toDTO),
                    totalCount: response.totalCount
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getStickerPack(packID: String) async throws -> StickerPackContent {
        var request = Barkfluff_Files_GetStickerPackRequest()
        request.packID = packID
        let req = request
        let mediaOrigin = await connectionManager.filesMediaOrigin

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { [self] client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getStickerPack(req)
                return StickerPackContent(
                    pack: toDTO(response.pack),
                    stickers: response.stickers.map { toDTO($0, mediaOrigin: mediaOrigin) }
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Mapping

    private nonisolated func toDTO(_ p: Barkfluff_Files_StickerPackInfo) -> StickerPackInfoDTO {
        StickerPackInfoDTO(
            id: p.id,
            creatorUserID: p.creatorUserID,
            coverStickerID: p.coverStickerID,
            name: p.name,
            description: p.description_p,
            createdAt: Self.date(from: p.createdAt),
            stickerCount: p.stickerCount
        )
    }

    private nonisolated func toDTO(_ s: Barkfluff_Files_StickerInfo, mediaOrigin: String?) -> StickerInfoDTO {
        StickerInfoDTO(
            id: s.id,
            stickerPackID: s.stickerPackID,
            fileID: s.fileID,
            previewFileID: s.previewFileID,
            emoji: s.emoji,
            addedAt: Self.date(from: s.addedAt),
            fileURL: ConnectionManager.rewriteHost(s.fileURL, mediaOrigin: mediaOrigin),
            previewURL: ConnectionManager.rewriteHost(s.previewURL, mediaOrigin: mediaOrigin)
        )
    }

    private static func date(from ts: SwiftProtobuf.Google_Protobuf_Timestamp) -> Date {
        Date(timeIntervalSince1970: TimeInterval(ts.seconds) + TimeInterval(ts.nanos) / 1_000_000_000)
    }
}
