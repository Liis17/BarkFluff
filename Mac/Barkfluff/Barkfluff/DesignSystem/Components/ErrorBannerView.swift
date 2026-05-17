//
//  ErrorBannerView.swift
//  Barkfluff
//
//  Баннер ошибки
//

import SwiftUI

struct ErrorBannerView: View {
    private let message: LocalizedStringResource
    private let onRetry: (() -> Void)?

    init(message: LocalizedStringResource, onRetry: (() -> Void)? = nil) {
        self.message = message
        self.onRetry = onRetry
    }

    /// Обёртка для случая, когда сообщение приходит в виде обычной строки
    /// (например, `error.localizedDescription` из системных ошибок).
    init(message: String, onRetry: (() -> Void)? = nil) {
        self.message = LocalizedStringResource(stringLiteral: message)
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
                Button("common.retry") {
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
        ErrorBannerView(message: LocalizedStringResource(stringLiteral: "Ошибка сети"))
        ErrorBannerView(message: LocalizedStringResource(stringLiteral: "Не удалось загрузить данные"), onRetry: {
            print("Retry tapped")
        })
    }
    .padding()
}
