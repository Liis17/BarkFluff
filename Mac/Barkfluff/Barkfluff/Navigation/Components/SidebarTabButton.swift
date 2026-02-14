//
//  SidebarTabButton.swift
//  Barkfluff
//
//  Кнопка вкладки в сайдбаре с Liquid Glass эффектом
//

import SwiftUI

/// Кнопка вкладки сайдбара
struct SidebarTabButton<Label: View>: View {
    let isActive: Bool
    let action: () -> Void
    @ViewBuilder let label: () -> Label

    var body: some View {
        Button(action: action) {
            label()
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
                .foregroundStyle(isActive ? Color.accentColor : .secondary)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background {
            if isActive {
                RoundedRectangle(cornerRadius: 10)
                    .fill(Color.accentColor.opacity(0.15))
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .contentShape(RoundedRectangle(cornerRadius: 10))
    }
}
