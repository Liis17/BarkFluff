//
//  OnlineStatusText.swift
//  Barkfluff
//
//  Текстовый компонент для отображения статуса "В сети" / "Был(а) N мин. назад"
//

import SwiftUI
import BFCore

/// Текстовый компонент для отображения статуса "В сети" / "Был(а) N мин. назад"
struct OnlineStatusText: View {
    let status: OnlineStatus

    var body: some View {
        switch status {
        case .online:
            Text("В сети")
                .font(.caption)
                .foregroundStyle(.green)
        case .offline(let lastSeen):
            Text(OnlineStatus.offline(lastSeen: lastSeen).displayText)
                .font(.caption)
                .foregroundStyle(.secondary)
        case .unknown:
            EmptyView()
        }
    }
}

#Preview {
    VStack(alignment: .leading, spacing: 8) {
        OnlineStatusText(status: .online)
        OnlineStatusText(status: .offline(lastSeen: Date().addingTimeInterval(-60)))
        OnlineStatusText(status: .offline(lastSeen: Date().addingTimeInterval(-3600)))
        OnlineStatusText(status: .offline(lastSeen: Date().addingTimeInterval(-86400)))
        OnlineStatusText(status: .unknown)
    }
    .padding()
}
