//
//  PasswordStrengthView.swift
//  Barkfluff
//
//  Индикатор силы пароля
//

import SwiftUI

/// Индикатор силы пароля
struct PasswordStrengthView: View {
    let strength: PasswordStrength
    let score: Int

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            // Название и оценка
            HStack {
                Text("Надёжность пароля:")
                    .font(.caption)
                    .foregroundStyle(.secondary)

                Spacer()

                Text(strength.label)
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundStyle(colorForStrength(strength))
            }

            // Сегменты индикатора (без GeometryReader)
            HStack(spacing: 4) {
                ForEach(1...5, id: \.self) { segment in
                    RoundedRectangle(cornerRadius: 2)
                        .fill(segment <= strength.rawValue ? colorForStrength(strength) : Color.gray.opacity(0.2))
                        .frame(maxWidth: .infinity)
                        .frame(height: 4)
                }
            }
        }
    }

    private func colorForStrength(_ strength: PasswordStrength) -> Color {
        switch strength {
        case .veryWeak, .weak:
            return .red
        case .medium:
            return .yellow
        case .strong, .veryStrong:
            return .green
        }
    }
}

/// Требования к паролю с чекмарками
struct PasswordRequirementsView: View {
    let password: String

    private var hasMinLength: Bool {
        password.count >= 8
    }

    private var hasUppercase: Bool {
        password.contains { $0.isUppercase }
    }

    private var hasLowercase: Bool {
        password.contains { $0.isLowercase }
    }

    private var hasDigit: Bool {
        password.contains { $0.isNumber }
    }

    private var hasSpecial: Bool {
        password.contains { "!@#$%^&*()_+-=[]{}|;':\",./<>?`~".contains($0) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            requirementRow("Минимум 8 символов", isMet: hasMinLength)
            requirementRow("Заглавная буква", isMet: hasUppercase)
            requirementRow("Строчная буква", isMet: hasLowercase)
            requirementRow("Цифра", isMet: hasDigit)
            requirementRow("Специальный символ", isMet: hasSpecial)
        }
    }

    @ViewBuilder
    private func requirementRow(_ text: String, isMet: Bool) -> some View {
        HStack(spacing: Theme.Spacing.xs) {
            Image(systemName: isMet ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 14))
                .foregroundStyle(isMet ? .green : .gray)

            Text(text)
                .font(.caption)
                .foregroundStyle(isMet ? .primary : .secondary)
        }
    }
}

/// Комбинированный view для пароля с индикатором силы
struct PasswordWithStrengthView: View {
    @Binding var password: String
    @Binding var confirmPassword: String
    var showRequirements: Bool = true

    @State private var showPassword = false
    @FocusState private var focusedField: Field?

    enum Field: Hashable {
        case password
        case confirmPassword
    }

    private var validationResult: PasswordValidationResult {
        PasswordValidator.validate(password)
    }

    private var confirmationResult: Bool {
        !confirmPassword.isEmpty && password == confirmPassword
    }

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.md) {
            // Поле пароля
            HStack {
                if showPassword {
                    TextField("Пароль", text: $password)
                        .focused($focusedField, equals: .password)
                } else {
                    SecureField("Пароль", text: $password)
                        .focused($focusedField, equals: .password)
                }

                Button(action: { showPassword.toggle() }) {
                    Image(systemName: showPassword ? "eye.slash" : "eye")
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
            }
            .textFieldStyle(.roundedBorder)

            // Индикатор силы (показываем только если есть пароль)
            if !password.isEmpty {
                PasswordStrengthView(
                    strength: validationResult.strength,
                    score: validationResult.score
                )
            }

            // Требования (опционально)
            if showRequirements && !password.isEmpty {
                PasswordRequirementsView(password: password)
            }

            // Подтверждение пароля
            SecureField("Подтвердите пароль", text: $confirmPassword)
                .focused($focusedField, equals: .confirmPassword)
                .textFieldStyle(.roundedBorder)

            // Индикатор совпадения
            if !confirmPassword.isEmpty {
                HStack(spacing: Theme.Spacing.xs) {
                    Image(systemName: confirmationResult ? "checkmark.circle.fill" : "xmark.circle.fill")
                        .foregroundStyle(confirmationResult ? .green : .red)
                    Text(confirmationResult ? "Пароли совпадают" : "Пароли не совпадают")
                        .font(.caption)
                        .foregroundStyle(confirmationResult ? .green : .red)
                }
            }
        }
    }
}

#Preview("Strength Indicator") {
    VStack(spacing: 20) {
        PasswordStrengthView(strength: .veryWeak, score: 10)
        PasswordStrengthView(strength: .weak, score: 30)
        PasswordStrengthView(strength: .medium, score: 50)
        PasswordStrengthView(strength: .strong, score: 70)
        PasswordStrengthView(strength: .veryStrong, score: 95)
    }
    .padding()
}

#Preview("Requirements") {
    VStack {
        PasswordRequirementsView(password: "Test1!")
        PasswordRequirementsView(password: "Test1234!")
    }
    .padding()
}

#Preview("Full View") {
    PasswordWithStrengthView(
        password: .constant("Test1234!"),
        confirmPassword: .constant("Test1234!"),
        showRequirements: true
    )
    .padding()
    .frame(width: 300)
}
