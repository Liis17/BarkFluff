//
//  ErrorBannerView.swift
//  Barkfluff
//
//  Баннер ошибки (iOS версия)
//

import SwiftUI

struct ErrorBannerView: View {
    let message: String
    var onDismiss: (() -> Void)?

    var body: some View {
        HStack(spacing: Theme.Spacing.sm) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.red)

            Text(message)
                .font(.subheadline)
                .foregroundStyle(.primary)

            Spacer()

            if let onDismiss {
                Button(action: onDismiss) {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.secondary)
                }
            }
        }
        .padding(Theme.Spacing.md)
        .background(.red.opacity(0.1))
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
    }
}

#Preview {
    VStack {
        ErrorBannerView(message: "Произошла ошибка")
        ErrorBannerView(message: "Ошибка с кнопкой закрытия", onDismiss: {})
    }
    .padding()
}
