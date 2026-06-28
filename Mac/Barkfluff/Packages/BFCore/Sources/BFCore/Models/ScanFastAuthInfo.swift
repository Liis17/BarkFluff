//
//  ScanFastAuthInfo.swift
//  BFCore
//
//  Метаданные нового устройства, полученные после сканирования QR
//  авторизованным клиентом (RPC ScanFastAuth).
//

import Foundation

/// Информация о новом устройстве, ожидающем подтверждения через FastAuth.
public struct ScanFastAuthInfo: Hashable, Sendable, Identifiable {
    public let fastAuthID: String
    public let deviceName: String
    public let operationSystem: String
    public let appName: String
    public let appVersion: String
    public let ipAddress: String
    public let confirmationCode: String
    public let expiresAt: Date

    public var id: String { fastAuthID }

    public init(
        fastAuthID: String,
        deviceName: String,
        operationSystem: String,
        appName: String,
        appVersion: String,
        ipAddress: String,
        confirmationCode: String,
        expiresAt: Date
    ) {
        self.fastAuthID = fastAuthID
        self.deviceName = deviceName
        self.operationSystem = operationSystem
        self.appName = appName
        self.appVersion = appVersion
        self.ipAddress = ipAddress
        self.confirmationCode = confirmationCode
        self.expiresAt = expiresAt
    }
}
