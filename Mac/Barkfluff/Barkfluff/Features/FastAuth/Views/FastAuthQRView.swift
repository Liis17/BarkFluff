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
            Text("fast_auth.qr.title")
                .font(.title3)
                .fontWeight(.semibold)

            Text("fast_auth.qr.instruction")
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)

            qrContent
                .frame(width: 220, height: 220)
                .background(
                    Color.white,
                    in: RoundedRectangle(cornerRadius: 14, style: .continuous)
                )
                .overlay {
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .strokeBorder(Color.black.opacity(0.08), lineWidth: 1)
                }
                .accessibilityElement(children: .combine)
                .accessibilityLabel(Text("fast_auth.qr.title"))

            statusLine
                .frame(minHeight: 22)

            if let message = viewModel.errorMessage {
                Label {
                    Text(message)
                        .font(.caption)
                        .multilineTextAlignment(.center)
                } icon: {
                    Image(systemName: "exclamationmark.circle.fill")
                }
                .foregroundStyle(.red)
                .frame(maxWidth: .infinity)
            }
        }
        .frame(maxWidth: .infinity, alignment: .top)
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
                .padding(10)
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
            HStack(spacing: Theme.Spacing.xs) {
                ProgressView()
                    .controlSize(.small)

                if viewModel.timeRemaining > 0 {
                    Text("fast_auth.qr.status.valid_for \(viewModel.timeRemaining)")
                } else {
                    Text("fast_auth.qr.status.waiting")
                }
            }
            .font(.caption)
            .foregroundStyle(.secondary)

        case .accepted:
            Label("fast_auth.qr.status.accepted", systemImage: "checkmark.circle.fill")
                .font(.caption.weight(.medium))
                .foregroundStyle(.green)

        case .rejected:
            Label("fast_auth.qr.status.rejected", systemImage: "xmark.circle.fill")
                .font(.caption)
                .foregroundStyle(.red)

        case .expired:
            Label("fast_auth.qr.status.expired", systemImage: "clock.fill")
                .font(.caption)
                .foregroundStyle(.orange)
        }
    }
}
