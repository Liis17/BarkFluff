//
//  ConfirmEmailStepView.swift
//  Barkfluff
//
//  Шаг 4: Подтверждение email кодом (iOS)
//

import SwiftUI
import BFCore

struct ConfirmEmailStepView: View {
    @Bindable var data: RegistrationData
    let authService: AuthServiceProtocol
    var onVerified: (() -> Void)?

    @State private var code: String = ""
    @State private var validationError: LocalizedStringResource?
    @State private var isVerifying = false
    @State private var canResend = false
    @State private var resendCooldown = 60
    @State private var isVerified = false

    var body: some View {
        VStack(spacing: 16) {
            // Информация
            VStack(spacing: 4) {
                Text("auth.register.step.confirm_email.sent_to")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                Text(verbatim: data.email)
                    .font(.headline)
                    .foregroundStyle(.blue)
            }

            // Поле ввода кода
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.confirm_email.code.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                TextField("auth.register.step.confirm_email.code.placeholder", text: $code)
                    .textFieldStyle(.roundedBorder)
                    .keyboardType(.numberPad)
                    .multilineTextAlignment(.center)
                    .font(.title3)
                    .disabled(isVerifying || isVerified)
                    .onChange(of: code) { _, newValue in
                        if newValue.count == 6 {
                            Task { await verifyCode(newValue) }
                        }
                    }
            }

            // Статус
            if isVerifying {
                HStack(spacing: 8) {
                    ProgressView()
                        .controlSize(.small)
                    Text("auth.register.step.confirm_email.verifying")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            } else if let error = validationError {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            } else if isVerified {
                Text("auth.register.step.confirm_email.verified")
                    .font(.subheadline)
                    .fontWeight(.medium)
                    .foregroundStyle(.green)
            }

            // Кнопка повторной отправки
            if canResend {
                Button("auth.register.step.confirm_email.resend") {
                    Task { await resendCode() }
                }
                .buttonStyle(.bordered)
                .controlSize(.small)
            } else {
                Text("auth.register.step.confirm_email.resend_cooldown \(resendCooldown)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .onAppear {
            startResendTimer()
        }
    }

    // MARK: - Actions

    private func verifyCode(_ code: String) async {
        guard let codeID = data.codeID else {
            validationError = LocalizedStringResource("auth.register.step.confirm_email.missing_code_id")
            return
        }

        validationError = nil
        isVerifying = true

        do {
            try await authService.confirmAccount(codeID: codeID, code: code)
            isVerified = true
            onVerified?()
        } catch {
            validationError = LocalizedStringResource("auth.register.step.confirm_email.invalid")
            isVerified = false
            self.code = ""
        }

        isVerifying = false
    }

    private func resendCode() async {
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
    ConfirmEmailStepView(
        data: RegistrationData(),
        authService: DependencyContainer().authService
    )
    .padding()
}
