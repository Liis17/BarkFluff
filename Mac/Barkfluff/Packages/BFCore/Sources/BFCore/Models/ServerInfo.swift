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

    public init(
        id: String = UUID().uuidString,
        name: String,
        version: String,
        description: String? = nil,
        color: ServerColor? = nil
    ) {
        self.id = id
        self.name = name
        self.version = version
        self.description = description
        self.color = color
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
