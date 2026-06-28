//
//  PasswordStepView.swift
//  Barkfluff
//
//  Шаг 5: Установка пароля (iOS)
//

import SwiftUI

struct PasswordStepView: View {
    @Bindable var data: RegistrationData

    @State private var showPassword = false
    @State private var showConfirmPassword = false
    @FocusState private var focusedField: Field?

    enum Field: Hashable {
        case password
        case confirmPassword
    }

    var body: some View {
        VStack(spacing: 16) {
            // Пароль
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.password.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack(spacing: 8) {
                    Group {
                        if showPassword {
                            TextField("auth.register.step.password.placeholder", text: $data.password)
                        } else {
                            SecureField("auth.register.step.password.placeholder", text: $data.password)
                        }
                    }
                    .textContentType(.newPassword)
                    .focused($focusedField, equals: .password)

                    Button {
                        showPassword.toggle()
                    } label: {
                        Image(systemName: showPassword ? "eye.slash" : "eye")
                            .foregroundStyle(.secondary)
                    }
                    .buttonStyle(.plain)
                }
                .textFieldStyle(.roundedBorder)
            }

            // Подтверждение
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.password.confirm.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack(spacing: 8) {
                    Group {
                        if showConfirmPassword {
                            TextField("auth.register.step.password.confirm.placeholder", text: $data.confirmPassword)
                        } else {
                            SecureField("auth.register.step.password.confirm.placeholder", text: $data.confirmPassword)
                        }
                    }
                    .textContentType(.newPassword)
                    .focused($focusedField, equals: .confirmPassword)

                    Button {
                        showConfirmPassword.toggle()
                    } label: {
                        Image(systemName: showConfirmPassword ? "eye.slash" : "eye")
                            .foregroundStyle(.secondary)
                    }
                    .buttonStyle(.plain)
                }
                .textFieldStyle(.roundedBorder)

                // Индикатор совпадения
                if !data.confirmPassword.isEmpty {
                    HStack(spacing: 4) {
                        Image(systemName: passwordsMatch ? "checkmark.circle.fill" : "xmark.circle.fill")
                            .foregroundStyle(passwordsMatch ? .green : .red)
                        Text(passwordsMatch ? "auth.register.step.password.match" : "auth.register.step.password.mismatch")
                    }
                    .font(.caption)
                }
            }

            // Требования
            if !data.password.isEmpty {
                VStack(alignment: .leading, spacing: 4) {
                    Text("auth.register.step.password.requirements")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    requirementRow("auth.register.step.password.req.length", isMet: data.password.count >= 8)
                    requirementRow("auth.register.step.password.req.uppercase", isMet: hasUppercase)
                    requirementRow("auth.register.step.password.req.lowercase", isMet: hasLowercase)
                    requirementRow("auth.register.step.password.req.digit", isMet: hasDigit)
                    requirementRow("auth.register.step.password.req.special", isMet: hasSpecial)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .onAppear {
            focusedField = .password
        }
    }

    // MARK: - Requirement Row

    @ViewBuilder
    private func requirementRow(_ key: LocalizedStringKey, isMet: Bool) -> some View {
        HStack(spacing: 4) {
            Image(systemName: isMet ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 12))
            Text(key)
        }
        .font(.caption)
        .foregroundStyle(isMet ? .green : .secondary)
    }

    // MARK: - Computed

    private var passwordsMatch: Bool {
        !data.confirmPassword.isEmpty && data.password == data.confirmPassword
    }

    private var hasUppercase: Bool {
        data.password.contains { $0.isUppercase }
    }

    private var hasLowercase: Bool {
        data.password.contains { $0.isLowercase }
    }

    private var hasDigit: Bool {
        data.password.contains { $0.isNumber }
    }

    private var hasSpecial: Bool {
        data.password.contains { "!@#$%^&*()_+-=[]{}|;':\",./<>?`~".contains($0) }
    }
}

#Preview {
    PasswordStepView(data: RegistrationData())
        .padding()
}
