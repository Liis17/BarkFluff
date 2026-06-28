//
//  CloudSettingsViewModel.swift
//  Barkfluff
//
//  Облачное хранилище: общий объём, лимит, разбиение по типам файлов.
//

import SwiftUI
import Observation
import BFCore
import BFNetworking

@MainActor
@Observable
final class CloudSettingsViewModel {

    var info: BFCore.StorageInfo?
    var isRefreshing: Bool = false
    var errorMessage: String?

    weak var dependencyContainer: DependencyContainer?

    /// Все интересные для отображения типы — фиксированный порядок.
    /// `.unknown` намеренно опущен.
    let displayedTypes: [UploadFileType] = [
        .messageAttachmentImage,
        .messageAttachmentVideo,
        .messageAttachmentGif,
        .messageAttachmentDocument,
        .messageAttachmentAudio,
        .messageAttachmentVoice,
        .messageAttachmentSticker,
        .userAvatar,
        .userProfilePoster,
        .chatPicture
    ]

    func refresh() async {
        guard let dc = dependencyContainer else { return }
        isRefreshing = true
        errorMessage = nil
        do {
            info = try await dc.fileService.getStorageInfo()
        } catch {
            errorMessage = String(localized: "settings.cloud.error.load")
        }
        isRefreshing = false
    }

    /// Доля типа от использованного объёма (0...1). Если `usedBytes == 0` — 0.
    func fraction(for type: UploadFileType) -> Double {
        guard let info, info.usedBytes > 0 else { return 0 }
        let bytes = info.usedByType[type] ?? 0
        return Double(bytes) / Double(info.usedBytes)
    }

    /// Прогресс заполнения хранилища (0...1). Если лимит не задан — 0.
    var usedFraction: Double {
        guard let info, info.limitBytes > 0 else { return 0 }
        return min(1.0, Double(info.usedBytes) / Double(info.limitBytes))
    }
}

// MARK: - UI Helpers

extension UploadFileType {
    var displayName: String {
        switch self {
        case .userAvatar: return String(localized: "upload_file_type.user_avatar")
        case .messageAttachmentImage: return String(localized: "upload_file_type.message_image")
        case .messageAttachmentVideo: return String(localized: "upload_file_type.message_video")
        case .messageAttachmentGif: return String(localized: "upload_file_type.message_gif")
        case .messageAttachmentDocument: return String(localized: "upload_file_type.message_document")
        case .chatPicture: return String(localized: "upload_file_type.chat_picture")
        case .messageAttachmentAudio: return String(localized: "upload_file_type.message_audio")
        case .messageAttachmentVoice: return String(localized: "upload_file_type.message_voice")
        case .messageAttachmentSticker: return String(localized: "upload_file_type.message_sticker")
        case .userProfilePoster: return String(localized: "upload_file_type.user_poster")
        case .unknown: return String(localized: "upload_file_type.unknown")
        }
    }

    var systemImage: String {
        switch self {
        case .userAvatar: return "person.crop.circle"
        case .messageAttachmentImage: return "photo"
        case .messageAttachmentVideo: return "video"
        case .messageAttachmentGif: return "rectangle.stack.badge.play"
        case .messageAttachmentDocument: return "doc"
        case .chatPicture: return "person.2.crop.square.stack"
        case .messageAttachmentAudio: return "music.note"
        case .messageAttachmentVoice: return "waveform"
        case .messageAttachmentSticker: return "face.smiling"
        case .userProfilePoster: return "rectangle.fill.on.rectangle.fill"
        case .unknown: return "questionmark.square"
        }
    }

    var tintColor: Color {
        switch self {
        case .userAvatar: return .pink
        case .messageAttachmentImage: return .blue
        case .messageAttachmentVideo: return .purple
        case .messageAttachmentGif: return .orange
        case .messageAttachmentDocument: return .gray
        case .chatPicture: return .mint
        case .messageAttachmentAudio: return .green
        case .messageAttachmentVoice: return .teal
        case .messageAttachmentSticker: return .yellow
        case .userProfilePoster: return .indigo
        case .unknown: return .secondary
        }
    }
}
