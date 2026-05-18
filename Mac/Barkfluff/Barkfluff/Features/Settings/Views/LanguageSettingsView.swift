//
//  LanguageSettingsView.swift
//  Barkfluff
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
                        (Text(verbatim: lang.flagEmoji + "  ") + Text(lang.titleKey))
                            .tag(lang)
                    }
                }
                .pickerStyle(.menu)
            } header: {
                Text("settings.language.section")
            } footer: {
                Text("settings.language.footer")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

#Preview {
    LanguageSettingsView()
        .environment(DependencyContainer())
}
