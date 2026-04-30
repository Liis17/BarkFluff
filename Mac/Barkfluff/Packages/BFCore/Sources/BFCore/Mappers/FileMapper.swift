//
//  FileMapper.swift
//  BFCore
//
//  Маппинг файлов Proto ↔ Domain
//

import Foundation
import BFNetworking
import BFProto

/// Маппер для File и связанных типов
public enum FileMapper {

    /// Преобразовать Proto GetUploadUrlResponse в Domain FileUploadInfo
    public static func toDomain(_ proto: Barkfluff_Files_GetUploadUrlResponse) -> FileUploadInfo {
        FileUploadInfo(
            fileID: proto.fileID,
            uploadURL: proto.url,
            expiresIn: 0
        )
    }

    /// Преобразовать Proto CheckFileHashResponse в Domain FileCheckResult
    public static func toDomain(_ proto: Barkfluff_Files_CheckFileHashResponse) -> FileCheckResult {
        FileCheckResult(
            exists: !proto.fileID.isEmpty,
            fileID: proto.fileID.isEmpty ? nil : proto.fileID
        )
    }

    /// Преобразовать Proto GetUserStorageInfoResponse в Domain StorageInfo
    public static func toDomain(_ proto: Barkfluff_Files_GetUserStorageInfoResponse) -> StorageInfo {
        StorageInfo(
            usedBytes: proto.totalUsedStorage,
            limitBytes: proto.storageLimit
        )
    }

    /// Преобразовать Proto DownloadFileData в Domain FileDownloadInfo
    public static func toDomain(_ proto: Barkfluff_Files_GetTempDownloadUrlResponse.DownloadFileData) -> FileDownloadInfo {
        FileDownloadInfo(
            fileID: proto.fileID,
            url: proto.url,
            previewURL: proto.previewURL.isEmpty ? nil : proto.previewURL
        )
    }

    /// Преобразовать Domain UploadFileType в Proto UploadFileType
    public static func toProto(_ domain: UploadFileType) -> Barkfluff_Files_UploadFileType {
        switch domain {
        case .unknown:
            return .unknown
        case .userAvatar:
            return .userAvatar
        case .messageAttachmentImage:
            return .messageAttachmentImage
        case .messageAttachmentVideo:
            return .messageAttachmentVideo
        case .messageAttachmentGif:
            return .messageAttachmentGif
        case .messageAttachmentDocument:
            return .messageAttachmentDocument
        case .chatPicture:
            return .chatPicture
        case .messageAttachmentAudio:
            return .messageAttachmentAudio
        case .messageAttachmentVoice:
            return .messageAttachmentVoice
        case .messageAttachmentSticker:
            return .messageAttachmentSticker
        case .userProfilePoster:
            return .userProfilePoster
        }
    }

    /// Преобразовать Proto UploadFileType в Domain UploadFileType
    public static func toDomain(_ proto: Barkfluff_Files_UploadFileType) -> UploadFileType {
        switch proto {
        case .unknown:
            return .unknown
        case .userAvatar:
            return .userAvatar
        case .messageAttachmentImage:
            return .messageAttachmentImage
        case .messageAttachmentVideo:
            return .messageAttachmentVideo
        case .messageAttachmentGif:
            return .messageAttachmentGif
        case .messageAttachmentDocument:
            return .messageAttachmentDocument
        case .chatPicture:
            return .chatPicture
        case .messageAttachmentAudio:
            return .messageAttachmentAudio
        case .messageAttachmentVoice:
            return .messageAttachmentVoice
        case .messageAttachmentSticker:
            return .messageAttachmentSticker
        case .userProfilePoster:
            return .userProfilePoster
        case .UNRECOGNIZED:
            return .unknown
        }
    }
}
