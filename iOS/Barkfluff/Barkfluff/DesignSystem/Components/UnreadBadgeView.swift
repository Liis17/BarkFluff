//
//  UnreadBadgeView.swift
//  Barkfluff (iOS)
//
//  Бейдж непрочитанных сообщений
//

import SwiftUI

struct UnreadBadgeView: View {
    let count: Int64

    var body: some View {
        Text(displayText)
            .font(.caption2)
            .fontWeight(.semibold)
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Color.accentColor)
            .clipShape(Capsule())
    }

    private var displayText: String {
        if count > 99 {
            return "99+"
        }
        return "\(count)"
    }
}
