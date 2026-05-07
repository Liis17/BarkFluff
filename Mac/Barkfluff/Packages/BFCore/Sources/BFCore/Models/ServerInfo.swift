//
//  ServerInfo.swift
//  BFCore
//
//  Информация о сервере
//

import Foundation

/// Информация о сервере BarkFluff
public struct ServerInfo: Identifiable, Hashable, Sendable {
    public let id: String
    public let name: String
    public let version: String
    public let description: String?
    public let color: ServerColor?
    public let publicName: String?
    public let location: String?

    /// Отображаемое публичное имя в формате `@servername`. Пусто, если бэкенд его не отдал.
    public var publicHandle: String? {
        guard let name = publicName, !name.isEmpty else { return nil }
        return name.hasPrefix("@") ? name : "@\(name)"
    }

    public init(
        id: String = UUID().uuidString,
        name: String,
        version: String,
        description: String? = nil,
        color: ServerColor? = nil,
        publicName: String? = nil,
        location: String? = nil
    ) {
        self.id = id
        self.name = name
        self.version = version
        self.description = description
        self.color = color
        self.publicName = publicName
        self.location = location
    }
}

/// Цвет сервера (для branding)
public struct ServerColor: Hashable, Sendable, Codable {
    public let hex: String

    public init(hex: String) {
        self.hex = hex
    }

    public init(red: UInt8, green: UInt8, blue: UInt8) {
        self.hex = String(format: "#%02X%02X%02X", red, green, blue)
    }
}
