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
                        Text("Миграция данных...")
                    }
                } else {
                    Picker("Хранилище токенов", selection: $viewModel.selectedStorageType) {
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
                Text("Хранилище токенов")
            } footer: {
                Text("Токены авторизации хранятся на устройстве. iCloud позволяет синхронизировать их между устройствами.")
            }

            // MARK: - 2FA Section
            Section {
                Toggle("Двухфакторная аутентификация", isOn: $viewModel.twoFactorEnabled)
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
                Text("Двухфакторная аутентификация")
            } footer: {
                Text("При включении 2FA вам потребуется вводить код из приложения-аутентификатора при каждом входе")
            }

            // MARK: - Password Section
            Section {
                Button("Сменить пароль") {
                    showChangePassword = true
                }
            } header: {
                Text("Пароль")
            }
        }
        .formStyle(.grouped)
        .padding()
        .sheet(isPresented: $showChangePassword) {
            ChangePasswordSheet()
        }
        .confirmationDialog(
            "Переключить хранилище?",
            isPresented: $showStorageChangeConfirmation,
            presenting: pendingStorageType
        ) { newType in
            Button("Переключить") {
                Task {
                    await viewModel.switchTokenStorage(to: newType)
                }
            }
            Button("Отмена", role: .cancel) {
                pendingStorageType = nil
            }
        } message: { newType in
            Text("Данные будут скопированы в \(newType.displayName). \(newType.warning ?? "")")
        }
        .alert("Требуется перезапуск", isPresented: $viewModel.showRestartRequired) {
            Button("OK") {
                // Пользователь должен сам перезапустить приложение
            }
        } message: {
            Text("Хранилище токенов изменено. Пожалуйста, перезапустите приложение для применения изменений.")
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
            Text("Смена пароля")
                .font(.title2)

            VStack(spacing: Theme.Spacing.md) {
                SecureField("Текущий пароль", text: $currentPassword)
                    .textFieldStyle(.roundedBorder)
                SecureField("Новый пароль", text: $newPassword)
                    .textFieldStyle(.roundedBorder)
                SecureField("Подтвердите пароль", text: $confirmPassword)
                    .textFieldStyle(.roundedBorder)
            }
            .frame(width: 300)

            HStack {
                Button("Отмена") {
                    dismiss()
                }
                .keyboardShortcut(.cancelAction)

                Button("Сохранить") {
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
