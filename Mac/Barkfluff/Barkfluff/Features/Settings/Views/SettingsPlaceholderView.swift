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
            Text("Этот раздел находится в разработке")
        } actions: {
            Text("Скоро здесь появятся новые возможности")
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
