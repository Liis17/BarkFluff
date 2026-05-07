//
//  NotificationsSettingsView.swift
//  Barkfluff
//
//  Настройки уведомлений и звука.
//

import SwiftUI

struct NotificationsSettingsView: View {
    var body: some View {
        Form {
            Section("Уведомления") {
                Toggle("Показывать уведомления", isOn: .constant(true))
                Toggle("Звук уведомлений", isOn: .constant(true))
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

#Preview {
    NotificationsSettingsView()
}
