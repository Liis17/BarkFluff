//
//  TokenExportData.swift
//  BFNetworking
//
//  Данные для миграции между хранилищами токенов
//

import Foundation

/// Данные для миграции между хранилищами
public struct TokenExportData: Sendable, Codable {
    public let accessToken: String?
    public let accessTokenExpiresAt: Date?
    public let refreshToken: String?
    public let refreshTokenExpirationDate: Date?
    public let serverHost: String?
    public let serverPort: Int?
    public let deviceId: String?

    public init(
        accessToken: String?,
        accessTokenExpiresAt: Date?,
        refreshToken: String?,
        refreshTokenExpirationDate: Date?,
        serverHost: String?,
        serverPort: Int?,
        deviceId: String?
    ) {
        self.accessToken = accessToken
        self.accessTokenExpiresAt = accessTokenExpiresAt
        self.refreshToken = refreshToken
        self.refreshTokenExpirationDate = refreshTokenExpirationDate
        self.serverHost = serverHost
        self.serverPort = serverPort
        self.deviceId = deviceId
    }
}
