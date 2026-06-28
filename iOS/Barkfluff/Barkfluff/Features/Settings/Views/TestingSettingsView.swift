//
//  TestingSettingsView.swift
//  Barkfluff (iOS)
//
//  Раздел «Тестирование» в Профиле. Локальные отладочные флаги, не передаются на сервер.
//

import SwiftUI

struct TestingSettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var dev = container.developerSettings

        Form {
            Section("settings.testing.section") {
                Toggle("settings.testing.show_user_ids", isOn: $dev.showUserIDs)
                Toggle("settings.testing.show_chat_ids", isOn: $dev.showChatIDs)
            }

            Section {} footer: {
                Text("settings.testing.footer")
            }
        }
        .navigationTitle("settings.category.testing")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        TestingSettingsView()
            .environment(DependencyContainer())
    }
}
