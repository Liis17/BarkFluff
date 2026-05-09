//
//  GeneralSettingsView.swift
//  Barkfluff (iOS)
//

import SwiftUI

struct GeneralSettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var appearance = container.appearanceSettings

        Form {
            Section("Внешний вид") {
                Picker("Тема", selection: $appearance.theme) {
                    ForEach(AppTheme.allCases) { theme in
                        Text(theme.title).tag(theme)
                    }
                }
            }
        }
        .navigationTitle("Общие")
        .navigationBarTitleDisplayMode(.inline)
    }
}
