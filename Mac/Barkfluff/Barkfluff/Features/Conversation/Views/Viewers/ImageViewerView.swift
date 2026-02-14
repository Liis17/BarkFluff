//
//  ImageViewerView.swift
//  Barkfluff
//
//  Полноэкранный просмотр изображения с zoom и pan
//

import SwiftUI
import Nuke
import NukeUI
import BFCore

struct ImageViewerView: View {
    let attachment: MessageAttachment

    @Environment(\.dismiss) private var dismiss
    @Environment(DependencyContainer.self) private var container

    @State private var scale: CGFloat = 1.0
    @State private var lastScale: CGFloat = 1.0
    @State private var offset: CGSize = .zero
    @State private var lastOffset: CGSize = .zero
    @State private var imageURL: URL?
    @State private var isLoading = true

    private let minScale: CGFloat = 1.0
    private let maxScale: CGFloat = 5.0

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            if let url = imageURL {
                imageView(url: url)
            } else if isLoading {
                ProgressView()
                    .controlSize(.large)
                    .tint(.white)
            } else {
                errorView
            }
        }
        .gesture(
            SimultaneousGesture(
                magnificationGesture,
                dragGesture
            )
        )
        .onTapGesture(count: 2) {
            withAnimation(.spring) {
                if scale > 1.0 {
                    scale = 1.0
                    offset = .zero
                } else {
                    scale = 2.0
                }
            }
        }
        .task {
            await loadImage()
        }
    }

    // MARK: - Private Views

    @ViewBuilder
    private func imageView(url: URL) -> some View {
        LazyImage(url: url) { state in
            if let image = state.image {
                image
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .scaleEffect(scale)
                    .offset(offset)
            } else {
                ProgressView()
                    .controlSize(.large)
                    .tint(.white)
            }
        }
    }

    private var errorView: some View {
        VStack(spacing: 16) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)

            Text("Не удалось загрузить изображение")
                .font(.headline)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Gestures

    private var magnificationGesture: some Gesture {
        MagnificationGesture()
            .onChanged { value in
                let newScale = lastScale * value
                scale = min(max(newScale, minScale), maxScale)
            }
            .onEnded { _ in
                lastScale = scale

                // Если масштаб меньше 1, возвращаем к 1
                if scale < 1.0 {
                    withAnimation(.spring) {
                        scale = 1.0
                        lastScale = 1.0
                    }
                }
            }
    }

    private var dragGesture: some Gesture {
        DragGesture()
            .onChanged { value in
                // Разрешаем pan только при зуме
                if scale > 1.0 {
                    offset = CGSize(
                        width: lastOffset.width + value.translation.width,
                        height: lastOffset.height + value.translation.height
                    )
                }
            }
            .onEnded { _ in
                lastOffset = offset
            }
    }

    // MARK: - Private Methods

    private func loadImage() async {
        do {
            var url: URL?

            if let previewURL = attachment.previewURL, !previewURL.isEmpty {
                url = URL(string: previewURL)
            } else {
                let urlString = try await container.fileService.getDownloadURL(fileID: attachment.fileID)
                url = URL(string: urlString)
            }

            imageURL = url
        } catch {
            // Error loading
        }

        isLoading = false
    }
}

#Preview {
    ImageViewerView(
        attachment: MessageAttachment(
            id: 1,
            type: .image,
            fileID: "test",
            fileName: "photo.jpg",
            fileSize: 2_000_000
        )
    )
    .environment(DependencyContainer())
}
