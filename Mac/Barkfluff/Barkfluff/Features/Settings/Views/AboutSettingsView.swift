//
//  AboutSettingsView.swift
//  Barkfluff
//
//  Информация о сервере и приложении.
//

import SwiftUI

struct AboutSettingsView: View {
    let viewModel: SettingsViewModel

    var body: some View {
        Form {
            Section("Информация о сервере") {
                if let serverInfo = viewModel.serverInfo {
                    LabeledContent("Имя сервера", value: serverInfo.name)
                    LabeledContent("Версия", value: serverInfo.version)
                    if let description = serverInfo.description {
                        Text(description)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                } else {
                    Text("Не подключено")
                        .foregroundStyle(.secondary)
                }
            }

            Section("Приложение") {
                LabeledContent("Версия", value: "1.0.0")
                LabeledContent("Сборка", value: "1")
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

#Preview {
    AboutSettingsView(viewModel: SettingsViewModel())
}
