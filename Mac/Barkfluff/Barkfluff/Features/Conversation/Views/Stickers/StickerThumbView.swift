//
//  StickerThumbView.swift
//  Barkfluff
//
//  Превью стикера в сетке пикера: hover-эффект + tap + long-press для overlay.
//

import SwiftUI
import BFCore

struct StickerThumbView: View {
    let sticker: Sticker
    let onTap: () -> Void
    let onLongPressStart: () -> Void
    let onLongPressEnd: () -> Void

    private let imageSize: CGFloat = 76

    @State private var isHovered = false

    var body: some View {
        // Стикеры маленькие (WebP, обычно 30–100 КБ) — нет смысла грузить
        // отдельный preview-файл. Используем основной fileID.
        StickerImageView(fileID: sticker.fileID, size: imageSize)
            .padding(2)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(isHovered ? Color.primary.opacity(0.06) : .clear)
            )
            .scaleEffect(isHovered ? 1.04 : 1.0)
            .animation(.easeInOut(duration: 0.12), value: isHovered)
            .onHover { isHovered = $0 }
            .contentShape(Rectangle())
            .onTapGesture { onTap() }
            .onLongPressGesture(minimumDuration: 0.6) {
                onLongPressStart()
            } onPressingChanged: { pressing in
                if !pressing {
                    onLongPressEnd()
                }
            }
            .help(sticker.emoji.isEmpty ? "" : sticker.emoji)
    }
}
