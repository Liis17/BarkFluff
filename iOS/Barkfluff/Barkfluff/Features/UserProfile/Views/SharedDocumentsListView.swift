//
//  SharedDocumentsListView.swift
//  Barkfluff (iOS)
//
//  Список общих документов чата.
//

import SwiftUI
import BFCore

struct SharedDocumentsListView: View {
    let items: [SharedMediaItem]

    var body: some View {
        VStack(spacing: 0) {
            ForEach(items) { item in
                HStack(spacing: 12) {
                    Image(systemName: "doc.fill")
                        .font(.title2)
                        .foregroundStyle(.blue)
                        .frame(width: 36, height: 36)
                        .background(Color.blue.opacity(0.1))
                        .clipShape(RoundedRectangle(cornerRadius: 8))

                    VStack(alignment: .leading, spacing: 2) {
                        Text(item.fileName.isEmpty ? String(localized: "user_profile.shared_docs.untitled") : item.fileName)
                            .font(.body)
                            .lineLimit(1)
                        Text(item.formattedSize)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 10)
                if item.id != items.last?.id {
                    Divider()
                        .padding(.leading, 60)
                }
            }
        }
        .background(Color(uiColor: .secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

}
