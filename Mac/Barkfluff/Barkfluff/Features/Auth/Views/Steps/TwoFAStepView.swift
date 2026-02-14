//
//  TwoFAStepView.swift
//  Barkfluff
//
//  Шаг 8: Настройка двухфакторной аутентификации (опционально)
//

import SwiftUI
import BFCore

/// Шаг регистрации: настройка 2FA
struct TwoFAStepView: View {
    @Bindable var data: RegistrationData
    let authService: AuthServiceProtocol

    @State private var otpCode: String = ""
    @State private var isEnabling = false
    @State private var isConfirming = false
    @State private var error: String?
    @State private var qrImage: NSImage?

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "shield.checkered.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            if let setupInfo = data.otpSetupInfo {
                // 2FA уже включена — показываем QR
                setupView(setupInfo)
            } else if data.is2FAEnabled {
                // 2FA уже подтверждена
                successView
            } else {
                // Предложение включить 2FA
                offerView
            }

            // Ошибка
            if let error = error {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            }
        }
    }

    // MARK: - Subviews

    @ViewBuilder
    private var offerView: some View {
        VStack(spacing: Theme.Spacing.md) {
            Text("Двухфакторная аутентификация добавляет дополнительный уровень защиты вашему аккаунту.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(width: 280)

            Button(action: { Task { await enable2FA() } }) {
                if isEnabling {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Text("Включить 2FA")
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(isEnabling)

            Text("Вы можете пропустить этот шаг и включить 2FA позже в настройках")
                .font(.caption)
                .foregroundStyle(.tertiary)
        }
    }

    @ViewBuilder
    private func setupView(_ info: OTPSetupInfo) -> some View {
        VStack(spacing: Theme.Spacing.md) {
            // QR код
            if let qrImage = qrImage {
                Image(nsImage: qrImage)
                    .interpolation(.none)
                    .resizable()
                    .frame(width: 180, height: 180)
            } else {
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .fill(Color.gray.opacity(0.2))
                    .frame(width: 180, height: 180)
                    .overlay(ProgressView())
            }

            Text("Отсканируйте QR-код в приложении Google Authenticator или аналогичном")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            // Код для ручного ввода
            VStack(spacing: Theme.Spacing.xs) {
                Text("Или введите код вручную:")
                    .font(.caption)
                    .foregroundStyle(.secondary)

                Text(info.secretCode)
                    .font(.system(.body, design: .monospaced))
                    .textSelection(.enabled)
                    .padding(Theme.Spacing.sm)
                    .background(Color(.textBackgroundColor))
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.sm))
            }

            // Поле для кода подтверждения
            VStack(spacing: Theme.Spacing.sm) {
                Text("Введите код из приложения:")
                    .font(.subheadline)

                OTPInputView(code: $otpCode, isError: error != nil)
                    .onChange(of: otpCode) { _, newValue in
                        if newValue.count == 6 {
                            Task { await confirm2FA(newValue) }
                        }
                    }

                if isConfirming {
                    ProgressView()
                        .controlSize(.small)
                }
            }
        }
        .onAppear {
            generateQRImage(from: info.qrCodeBase64)
        }
    }

    @ViewBuilder
    private var successView: some View {
        VStack(spacing: Theme.Spacing.md) {
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(.green)

            Text("Двухфакторная аутентификация включена!")
                .font(.headline)

            Text("Теперь при входе в аккаунт вам понадобится код из приложения-аутентификатора")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(width: 280)
        }
    }

    // MARK: - Actions

    private func enable2FA() async {
        isEnabling = true
        error = nil
        defer { isEnabling = false }

        do {
            let info = try await authService.enableOTP()
            data.otpSetupInfo = info
        } catch {
            self.error = "Не удалось включить 2FA: \(error.localizedDescription)"
        }
    }

    private func confirm2FA(_ code: String) async {
        isConfirming = true
        error = nil
        defer { isConfirming = false }

        do {
            try await authService.confirmOTP(code: code)
            data.is2FAEnabled = true
        } catch {
            self.error = "Неверный код. Попробуйте снова."
            otpCode = ""
        }
    }

    private func generateQRImage(from base64: String) {
        guard let data = Data(base64Encoded: base64) else { return }
        qrImage = NSImage(data: data)
    }
}

#Preview {
    TwoFAStepView(data: RegistrationData(), authService: DependencyContainer().authService)
        .padding()
}
