//
//  CachedImageView.swift
//  Barkfluff (iOS)
//
//  Обёртка над Nuke.LazyImage, которая сначала идёт в MediaCacheManager
//  за локальным URL, а уже его передаёт декодеру.
//

import SwiftUI
import Nuke
import NukeUI
import BFCore

struct CachedImageView<Content: View, Placeholder: View>: View {

    let fileID: String?
    let type: CachedFileType
    /// Подсказка с уже известным presigned URL (для аватаров, где fileID
    /// извлечён из самого URL и getDownloadURL не сработает).
    let presignedURLHint: String?
    let processors: [any ImageProcessing]
    let content: (Image) -> Content
    let placeholder: () -> Placeholder

    @Environment(DependencyContainer.self) private var container
    @State private var localURL: URL?
    @State private var isLoading: Bool = true
    @State private var didFail: Bool = false

    init(
        fileID: String?,
        type: CachedFileType,
        presignedURLHint: String? = nil,
        processors: [any ImageProcessing] = [],
        @ViewBuilder content: @escaping (Image) -> Content,
        @ViewBuilder placeholder: @escaping () -> Placeholder
    ) {
        self.fileID = fileID
        self.type = type
        self.presignedURLHint = presignedURLHint
        self.processors = processors
        self.content = content
        self.placeholder = placeholder
    }

    var body: some View {
        Group {
            if let url = localURL {
                LazyImage(url: url) { state in
                    if let image = state.image {
                        content(image)
                    } else if state.isLoading {
                        placeholder()
                    } else {
                        placeholder()
                    }
                }
                .processors(processors)
            } else {
                placeholder()
            }
        }
        .task(id: fileID ?? "") {
            await resolve()
        }
    }

    private func resolve() async {
        guard let fileID, !fileID.isEmpty else {
            localURL = nil
            isLoading = false
            return
        }
        isLoading = true
        didFail = false
        do {
            let url = try await container.mediaCacheManager.resolveURL(
                for: fileID,
                type: type,
                presignedURLHint: presignedURLHint
            )
            localURL = url
        } catch {
            didFail = true
            localURL = nil
        }
        isLoading = false
    }
}
