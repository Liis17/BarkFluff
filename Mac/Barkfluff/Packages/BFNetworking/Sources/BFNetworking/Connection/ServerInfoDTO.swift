//
//  ServerInfoDTO.swift
//  BFNetworking
//
//  Информация о сервере (networking-level DTO)
//

import Foundation

/// Информация о сервере, полученная от Beacon
public struct ServerInfoDTO: Sendable, Codable, Equatable {
    public let name: String
    public let version: String
    public let description: String?
    public let color: String?

    public init(name: String, version: String, description: String? = nil, color: String? = nil) {
        self.name = name
        self.version = version
        self.description = description
        self.color = color
    }
}
