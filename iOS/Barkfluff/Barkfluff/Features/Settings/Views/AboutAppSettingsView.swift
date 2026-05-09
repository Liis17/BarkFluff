//
//  AboutAppSettingsView.swift
//  Barkfluff (iOS)
//
//  Информация о приложении и системе.
//

import SwiftUI
import UIKit
import BFNetworking

struct AboutAppSettingsView: View {
    private let info = SystemInfo.collect()

    var body: some View {
        Form {
            Section("Приложение") {
                LabeledContent("Название", value: info.appName)
                LabeledContent("Версия", value: info.appVersion)
                LabeledContent("Сборка", value: info.appBuild)
                LabeledContent("Bundle ID", value: info.bundleID)
            }

            Section("Устройство") {
                LabeledContent("OS", value: info.osVersion)
                LabeledContent("Модель", value: info.deviceModel)
                LabeledContent("Имя устройства", value: info.deviceName)
                LabeledContent("Память", value: info.memoryTotal)
            }
        }
        .navigationTitle("О приложении")
        .navigationBarTitleDisplayMode(.inline)
    }
}

private struct SystemInfo {
    let appName: String
    let appVersion: String
    let appBuild: String
    let bundleID: String
    let osVersion: String
    let deviceName: String
    let deviceModel: String
    let memoryTotal: String

    static func collect() -> SystemInfo {
        let bundle = Bundle.main
        let info = bundle.infoDictionary ?? [:]
        let metadata = DeviceMetadataProvider.shared
        let appName = metadata.appName
        let appVersion = metadata.appVersion
        let appBuild = info["CFBundleVersion"] as? String ?? "—"
        let bundleID = bundle.bundleIdentifier ?? "—"

        let proc = ProcessInfo.processInfo
        let v = proc.operatingSystemVersion
        let osVersion = "iOS \(v.majorVersion).\(v.minorVersion).\(v.patchVersion)"

        let device = UIDevice.current
        let memBytes = Int64(proc.physicalMemory)
        let memoryTotal = ByteCountFormatter.string(fromByteCount: memBytes, countStyle: .memory)

        return SystemInfo(
            appName: appName,
            appVersion: appVersion,
            appBuild: appBuild,
            bundleID: bundleID,
            osVersion: osVersion,
            deviceName: device.name,
            deviceModel: device.model,
            memoryTotal: memoryTotal
        )
    }
}
