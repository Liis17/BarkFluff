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
            Section("settings.security.two_fa.section") {
                Toggle("settings.security.two_fa.toggle_short", isOn: Binding(
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

            Section("settings.security.storage.section") {
                Picker("settings.security.storage.type_picker", selection: Binding(
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
                        Text("settings.security.storage.migrating_short")
                    }
                }
                if let error = viewModel.migrationError {
                    Text(error)
                        .font(.footnote)
                        .foregroundStyle(.red)
                }
                if viewModel.showRestartRequired {
                    Text("settings.security.restart_hint")
                        .font(.footnote)
                        .foregroundStyle(.orange)
                }
            }
        }
        .navigationTitle("settings.category.security")
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
