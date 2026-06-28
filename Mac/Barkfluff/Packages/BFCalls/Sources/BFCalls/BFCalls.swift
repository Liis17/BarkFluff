//
//  BFCalls.swift
//  BFCalls
//
//  Модели звонка для Apple-клиентов. Оркестрация — CallController.
//  Сигнализация: BFNetworking (CallsRepository). Медиа: LiveKit SFU.
//

import Foundation
import BFNetworking
import LiveKit

// MARK: - Фаза звонка

public enum CallPhase: Sendable, Equatable {
    case idle        // нет активного звонка
    case outgoing    // инициировали, идёт вызов
    case incoming    // входящий ринг
    case connecting  // подключаемся к LiveKit-комнате
    case active      // в разговоре
    case ended       // завершается
}

// MARK: - Текущий звонок

public struct ActiveCallInfo: Sendable, Equatable {
    public var callID: String
    /// Собеседник: callee (исходящий) либо caller (входящий). nil для группового.
    public let peerUserID: Int64?
    /// Чат группового звонка (Guid). nil для личного.
    public let chatID: String?
    public let media: CallMediaTypeDTO
    public let isGroup: Bool
    public let isIncoming: Bool

    public init(callID: String, peerUserID: Int64?, chatID: String?, media: CallMediaTypeDTO, isGroup: Bool, isIncoming: Bool) {
        self.callID = callID
        self.peerUserID = peerUserID
        self.chatID = chatID
        self.media = media
        self.isGroup = isGroup
        self.isIncoming = isIncoming
    }
}

// MARK: - Плитка участника (для сетки группового звонка / собеседника 1-на-1)

public struct CallTile: Identifiable {
    /// identity участника (userId-строка), для скриншер-плитки — с суффиксом "#screen".
    public let id: String
    public let userID: Int64?
    public var isSpeaking: Bool
    public let isScreenShare: Bool
    /// Видеотрек (камера или демонстрация экрана). nil — рисуем аватар.
    public var videoTrack: VideoTrack?

    public init(id: String, userID: Int64?, isSpeaking: Bool, isScreenShare: Bool, videoTrack: VideoTrack?) {
        self.id = id
        self.userID = userID
        self.isSpeaking = isSpeaking
        self.isScreenShare = isScreenShare
        self.videoTrack = videoTrack
    }
}

// MARK: - Отображение участника (имя/аватар, резолвится приложением)

public struct CallParticipantDisplay: Sendable, Equatable {
    public let name: String
    public let initials: String
    public let avatarURL: String?

    public init(name: String, initials: String, avatarURL: String?) {
        self.name = name
        self.initials = initials
        self.avatarURL = avatarURL
    }
}

// MARK: - Качество видео (локальное, у публикующего)

public enum CallVideoQualityLevel: Sendable, Hashable, CaseIterable {
    case auto
    case low
    case medium
    case high

    /// Пресет публикации видео (размер/fps/битрейт). nil = авто (дефолт SDK). Зеркалит веб (calls-ui.js).
    var preset: (width: Int32, height: Int32, fps: Int, bitrate: Int)? {
        switch self {
        case .auto:   return nil
        case .low:    return (640, 360, 24, 400_000)
        case .medium: return (960, 540, 25, 1_000_000)
        case .high:   return (1280, 720, 30, 1_700_000)
        }
    }
}

// MARK: - Пресеты битрейта голоса (общие для звонка)

enum CallAudioPreset {
    /// Битрейт голоса по уровню качества. nil = авто (дефолт SDK). Зеркалит веб.
    static func bitrate(for quality: CallAudioQualityDTO) -> Int? {
        switch quality {
        case .auto:   return nil
        case .low:    return 14_000
        case .medium: return 24_000
        case .high:   return 48_000
        }
    }
}
