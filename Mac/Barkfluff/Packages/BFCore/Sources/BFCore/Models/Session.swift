//
//  Session.swift
//  BFCore
//
//  Модели сессий
//

import Foundation

/// Активная сессия пользователя
public struct Session: Identifiable, Hashable, Sendable {
    public let id: String
    public let deviceName: String
    public let deviceType: DeviceType
    public let lastActive: Date
    public let ipAddress: String?
    public let isCurrent: Bool

    public init(
        id: String,
        deviceName: String,
        deviceType: DeviceType,
        lastActive: Date,
        ipAddress: String? = nil,
        isCurrent: Bool = false
    ) {
        self.id = id
        self.deviceName = deviceName
        self.deviceType = deviceType
        self.lastActive = lastActive
        self.ipAddress = ipAddress
        self.isCurrent = isCurrent
    }
}

/// Тип устройства
public enum DeviceType: String, Sendable, Codable, CaseIterable {
    case mac
    case iphone
    case ipad
    case web
    case unknown

    public var displayName: String {
        switch self {
        case .mac: return "Mac"
        case .iphone: return "iPhone"
        case .ipad: return "iPad"
        case .web: return "Web"
        case .unknown: return "Неизвестно"
        }
    }

    public var systemImage: String {
        switch self {
        case .mac: return "desktopcomputer"
        case .iphone: return "iphone"
        case .ipad: return "ipad"
        case .web: return "globe"
        case .unknown: return "questionmark.circle"
        }
    }
}
