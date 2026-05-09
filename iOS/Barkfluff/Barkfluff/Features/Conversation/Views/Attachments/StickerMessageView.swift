//
//  StickerMessageView.swift
//  Barkfluff (iOS)
//
//  Отрисовка стикера в чате через UIImage (поддержка WebP нативно с iOS 14+).
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
