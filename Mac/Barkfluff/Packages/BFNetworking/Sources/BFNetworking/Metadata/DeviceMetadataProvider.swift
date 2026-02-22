//
//  DeviceMetadataProvider.swift
//  BFNetworking
//
//  Метаданные устройства для gRPC-заголовков
//

import Foundation

/// Предоставляет метаданные устройства для gRPC-заголовков.
public struct DeviceMetadataProvider: Sendable {

    public static let shared = DeviceMetadataProvider()

    /// Имя устройства (напр. "MacBook Pro")
    public var deviceName: String {
        Host.current().localizedName ?? "Mac"
    }

    /// Название и версия ОС (напр. "macOS 26.0.0")
    public var osName: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        return "macOS \(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
    }

    /// Название приложения
    public let appName = "BarkFluff macOS"

    /// Версия приложения (из Bundle)
    public var appVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0.0"
    }
}
