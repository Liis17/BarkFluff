//
//  OTPVerificationView.swift
//  Barkfluff
//
//  Экран ввода кода подтверждения
//

import SwiftUI

struct OTPVerificationView: View {
    @State private var code = ""
    @State private var isLoading = false

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            Image(systemName: "number.circle")
                .font(.system(size: 64))
                .foregroundStyle(Color.accentColor)

            Text("auth.otp.title")
                .font(.title)

            Text("auth.otp.subtitle")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            TextField("auth.otp.placeholder", text: $code)
                .textFieldStyle(.roundedBorder)
                .frame(width: 200)
                .multilineTextAlignment(.center)

            Button("auth.otp.submit") {
                // TODO: Implement verification
                isLoading = true
            }
            .buttonStyle(.borderedProminent)
            .disabled(code.isEmpty || isLoading)

            if isLoading {
                ProgressView()
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

#Preview {
    OTPVerificationView()
}
