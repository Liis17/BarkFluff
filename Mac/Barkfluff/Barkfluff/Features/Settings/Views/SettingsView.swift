//
//  SettingsView.swift
//  Barkfluff
//
//  Настройки приложения
//

import SwiftUI

struct SettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = SettingsViewModel()

    var body: some View {
        TabView {
            GeneralSettingsView(viewModel: viewModel)
                .tabItem {
                    Label("Основные", systemImage: "gear")
                }

            SessionsView(viewModel: viewModel)
                .tabItem {
                    Label("Сессии", systemImage: "laptop.trianglebadge.exclamationmark")
                }

            SecuritySettingsView(viewModel: viewModel)
                .tabItem {
                    Label("Безопасность", systemImage: "lock.shield")
                }

            StorageSettingsView(viewModel: viewModel)
                .tabItem {
                    Label("Хранилище", systemImage: "externaldrive")
                }

            AboutView(viewModel: viewModel)
                .tabItem {
                    Label("О сервере", systemImage: "info.circle")
                }
        }
        .frame(minWidth: 500, minHeight: 400)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.loadSettings()
        }
    }
}

struct GeneralSettingsView: View {
    let viewModel: SettingsViewModel

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

struct StorageSettingsView: View {
    let viewModel: SettingsViewModel

    var body: some View {
        Form {
            Section("Использование хранилища") {
                if let storageInfo = viewModel.storageInfo {
                    VStack(alignment: .leading) {
                        Text("Использовано: \(storageInfo.usedGB) GB из \(storageInfo.limitGB) GB")
                        ProgressView(value: storageInfo.usedPercentage)
                            .tint(storageInfo.usedPercentage > 0.9 ? .red : .accentColor)
                    }
                } else {
                    Text("Загрузка...")
                }
            }

            Section {
                Button("Очистить кеш") {
                    // TODO: Clear cache
                }
            }
        }
        .formStyle(.grouped)
        .padding()
    }
}

struct AboutView: View {
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
    SettingsView()
}
