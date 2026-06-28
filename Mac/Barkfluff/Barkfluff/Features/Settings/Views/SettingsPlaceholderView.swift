//
//  SettingsPlaceholderView.swift
//  Barkfluff
//
//  Placeholder для разделов настроек в разработке
//

import SwiftUI

struct SettingsPlaceholderView: View {
    let category: SettingsCategory

    var body: some View {
        ContentUnavailableView {
            Label(category.title, systemImage: category.icon)
                .foregroundStyle(category.iconColor)
        } description: {
            Text("settings.placeholder.in_development")
        } actions: {
            Text("settings.placeholder.coming_soon")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .navigationTitle(category.title)
    }
}

#Preview {
    NavigationStack {
        SettingsPlaceholderView(category: .general)
    }
}
