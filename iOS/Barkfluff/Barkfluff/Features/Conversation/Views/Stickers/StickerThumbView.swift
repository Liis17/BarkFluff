//
//  StickerThumbView.swift
//  Barkfluff (iOS)
//
//  Превью стикера в сетке пикера: tap для отправки + long-press для overlay.
//

import SwiftUI
import BFCore

struct StickerThumbView: View {
    let sticker: Sticker
    let onTap: () -> Void
    let onLongPressStart: () -> Void
    let onLongPressEnd: () -> Void

    private let imageSize: CGFloat = 76

    var body: some View {
        StickerImageView(fileID: sticker.fileID, size: imageSize)
            .padding(2)
            .contentShape(Rectangle())
            .onTapGesture { onTap() }
            .onLongPressGesture(minimumDuration: 0.5, maximumDistance: 10) {
                onLongPressStart()
            } onPressingChanged: { pressing in
                if !pressing {
                    onLongPressEnd()
                }
            }
    }
}
