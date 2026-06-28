//
//  LoadingView.swift
//  Barkfluff
//
//  View для отображения загрузки
//

import SwiftUI

struct LoadingView: View {
    var body: some View {
        VStack(spacing: Theme.Spacing.md) {
            ProgressView()
                .scaleEffect(1.5)
            Text("common.loading")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

#Preview {
    LoadingView()
}
