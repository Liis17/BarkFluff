//
//  GeneralSettingsView.swift
//  Barkfluff
//
//  Общие настройки приложения.
//

import SwiftUI

struct GeneralSettingsView: View {
    var body: some View {
        Form {
            Section("Уведомления") {
                Toggle("Показывать уведомления", isOn: .constant(true))
                Toggle("Звук уведомлений", isOn: .constant(true))
            }

            Section("Внешний вид") {
                Picker("Тема", selection: .constant(0)) {
                    Text("Системная").tag(0)
                    Text("Светлая").tag(1)
                    Text("Тёмная").tag(2)
                }
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

#Preview {
    GeneralSettingsView()
}
