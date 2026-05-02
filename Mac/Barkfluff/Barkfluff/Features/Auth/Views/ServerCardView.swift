//
//  ServerCardView.swift
//  Barkfluff
//
//  Карточка сервера из списка Navigator
//

import SwiftUI
import BFCore

struct ServerCardView: View {
    let server: NavigatorServer
    let isConnecting: Bool
    let isDisabled: Bool
    let onTap: () -> Void

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 14) {
                // Иконка сервера
                serverIcon

                // Информация
                VStack(alignment: .leading, spacing: 3) {
                    Text(server.name)
                        .font(.headline)
                        .foregroundStyle(.primary)

                    if let description = server.description, !description.isEmpty {
                        Text(description)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }

                    HStack(spacing: 6) {
                        Image(systemName: "globe")
                            .font(.caption2)
                        Text(server.host)
                            .font(.caption)

                        if !server.serverPublicName.isEmpty {
                            Text("·")
                                .font(.caption2)
                            Text("@\(server.serverPublicName)")
                                .font(.caption)
                                .fontWeight(.medium)
                        }
                    }
                    .foregroundStyle(.tertiary)
                }

                Spacer()

                // Правая часть: статус или шеврон
                if isConnecting {
                    ProgressView()
                        .controlSize(.small)
                        .tint(serverColor)
                } else {
                    Image(systemName: "chevron.right")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 14)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(cardBackground)
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .opacity(isDisabled ? 0.45 : 1.0)
        .disabled(isDisabled || isConnecting)
        .scaleEffect(isConnecting ? 0.99 : 1.0)
        .animation(.easeInOut(duration: 0.15), value: isConnecting)
    }

    // MARK: - Subviews

    private var serverIcon: some View {
        ZStack {
            Circle()
                .fill(serverColor.opacity(0.15))
                .frame(width: 44, height: 44)
            Text(serverInitial)
                .font(.title3.bold())
                .foregroundStyle(serverColor)
        }
    }

    private var cardBackground: some View {
        RoundedRectangle(cornerRadius: 14)
            .fill(.regularMaterial)
            .shadow(color: .black.opacity(0.06), radius: 10, x: 0, y: 4)
            .shadow(color: .black.opacity(0.03), radius: 2, x: 0, y: 1)
            .overlay(
                RoundedRectangle(cornerRadius: 14)
                    .strokeBorder(
                        isConnecting
                            ? serverColor.opacity(0.4)
                            : Color.primary.opacity(0.06),
                        lineWidth: 1
                    )
            )
    }

    // MARK: - Helpers

    private var serverInitial: String {
        String(server.name.prefix(1)).uppercased()
    }

    private var serverColor: Color {
        let palette: [Color] = [.blue, .purple, .green, .orange, .pink, .cyan, .indigo, .mint]
        return palette[abs(server.name.hashValue) % palette.count]
    }
}

#Preview {
    VStack(spacing: 10) {
        ServerCardView(
            server: NavigatorServer(
                id: "1",
                name: "BarkFluff Main",
                host: "main.barkfluff.com",
                port: 7004,
                description: "Основной сервер",
                accountsCount: 12500,
                serverPublicName: "barkfluff"
            ),
            isConnecting: false,
            isDisabled: false,
            onTap: {}
        )

        ServerCardView(
            server: NavigatorServer(
                id: "2",
                name: "Dev Server",
                host: "dev.barkfluff.com",
                port: 7004,
                description: "Сервер для разработки",
                accountsCount: 12,
                serverPublicName: "dev"
            ),
            isConnecting: true,
            isDisabled: false,
            onTap: {}
        )

        ServerCardView(
            server: NavigatorServer(
                id: "3",
                name: "Community",
                host: "community.barkfluff.com",
                port: 7004,
                description: nil,
                accountsCount: 3400,
                serverPublicName: "comm"
            ),
            isConnecting: false,
            isDisabled: true,
            onTap: {}
        )
    }
    .padding(20)
    .frame(width: 460)
}
