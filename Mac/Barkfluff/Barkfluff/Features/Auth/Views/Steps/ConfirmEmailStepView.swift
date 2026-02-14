//
//  ConfirmEmailStepView.swift
//  Barkfluff
//
//  Шаг 4: Подтверждение email кодом
//

import SwiftUI
import BFCore

/// Шаг регистрации: подтверждение email кодом
struct ConfirmEmailStepView: View {
    @Bindable var data: RegistrationData
    let authService: AuthServiceProtocol
    var onVerified: (() -> Void)?

    @State private var code: String = ""
    @State private var validationError: String?
    @State private var isVerifying = false
    @State private var canResend = false
    @State private var resendCooldown = 60
    @State private var isVerified = false

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "mail.stack.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Текст
            VStack(spacing: Theme.Spacing.sm) {
                Text("Код отправлен на")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                Text(data.email)
                    .font(.headline)
            }

            // Поле ввода кода
            VStack(spacing: Theme.Spacing.md) {
                OTPInputView(
                    code: $code,
                    isError: validationError != nil,
                    onComplete: { code in
                        Task { await verifyCode(code) }
                    }
                )

                if let error = validationError {
                    Text(error)
                        .font(.caption)
                        .foregroundStyle(.red)
                }

                if isVerified {
                    Label("Email подтверждён", systemImage: "checkmark.circle.fill")
                        .font(.subheadline)
                        .foregroundStyle(.green)
                }
            }

            // Кнопка повторной отправки
            if canResend {
                Button("Отправить код повторно") {
                    Task { await resendCode() }
                }
                .buttonStyle(.plain)
                .foregroundStyle(Color.accentColor)
            } else {
                Text("Повторная отправка через \(resendCooldown) сек")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .onAppear {
            startResendTimer()
        }
    }

    private func handleCodeChange(_ newValue: String) {
        validationError = nil

        // Автоматически верифицируем при вводе 6 цифр
        if newValue.count == 6 {
            let result = OTPCodeValidator.validate(newValue)
            if result.isValid {
                Task { await verifyCode(newValue) }
            } else {
                validationError = result.error
            }
        }
    }

    private func verifyCode(_ code: String) async {
        guard let codeID = data.codeID else {
            validationError = "Ошибка: отсутствует codeID"
            return
        }

        isVerifying = true
        defer { isVerifying = false }

        do {
            try await authService.confirmAccount(codeID: codeID, code: code)
            isVerified = true
            // Уведомляем родителя об успешной верификации
            onVerified?()
        } catch {
            validationError = "Неверный код. Попробуйте снова."
        }
    }

    private func resendCode() async {
        // TODO: Реализовать повторную отправку кода
        canResend = false
        resendCooldown = 60
        startResendTimer()
    }

    private func startResendTimer() {
        Task {
            while resendCooldown > 0 {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                resendCooldown -= 1
            }
            canResend = true
        }
    }
}

#Preview {
    ConfirmEmailStepView(data: RegistrationData(), authService: DependencyContainer().authService)
        .padding()
}
