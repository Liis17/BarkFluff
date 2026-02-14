//
//  ErrorBannerView.swift
//  Barkfluff
//
//  Баннер ошибки
//

import SwiftUI

struct ErrorBannerView: View {
    let message: String
    let onRetry: (() -> Void)?

    init(message: String, onRetry: (() -> Void)? = nil) {
        self.message = message
        self.onRetry = onRetry
    }

    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.white)

            Text(message)
                .font(.subheadline)
                .foregroundStyle(.white)

            Spacer()

            if let onRetry = onRetry {
                Button("Повторить") {
                    onRetry()
                }
                .buttonStyle(.bordered)
                .tint(.white)
            }
        }
        .padding(Theme.Spacing.md)
        .background(Color.red)
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
    }
}

#Preview {
    VStack(spacing: 16) {
        ErrorBannerView(message: "Ошибка сети")
        ErrorBannerView(message: "Не удалось загрузить данные", onRetry: {
            print("Retry tapped")
        })
    }
    .padding()
}
