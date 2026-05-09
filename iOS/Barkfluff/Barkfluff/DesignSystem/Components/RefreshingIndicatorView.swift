//
//  RefreshingIndicatorView.swift
//  Barkfluff (iOS)
//
//  Тонкая «полоска» в заголовке списка/чата на время stale-while-revalidate.
//

import SwiftUI

struct RefreshingIndicatorView: View {
    var label: String = "Обновление…"

    var body: some View {
        HStack(spacing: 6) {
            ProgressView()
                .controlSize(.small)
                .scaleEffect(0.7)
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 4)
        .background(.ultraThinMaterial)
    }
}
