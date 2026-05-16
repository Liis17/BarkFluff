//
//  NotificationsSettingsView.swift
//  Barkfluff
//
//  Настройки уведомлений и звука. Тогглеры байндятся к
//  DependencyContainer.notificationSettings (UserDefaults-backed).
//

import SwiftUI

struct NotificationsSettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var settings = container.notificationSettings

        Form {
            Section("settings.notifications.section") {
                Toggle("settings.notifications.show", isOn: $settings.showNotifications)
                Toggle("settings.notifications.sound", isOn: $settings.playSound)
                    .disabled(!settings.showNotifications)
            }
        }
        .formStyle(.grouped)
        .padding()
        .onChange(of: settings.showNotifications) { _, newValue in
            // При выключении — стираем уже показанные баннеры, чтобы они не
            // висели в Notification Center после того, как фича отключена.
            if !newValue {
                container.notificationService.removeAllDelivered()
            }
        }
    }
}

#Preview {
    NotificationsSettingsView()
        .environment(DependencyContainer())
}
