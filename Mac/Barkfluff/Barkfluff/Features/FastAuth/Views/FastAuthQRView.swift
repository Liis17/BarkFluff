//
//  FastAuthQRView.swift
//  Barkfluff
//
//  Встраиваемая панель быстрой авторизации через QR-код.
//

import SwiftUI
import BFCore

struct QRPanelView: View {
    @Bindable var viewModel: FastAuthViewModel

    var body: some View {
        VStack(spacing: Theme.Spacing.md) {
            Text("Войти через QR")
                .font(.headline)

            Text("Отсканируйте код в мобильном BarkFluff")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            qrContent
                .frame(width: 220, height: 220)
                .background(
                    RoundedRectangle(cornerRadius: 12)
                        .fill(Color.white)
                )

            statusLine

            if let message = viewModel.errorMessage {
                Text(message)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .multilineTextAlignment(.center)
            }
        }
        .padding(Theme.Spacing.xxl)
        .frame(width: 280)
        .background(
            RoundedRectangle(cornerRadius: 18)
                .fill(.regularMaterial)
                .shadow(color: .black.opacity(0.08), radius: 24, x: 0, y: 8)
                .shadow(color: .black.opacity(0.04), radius: 4, x: 0, y: 2)
        )
        .onAppear { viewModel.startSession() }
        .onDisappear { viewModel.cancel() }
    }

    @ViewBuilder
    private var qrContent: some View {
        if let image = viewModel.currentTokenImage {
            Image(nsImage: image)
                .interpolation(.none)
                .resizable()
                .scaledToFit()
                .padding(8)
        } else if viewModel.isLoading {
            ProgressView()
        } else {
            Image(systemName: "qrcode")
                .font(.system(size: 64))
                .foregroundStyle(.tertiary)
        }
    }

    @ViewBuilder
    private var statusLine: some View {
        switch viewModel.currentStatus {
        case .pending:
            HStack(spacing: 6) {
                ProgressView().scaleEffect(0.6)
                Text(viewModel.timeRemaining > 0
                     ? "Действителен ещё \(viewModel.timeRemaining) с"
                     : "Ожидание сканирования…")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        case .accepted:
            HStack(spacing: 6) {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                Text("Вход подтверждён").font(.caption.weight(.medium))
            }
        case .rejected:
            HStack(spacing: 6) {
                Image(systemName: "xmark.circle.fill").foregroundStyle(.red)
                Text("Отклонено, обновляем…").font(.caption)
            }
        case .expired:
            HStack(spacing: 6) {
                Image(systemName: "clock.fill").foregroundStyle(.orange)
                Text("Срок истёк, обновляем…").font(.caption)
            }
        }
    }
}
