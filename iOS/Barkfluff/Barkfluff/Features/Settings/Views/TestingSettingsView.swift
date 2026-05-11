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
            Section("Отладка") {
                Toggle("Показывать ID пользователей", isOn: $dev.showUserIDs)
                Toggle("Показывать ID чатов", isOn: $dev.showChatIDs)
            }

            Section {} footer: {
                Text("Локальные настройки только для этого устройства. Не отправляются на сервер.")
            }
        }
        .navigationTitle("Тестирование")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        TestingSettingsView()
            .environment(DependencyContainer())
    }
}
