//
//  CacheSettingsViewModel.swift
//  Barkfluff (iOS)
//

import SwiftUI
import Observation
import BFCore

@MainActor
@Observable
final class CacheSettingsViewModel {

    var stats: CacheStats = .empty
    var isClearing: Bool = false
    var isRefreshing: Bool = false

    weak var dependencyContainer: DependencyContainer?

    let displayedTypes: [CachedFileType] = [
        .image, .video, .gif, .preview, .avatar,
        .document, .audio, .voice, .sticker
    ]

    func refreshStats() async {
        guard let dc = dependencyContainer else { return }
        isRefreshing = true
        stats = await dc.mediaCacheManager.stats()
        isRefreshing = false
    }

    func clear(_ type: CachedFileType) async {
        guard let dc = dependencyContainer else { return }
        isClearing = true
        await dc.mediaCacheManager.clear(types: [type])
        await refreshStats()
        isClearing = false
    }

    func clearAll() async {
        guard let dc = dependencyContainer else { return }
        isClearing = true
        await dc.mediaCacheManager.clearAll()
        try? await dc.localChatRepository.clear()
        try? await dc.localMessageRepository.clearAll()
        await refreshStats()
        isClearing = false
    }
}

// MARK: - UI Helpers

extension CachedFileType {
    var displayName: String {
        switch self {
        case .avatar: return "Аватары"
        case .image: return "Изображения"
        case .poster: return "Постеры"
        case .video: return "Видео"
        case .gif: return "GIF"
        case .document: return "Документы"
        case .audio: return "Аудио"
        case .voice: return "Голосовые"
        case .sticker: return "Стикеры"
        case .preview: return "Превью"
        }
    }

    var systemImage: String {
        switch self {
        case .avatar: return "person.crop.circle"
        case .image: return "photo"
        case .poster: return "photo.on.rectangle.angled"
        case .video: return "video"
        case .gif: return "rectangle.stack.badge.play"
        case .document: return "doc"
        case .audio: return "music.note"
        case .voice: return "waveform"
        case .sticker: return "face.smiling"
        case .preview: return "rectangle.on.rectangle"
        }
    }

    var tintColor: Color {
        switch self {
        case .avatar: return .pink
        case .image: return .blue
        case .poster: return .indigo
        case .video: return .purple
        case .gif: return .orange
        case .document: return .gray
        case .audio: return .green
        case .voice: return .teal
        case .sticker: return .yellow
        case .preview: return .cyan
        }
    }
}
