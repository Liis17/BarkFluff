//
//  StickerMessageView.swift
//  Barkfluff
//
//  Отрисовка стикера в чате через NSImage (поддержка WebP нативно с macOS 11+).
//
//  Используется в двух режимах:
//  - «чистый» стикер (без текста и других вложений) — большой 180×180,
//    рисуется без bubble-фона прямо в MessageBubbleView;
//  - стикер в составе bubble (с текстом или другими вложениями) — компактный 140×140.
//

import SwiftUI
import BFCore

struct StickerMessageView: View {
    let attachment: MessageAttachment
    var size: CGFloat = 180

    var body: some View {
        StickerImageView(fileID: attachment.fileID, size: size)
    }
}
