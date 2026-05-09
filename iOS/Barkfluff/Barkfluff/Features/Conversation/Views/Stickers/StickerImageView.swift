//
//  StickerImageView.swift
//  Barkfluff (iOS)
//
//  Универсальный рендер стикера (WebP) через UIImage + MediaCacheManager.
//  iOS декодирует WebP нативно с iOS 14+ через ImageIO.
//

import SwiftUI
import UIKit
import BFCore

struct StickerImageView<Placeholder: View>: View {
    let fileID: String
    let size: CGFloat
    @ViewBuilder let placeholder: () -> Placeholder

    @Environment(DependencyContainer.self) private var container
    @State private var image: UIImage?
    @State private var didFail = false

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .frame(width: size, height: size)
            } else {
                placeholder()
                    .frame(width: size, height: size)
            }
        }
        .task(id: fileID) {
            await load()
        }
    }

    private func load() async {
        guard !fileID.isEmpty else {
            didFail = true
            return
        }
        do {
            let url = try await container.mediaCacheManager.resolveURL(
                for: fileID,
                type: .sticker
            )
            if let uiImage = UIImage(contentsOfFile: url.path) {
                await MainActor.run { self.image = uiImage }
            } else {
                await MainActor.run { self.didFail = true }
            }
        } catch {
            await MainActor.run { self.didFail = true }
        }
    }
}

extension StickerImageView where Placeholder == StickerImagePlaceholder {
    init(fileID: String, size: CGFloat) {
        self.init(fileID: fileID, size: size) {
            StickerImagePlaceholder()
        }
    }
}

struct StickerImagePlaceholder: View {
    var body: some View {
        RoundedRectangle(cornerRadius: 10)
            .fill(.fill.tertiary)
    }
}
