//
//  StickersGridView.swift
//  Barkfluff
//
//  Сетка стикеров активного таба пикера.
//

import SwiftUI
import BFCore

struct StickersGridView: View {
    let stickers: [Sticker]
    let onTap: (Sticker) -> Void
    let onLongPressStart: (Sticker) -> Void
    let onLongPressEnd: () -> Void

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 4), count: 4)

    var body: some View {
        ScrollView {
            if stickers.isEmpty {
                emptyState
                    .frame(maxWidth: .infinity, minHeight: 200)
            } else {
                LazyVGrid(columns: columns, spacing: 4) {
                    ForEach(stickers) { sticker in
                        StickerThumbView(
                            sticker: sticker,
                            onTap: { onTap(sticker) },
                            onLongPressStart: { onLongPressStart(sticker) },
                            onLongPressEnd: onLongPressEnd
                        )
                    }
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
            }
        }
    }

    private var emptyState: some View {
        VStack(spacing: 8) {
            Image(systemName: "face.dashed")
                .font(.system(size: 28))
                .foregroundStyle(.secondary)
            Text("conversation.stickers.empty")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
        .padding()
    }
}
