//
//  SecuritySettingsView.swift
//  Barkfluff
//
//  Настройки безопасности
//

import SwiftUI

struct SecuritySettingsView: View {
    @Bindable var viewModel: SettingsViewModel
    @State private var showChangePassword = false
    @State private var showStorageChangeConfirmation = false
    @State private var pendingStorageType: TokenStorageType?

    var body: some View {
        Form {
            // MARK: - Token Storage Section
            Section {
                if viewModel.isMigratingStorage {
                    HStack {
                        ProgressView()
                            .scaleEffect(0.8)
                        Text("settings.security.storage.migrating")
                    }
                } else {
                    Picker("settings.security.storage.picker", selection: $viewModel.selectedStorageType) {
                        ForEach(TokenStorageType.allCases) { type in
                            HStack {
                                Image(systemName: type.iconName)
                                Text(type.displayName)
                            }
                            .tag(type)
                        }
                    }
                    .onChange(of: viewModel.selectedStorageType) { oldValue, newValue in
                        if oldValue != newValue {
                            pendingStorageType = newValue
                            showStorageChangeConfirmation = true
                            // Временно возвращаем старое значение до подтверждения
                            viewModel.selectedStorageType = oldValue
                        }
                    }
                }

                if let warning = viewModel.selectedStorageType.warning {
                    HStack(spacing: 8) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundStyle(.orange)
                        Text(warning)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                if let error = viewModel.migrationError {
                    HStack(spacing: 8) {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundStyle(.red)
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
            } header: {
                Text("settings.security.storage.section")
            } footer: {
                Text("settings.security.storage.footer")
            }

            // MARK: - 2FA Section
            Section {
                Toggle("settings.security.two_fa.toggle", isOn: $viewModel.twoFactorEnabled)
                    .onChange(of: viewModel.twoFactorEnabled) { _, newValue in
                        Task {
                            if newValue {
                                await viewModel.enable2FA()
                            } else {
                                await viewModel.disable2FA()
                            }
                        }
                    }
            } header: {
                Text("settings.security.two_fa.section")
            } footer: {
                Text("settings.security.two_fa.footer")
            }

            // MARK: - Password Section
            Section {
                Button("settings.security.password.change") {
                    showChangePassword = true
                }
            } header: {
                Text("settings.security.password.section")
            }
        }
        .formStyle(.grouped)
        .padding()
        .sheet(isPresented: $showChangePassword) {
            ChangePasswordSheet()
        }
        .confirmationDialog(
            "settings.security.storage.switch_confirm.title",
            isPresented: $showStorageChangeConfirmation,
            presenting: pendingStorageType
        ) { newType in
            Button("settings.security.storage.switch_confirm.confirm") {
                Task {
                    await viewModel.switchTokenStorage(to: newType)
                }
            }
            Button("common.cancel", role: .cancel) {
                pendingStorageType = nil
            }
        } message: { newType in
            Text("settings.security.storage.switch_confirm.message \(newType.displayName) \(newType.warning ?? "")")
        }
        .alert("settings.security.restart_required.title", isPresented: $viewModel.showRestartRequired) {
            Button("common.ok") {
                // Пользователь должен сам перезапустить приложение
            }
        } message: {
            Text("settings.security.restart_required.message")
        }
    }
}

struct ChangePasswordSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var currentPassword = ""
    @State private var newPassword = ""
    @State private var confirmPassword = ""

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            Text("settings.security.password.change_title")
                .font(.title2)

            VStack(spacing: Theme.Spacing.md) {
                SecureField("settings.security.password.current", text: $currentPassword)
                    .textFieldStyle(.roundedBorder)
                SecureField("settings.security.password.new", text: $newPassword)
                    .textFieldStyle(.roundedBorder)
                SecureField("settings.security.password.confirm", text: $confirmPassword)
                    .textFieldStyle(.roundedBorder)
            }
            .frame(width: 300)

            HStack {
                Button("common.cancel") {
                    dismiss()
                }
                .keyboardShortcut(.cancelAction)

                Button("common.save") {
                    // TODO: Change password
                    dismiss()
                }
                .buttonStyle(.borderedProminent)
                .disabled(newPassword.isEmpty || newPassword != confirmPassword)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding()
        .frame(width: 400, height: 300)
    }
}

#Preview {
    SecuritySettingsView(viewModel: SettingsViewModel())
}
