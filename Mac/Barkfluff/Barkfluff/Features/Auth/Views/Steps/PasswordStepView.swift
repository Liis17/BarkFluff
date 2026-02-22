//
//  PasswordStepView.swift
//  Barkfluff
//
//  Шаг 5: Установка пароля (КРИТИЧНЫЙ шаг)
//

import SwiftUI
import BFCore

/// Шаг регистрации: установка пароля
struct PasswordStepView: View {
    @Bindable var data: RegistrationData
    let authService: AuthServiceProtocol

    @State private var showPassword = false
    @State private var validationError: String?
    @State private var isSaving = false
    @FocusState private var isFieldFocused: Bool

    var passwordResult: PasswordValidationResult {
        PasswordValidator.validate(data.password)
    }

    var passwordsMatch: Bool {
        !data.confirmPassword.isEmpty && data.password == data.confirmPassword
    }

    var isValid: Bool {
        passwordResult.isValid && passwordsMatch
    }

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "lock.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Поля ввода в glass-контейнере
            VStack(alignment: .leading, spacing: Theme.Spacing.md) {
                // Пароль
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    HStack {
                        Group {
                            if showPassword {
                                TextField("Пароль", text: $data.password)
                            } else {
                                SecureField("Пароль", text: $data.password)
                            }
                        }
                        .textFieldStyle(.roundedBorder)
                        .focused($isFieldFocused)

                        Button(action: { showPassword.toggle() }) {
                            Image(systemName: showPassword ? "eye.slash" : "eye")
                                .foregroundStyle(.secondary)
                        }
                        .buttonStyle(.plain)
                    }

                    // Индикатор силы пароля
                    if !data.password.isEmpty {
                        PasswordStrengthView(
                            strength: passwordResult.strength,
                            score: passwordResult.score
                        )
                    }
                }

                // Подтверждение пароля
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    SecureField("Подтвердите пароль", text: $data.confirmPassword)
                        .textFieldStyle(.roundedBorder)
                        .focused($isFieldFocused)

                    if !data.confirmPassword.isEmpty {
                        HStack(spacing: Theme.Spacing.xs) {
                            Image(systemName: passwordsMatch ? "checkmark.circle.fill" : "xmark.circle.fill")
                                .foregroundStyle(passwordsMatch ? .green : .red)
                            Text(passwordsMatch ? "Пароли совпадают" : "Пароли не совпадают")
                                .font(.caption)
                                .foregroundStyle(passwordsMatch ? .green : .red)
                        }
                    }
                }

                // Требования к паролю
                if !data.password.isEmpty {
                    PasswordRequirementsView(password: data.password)
                        .padding(.top, Theme.Spacing.sm)
                }

                // Ошибка
                if let error = validationError {
                    ErrorBannerView(message: error)
                }
            }
            .frame(maxWidth: 300)
        }
        .padding(Theme.Spacing.xl)
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: Theme.Radius.xl))
        .padding(Theme.Spacing.md) // Отступ для тени glass
        .onDisappear {
            isFieldFocused = false
        }
    }

    /// Сохраняет пароль на сервере
    func savePassword() async -> Bool {
        guard isValid else { return false }

        isSaving = true
        defer { isSaving = false }

        do {
            try await authService.setPassword(password: data.password)
            return true
        } catch {
            validationError = error.localizedDescription
            return false
        }
    }
}

#Preview {
    PasswordStepView(data: RegistrationData(), authService: DependencyContainer().authService)
        .padding()
}
