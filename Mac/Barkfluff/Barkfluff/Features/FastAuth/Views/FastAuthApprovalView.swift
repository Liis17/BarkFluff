//
//  FastAuthApprovalView.swift
//  Barkfluff
//
//  Подтверждение быстрой авторизации (на устройстве, которое уже авторизовано)
//

import SwiftUI
import BFCore

struct FastAuthApprovalView: View {
    let request: FastAuthRequest
    @Environment(\.dismiss) private var dismiss

    var onApprove: (() -> Void)?
    var onReject: (() -> Void)?

    var body: some View {
        VStack(spacing: 24) {
            // Иконка устройства
            Image(systemName: deviceIcon)
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            VStack(spacing: 8) {
                Text("fast_auth.approval.title")
                    .font(.title2)
                    .fontWeight(.semibold)

                Text("fast_auth.approval.warning")
                    .foregroundStyle(.secondary)
            }

            // Информация о запросе
            VStack(spacing: 12) {
                HStack {
                    Image(systemName: "desktopcomputer")
                        .frame(width: 24)
                    Text("fast_auth.approval.device_label")
                        .foregroundStyle(.secondary)
                    Spacer()
                    Text(request.deviceName)
                        .fontWeight(.medium)
                }

                HStack {
                    Image(systemName: "network")
                        .frame(width: 24)
                    Text("fast_auth.approval.type_label")
                        .foregroundStyle(.secondary)
                    Spacer()
                    Text(request.deviceType)
                        .fontWeight(.medium)
                }

                HStack {
                    Image(systemName: "clock")
                        .frame(width: 24)
                    Text("fast_auth.approval.time_label")
                        .foregroundStyle(.secondary)
                    Spacer()
                    Text(request.requestedAt, style: .time)
                        .fontWeight(.medium)
                }
            }
            .padding()
            .background(Color.secondary.opacity(0.1))
            .clipShape(RoundedRectangle(cornerRadius: 12))

            // Кнопки
            VStack(spacing: 12) {
                Button {
                    onApprove?()
                    dismiss()
                } label: {
                    Text("fast_auth.approval.accept")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)

                Button("fast_auth.approval.reject", role: .destructive) {
                    onReject?()
                    dismiss()
                }
                .buttonStyle(.bordered)
            }
        }
        .padding(32)
        .frame(width: 400)
    }

    private var deviceIcon: String {
        let type = request.deviceType.lowercased()
        if type.contains("iphone") { return "iphone" }
        if type.contains("ipad") { return "ipad" }
        if type.contains("mac") { return "desktopcomputer" }
        return "questionmark.circle"
    }
}

#Preview {
    FastAuthApprovalView(
        request: FastAuthRequest(
            id: "test",
            deviceName: "MacBook Pro",
            deviceType: "macOS",
            requestedAt: Date()
        )
    )
}
