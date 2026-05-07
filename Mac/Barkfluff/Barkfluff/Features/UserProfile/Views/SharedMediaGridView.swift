//
//  SharedMediaGridView.swift
//  Barkfluff
//
//  Сетка общих медиа файлов
//

import SwiftUI
import BFCore
import Nuke
import NukeUI

/// Сетка медиа файлов (изображения, видео, GIF)
struct SharedMediaGridView: View {
    let items: [SharedMediaItem]
    let fileService: FileServiceProtocol
    let isLoading: Bool
    let onLoadMore: () -> Void

    @Environment(DependencyContainer.self) private var container

    private let columns = [
        GridItem(.flexible(), spacing: 2),
        GridItem(.flexible(), spacing: 2),
        GridItem(.flexible(), spacing: 2)
    ]

    var body: some View {
        LazyVGrid(columns: columns, spacing: 2) {
            ForEach(items) { item in
                SharedMediaThumbnailView(
                    item: item,
                    fileService: fileService,
                    allItems: items
                )
                .aspectRatio(1, contentMode: .fit)
                .clipped()
                .onAppear {
                    // Пагинация: загрузить ещё при приближении к концу
                    if item.id == items.last?.id {
                        onLoadMore()
                    }
                }
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 8))

        if isLoading {
            HStack {
                Spacer()
                ProgressView()
                    .controlSize(.small)
                Spacer()
            }
            .padding(.vertical, Theme.Spacing.sm)
        }
    }
}

/// Одна ячейка-превью в сетке
struct SharedMediaThumbnailView: View {
    let item: SharedMediaItem
    let fileService: FileServiceProtocol
    let allItems: [SharedMediaItem]

    @Environment(DependencyContainer.self) private var container

    var body: some View {
        Color.clear
            .overlay {
                CachedImageView(
                    fileID: previewCacheFileID,
                    type: .preview,
                    presignedURLHint: item.previewURL,
                    content: { image in
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    },
                    placeholder: { loadingPlaceholder }
                )
            }
            .clipped()
            .overlay {
                // Оверлей для видео — иконка play
                if item.type == .video {
                    Color.black.opacity(0.3)
                    Image(systemName: "play.circle.fill")
                        .font(.title2)
                        .foregroundStyle(.white)
                }

                // Оверлей для GIF — лейбл
                if item.type == .gif {
                    VStack {
                        Spacer()
                        HStack {
                            Text("GIF")
                                .font(.caption2.bold())
                                .foregroundStyle(.white)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(Capsule().fill(.black.opacity(0.6)))
                            Spacer()
                        }
                        .padding(4)
                    }
                }
            }
            .contentShape(Rectangle())
            .onTapGesture {
                openMediaViewer()
            }
    }

    private var previewCacheFileID: String {
        if let previewFileID = item.previewFileID, !previewFileID.isEmpty {
            return previewFileID
        }
        if let url = item.previewURL, let fid = S3URLParser.fileID(from: url) {
            return fid
        }
        return item.fileID
    }

    private var loadingPlaceholder: some View {
        Color.gray.opacity(0.1)
            .overlay { ProgressView().controlSize(.small) }
    }

    private var errorPlaceholder: some View {
        Color.gray.opacity(0.1)
            .overlay {
                Image(systemName: "photo")
                    .foregroundStyle(.quaternary)
            }
    }

    private func openMediaViewer() {
        // Конвертируем SharedMediaItem в MessageAttachment для FullScreenMediaWindowManager
        let attachments = allItems.map { item in
            MessageAttachment(
                id: item.id,
                type: item.type,
                fileID: item.fileID,
                fileName: item.fileName,
                fileSize: item.fileSize,
                previewURL: item.previewURL
            )
        }

        guard let index = allItems.firstIndex(where: { $0.id == item.id }) else { return }

        FullScreenMediaWindowManager.shared.openMediaViewer(
            attachments: attachments,
            initialIndex: index,
            messageText: nil,
            container: container
        )
    }
}

#Preview {
    let container = DependencyContainer()

    SharedMediaGridView(
        items: [
            SharedMediaItem(
                id: 1,
                messageID: 1,
                type: .image,
                fileID: "test",
                previewURL: nil,
                previewFileID: nil,
                fileName: "image.jpg",
                fileSize: 1024,
                sentAt: Date(),
                senderID: 1
            )
        ],
        fileService: container.fileService,
        isLoading: false,
        onLoadMore: {}
    )
    .environment(container)
    .frame(width: 280, height: 100)
    .padding()
}
