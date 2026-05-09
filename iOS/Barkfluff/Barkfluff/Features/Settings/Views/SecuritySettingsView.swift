//
//  SecuritySettingsView.swift
//  Barkfluff (iOS)
//
//  Базовые настройки безопасности: 2FA, выбор хранилища токенов.
//

import SwiftUI
import BFNetworking

struct SecuritySettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = SettingsViewModel()

    var body: some View {
        Form {
            Section("Двухфакторная аутентификация") {
                Toggle("2FA", isOn: Binding(
                    get: { viewModel.twoFactorEnabled },
                    set: { newValue in
                        Task {
                            if newValue {
                                await viewModel.enable2FA()
                            } else {
                                await viewModel.disable2FA()
                            }
                        }
                    }
                ))
            }

            Section("Хранилище токенов") {
                Picker("Тип", selection: Binding(
                    get: { viewModel.selectedStorageType },
                    set: { newValue in
                        Task { await viewModel.switchTokenStorage(to: newValue) }
                    }
                )) {
                    ForEach(viewModel.availableStorageTypes, id: \.self) { type in
                        Text(type.iosDisplayName).tag(type)
                    }
                }
                if viewModel.isMigratingStorage {
                    HStack {
                        ProgressView()
                        Text("Перенос данных…")
                    }
                }
                if let error = viewModel.migrationError {
                    Text(error)
                        .font(.footnote)
                        .foregroundStyle(.red)
                }
                if viewModel.showRestartRequired {
                    Text("Перезапустите приложение, чтобы изменения вступили в силу.")
                        .font(.footnote)
                        .foregroundStyle(.orange)
                }
            }
        }
        .navigationTitle("Безопасность")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
        }
    }
}

private extension TokenStorageType {
    var iosDisplayName: String {
        switch self {
        case .userDefaults: return "UserDefaults"
        case .keychain: return "Keychain"
        case .keychainICloud: return "Keychain + iCloud"
        }
    }
}
