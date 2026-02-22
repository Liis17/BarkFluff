//
//  ServerCardView.swift
//  Barkfluff
//
//  Карточка сервера из списка Navigator
//

import SwiftUI
import BFCore

/// Карточка сервера из списка Navigator
struct ServerCardView: View {
    let server: NavigatorServer
    let isConnecting: Bool
    let isDisabled: Bool
    let onTap: () -> Void

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 12) {
                // Иконка сервера (первая буква имени)
                Text(serverInitial)
                    .font(.title3)
                    .fontWeight(.bold)
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 40)
                    .background(
                        Circle()
                            .fill(serverColor)
                    )

                VStack(alignment: .leading, spacing: 2) {
                    // Название сервера
                    Text(server.name)
                        .font(.headline)
                        .foregroundStyle(.primary)

                    // Описание
                    if let description = server.description, !description.isEmpty {
                        Text(description)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }

                    // Локация и юзернейм сервера
                    HStack(spacing: 8) {
                        Label(server.host, systemImage: "location")
                            .font(.caption)

                        Text("•")
                            .font(.caption2)

                        Text("@\(server.serverPublicName)")
                            .font(.caption)
                            .fontWeight(.medium)
                    }
                    .foregroundStyle(.tertiary)
                }

                Spacer()

                if isConnecting {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Image(systemName: "chevron.right")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background {
            RoundedRectangle(cornerRadius: 10)
                .fill(Color(.controlBackgroundColor))
        }
        .overlay {
            RoundedRectangle(cornerRadius: 10)
                .stroke(Color(.separatorColor), lineWidth: 0.5)
        }
        .opacity(isDisabled ? 0.5 : 1.0)
        .disabled(isDisabled)
    }

    // MARK: - Helpers

    private var serverInitial: String {
        String(server.name.prefix(1)).uppercased()
    }

    /// Детерминированный цвет на основе имени сервера
    private var serverColor: Color {
        let colors: [Color] = [.blue, .purple, .green, .orange, .pink, .cyan, .indigo, .mint]
        let hash = abs(server.name.hashValue)
        return colors[hash % colors.count]
    }

    private var formattedAccountsCount: String {
        let count = server.accountsCount
        if count >= 1_000_000 {
            return String(format: "%.1fM", Double(count) / 1_000_000)
        } else if count >= 1_000 {
            return String(format: "%.1fK", Double(count) / 1_000)
        }
        return "\(count)"
    }
}

#Preview {
    VStack(spacing: 12) {
        ServerCardView(
            server: NavigatorServer(
                id: "1",
                name: "Main Server",
                host: "main.barkfluff.com",
                port: 7004,
                description: "Основной сервер BarkFluff",
                accountsCount: 12500,
                serverPublicName: ""
            ),
            isConnecting: false,
            isDisabled: false,
            onTap: {}
        )

        ServerCardView(
            server: NavigatorServer(
                id: "2",
                name: "dev",
                host: "dev.barkfluff.com",
                port: 7004,
                description: "Сервер для разработки",
                accountsCount: 12,
                serverPublicName: "Dev Server"
            ),
            isConnecting: true,
            isDisabled: false,
            onTap: {}
        )
    }
    .padding()
    .frame(width: 400)
}
