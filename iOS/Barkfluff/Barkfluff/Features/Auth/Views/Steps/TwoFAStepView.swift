//
//  TwoFAStepView.swift
//  Barkfluff
//
//  Шаг 8: Настройка 2FA (iOS)
//

import SwiftUI
import BFCore

struct TwoFAStepView: View {
    @Bindable var data: RegistrationData
    let authService: AuthServiceProtocol

    @State private var otpCode: String = ""
    @State private var isEnabling = false
    @State private var isConfirming = false
    @State private var error: LocalizedStringResource?
    @State private var qrImage: UIImage?

    var body: some View {
        VStack(spacing: 16) {
            if let setupInfo = data.otpSetupInfo {
                setupView(setupInfo)
            } else if data.is2FAEnabled {
                successView
            } else {
                offerView
            }

            if let error = error {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            }
        }
    }

    // MARK: - Offer View

    @ViewBuilder
    private var offerView: some View {
        VStack(spacing: 12) {
            Text("auth.register.step.two_fa.offer.description")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            Button {
                Task { await enable2FA() }
            } label: {
                if isEnabling {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Text("auth.register.step.two_fa.enable")
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(isEnabling)

            Text("auth.register.step.two_fa.enable_later")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Setup View

    @ViewBuilder
    private func setupView(_ info: OTPSetupInfo) -> some View {
        VStack(spacing: 12) {
            // QR код
            if let qrImage = qrImage {
                Image(uiImage: qrImage)
                    .interpolation(.none)
                    .resizable()
                    .frame(width: 140, height: 140)
                    .clipShape(RoundedRectangle(cornerRadius: 8))
            } else {
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color(uiColor: .tertiarySystemFill))
                    .frame(width: 140, height: 140)
                    .overlay(ProgressView())
            }

            Text("auth.register.step.two_fa.scan_hint")
                .font(.caption)
                .foregroundStyle(.secondary)

            // Код для ручного ввода
            VStack(spacing: 4) {
                Text("auth.register.step.two_fa.manual_hint")
                    .font(.caption)
                    .foregroundStyle(.secondary)

                Text(verbatim: info.secretCode)
                    .font(.system(.subheadline, design: .monospaced))
                    .fontWeight(.medium)
                    .textSelection(.enabled)
                    .padding(8)
                    .background(Color(uiColor: .secondarySystemGroupedBackground))
                    .clipShape(RoundedRectangle(cornerRadius: 6))
            }

            // Поле для кода подтверждения
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.two_fa.app_code")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack(spacing: 8) {
                    TextField("auth.register.step.two_fa.app_code.placeholder", text: $otpCode)
                        .textFieldStyle(.roundedBorder)
                        .keyboardType(.numberPad)
                        .multilineTextAlignment(.center)
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
        }
        .onAppear {
            generateQRImage(from: info.qrCodeBase64)
        }
    }

    // MARK: - Success View

    @ViewBuilder
    private var successView: some View {
        VStack(spacing: 12) {
            Image(systemName: "checkmark.seal.fill")
                .font(.system(size: 48))
                .foregroundStyle(.green)

            Text("auth.register.step.two_fa.success.title")
                .font(.headline)

            Text("auth.register.step.two_fa.success.description")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
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
            self.error = LocalizedStringResource("auth.register.step.two_fa.error.enable")
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
            self.error = LocalizedStringResource("auth.register.step.two_fa.error.invalid")
            otpCode = ""
        }
    }

    private func generateQRImage(from base64: String) {
        guard let data = Data(base64Encoded: base64) else { return }
        qrImage = UIImage(data: data)
    }
}

#Preview {
    TwoFAStepView(data: RegistrationData(), authService: DependencyContainer().authService)
        .padding()
}
