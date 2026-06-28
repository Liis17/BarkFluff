//
//  AboutAppSettingsView.swift
//  Barkfluff
//
//  Информация о приложении и системе.
//

import SwiftUI
import Foundation
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

            Section("settings.about_app.section.system") {
                LabeledContent("settings.about_app.os", value: info.osVersion)
                LabeledContent("settings.about_app.host", value: info.hostName)
                LabeledContent("settings.about_app.cpu", value: info.cpuModel)
                LabeledContent("settings.about_app.cores", value: "\(info.cpuCores)")
                LabeledContent("settings.about_app.memory", value: info.memoryTotal)
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

// MARK: - System Info

private struct SystemInfo {
    let appName: String
    let appVersion: String
    let appBuild: String
    let bundleID: String
    let osVersion: String
    let hostName: String
    let cpuModel: String
    let cpuCores: Int
    let memoryTotal: String

    static func collect() -> SystemInfo {
        let bundle = Bundle.main
        let info = bundle.infoDictionary ?? [:]
        // Имя/версия — тот же источник, что уходит на сервер в gRPC-метаданных (x-app-name / x-app-version).
        let metadata = DeviceMetadataProvider.shared
        let appName = metadata.appName
        let appVersion = metadata.appVersion
        let appBuild = info["CFBundleVersion"] as? String ?? "—"
        let bundleID = bundle.bundleIdentifier ?? "—"

        let proc = ProcessInfo.processInfo
        let v = proc.operatingSystemVersion
        let osVersion = "macOS \(v.majorVersion).\(v.minorVersion).\(v.patchVersion)"

        let host = Host.current().localizedName ?? proc.hostName
        let memBytes = Int64(proc.physicalMemory)
        let memoryTotal = ByteCountFormatter.string(fromByteCount: memBytes, countStyle: .memory)

        return SystemInfo(
            appName: appName,
            appVersion: appVersion,
            appBuild: appBuild,
            bundleID: bundleID,
            osVersion: osVersion,
            hostName: host,
            cpuModel: sysctlString("machdep.cpu.brand_string") ?? "—",
            cpuCores: proc.activeProcessorCount,
            memoryTotal: memoryTotal
        )
    }

    /// Прочитать sysctl-строку. Возвращает `nil`, если ключ не найден.
    private static func sysctlString(_ name: String) -> String? {
        var size: size_t = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return nil }
        var buffer = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else { return nil }
        return String(cString: buffer)
    }
}

#Preview {
    AboutAppSettingsView()
}
