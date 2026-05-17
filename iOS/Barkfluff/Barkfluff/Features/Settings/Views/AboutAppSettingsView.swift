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
            Section("settings.about_app.section.app") {
                LabeledContent("settings.about_app.name", value: info.appName)
                LabeledContent("settings.about_app.version", value: info.appVersion)
                LabeledContent("settings.about_app.build", value: info.appBuild)
                LabeledContent("settings.about_app.bundle_id", value: info.bundleID)
            }

            Section("settings.about_app.section.device") {
                LabeledContent("settings.about_app.os", value: info.osVersion)
                LabeledContent("settings.about_app.model", value: info.deviceModel)
                LabeledContent("settings.about_app.device_name", value: info.deviceName)
                LabeledContent("settings.about_app.memory", value: info.memoryTotal)
            }
        }
        .navigationTitle("settings.category.about_app")
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
