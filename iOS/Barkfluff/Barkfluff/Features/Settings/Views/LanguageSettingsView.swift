//
//  LanguageSettingsView.swift
//  Barkfluff (iOS)
//
//  Выбор языка интерфейса: системный / русский / английский.
//  Смена применяется мгновенно через `.environment(\.locale,)` на корневой Scene.
//

import SwiftUI

struct LanguageSettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var loc = container.localizationSettings

        Form {
            Section {
                Picker("settings.language.picker", selection: $loc.language) {
                    ForEach(AppLanguage.allCases) { lang in
                        Text(lang.titleKey).tag(lang)
                    }
                }
                .pickerStyle(.inline)
                .labelsHidden()
            } header: {
                Text("settings.language.section")
            } footer: {
                Text("settings.language.footer")
            }
        }
        .navigationTitle("settings.category.language")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        LanguageSettingsView()
            .environment(DependencyContainer())
    }
}
