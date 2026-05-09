//
//  StickerImageView.swift
//  Barkfluff
//
//  Универсальный рендер стикера (WebP) через NSImage + MediaCacheManager.
//  Не зависит от Nuke — NSImage на macOS 11+ декодирует WebP нативно через ImageIO.
//
//  Загрузка идёт через `MediaCacheManager.resolveURL`, который дёргает
//  свежий presigned URL через `getTempDownloadURL`. Это устойчиво
//  к протухшим `file_url` / `preview_url` из StickerInfo / MessageAttachment.
//

import SwiftUI
import AppKit
import BFCore

struct StickerImageView<Placeholder: View>: View {
    let fileID: String
    let size: CGFloat
    @ViewBuilder let placeholder: () -> Placeholder

    @Environment(DependencyContainer.self) private var container
    @State private var image: NSImage?
    @State private var didFail = false

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image)
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
            if let nsImage = NSImage(contentsOf: url) {
                await MainActor.run { self.image = nsImage }
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
