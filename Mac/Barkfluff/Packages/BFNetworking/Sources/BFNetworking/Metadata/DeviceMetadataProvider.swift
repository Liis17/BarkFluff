//
//  DeviceMetadataProvider.swift
//  BFNetworking
//
//  Метаданные устройства для gRPC-заголовков
//

import Foundation

#if canImport(UIKit)
import UIKit
#endif

/// Предоставляет метаданные устройства для gRPC-заголовков.
public struct DeviceMetadataProvider: Sendable {

    public static let shared = DeviceMetadataProvider()

    /// Имя устройства (напр. "MacBook Pro" или "iPhone 17")
    public var deviceName: String {
        #if os(iOS)
        return UIDevice.current.name
        #else
        return Host.current().localizedName ?? "Mac"
        #endif
    }

    /// Название и версия ОС (напр. "macOS 26.0.0" или "iOS 26.2.0")
    public var osName: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        #if os(iOS)
        return "iOS \(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
        #else
        return "macOS \(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
        #endif
    }

    /// Название приложения
    public var appName: String {
        #if os(iOS)
        return "BarkFluff iOS"
        #else
        return "BarkFluff macOS"
        #endif
    }

    /// Версия приложения (из Bundle)
    public var appVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0.0"
    }
}
