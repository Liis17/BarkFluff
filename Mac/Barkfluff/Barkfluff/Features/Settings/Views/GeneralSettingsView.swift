//
//  GeneralSettingsView.swift
//  Barkfluff
//
//  Общие настройки приложения.
//

import SwiftUI

struct GeneralSettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var appearance = container.appearanceSettings

        Form {
            Section("settings.general.appearance.section") {
                Picker("settings.general.theme.label", selection: $appearance.theme) {
                    ForEach(AppTheme.allCases) { theme in
                        Text(theme.titleKey).tag(theme)
                    }
                }
                .pickerStyle(.menu)
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

#Preview {
    GeneralSettingsView()
        .environment(DependencyContainer())
}
