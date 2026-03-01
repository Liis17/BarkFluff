//
//  ImageAttachmentView.swift
//  Barkfluff
//
//  Отображение изображения-вложения с Nuke
//

import SwiftUI
import Nuke
import NukeUI
import BFCore

struct ImageAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize
    let onTap: () -> Void

    @Environment(DependencyContainer.self) private var container

    var body: some View {
        ZStack {
            if let imageURL = resolvedImageURL {
                LazyImage(url: imageURL) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                            .frame(width: targetSize.width, height: targetSize.height)
                            .clipped()
                            .clipShape(RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous))
                            .contentShape(Rectangle())
                            .onTapGesture(perform: onTap)
                    } else if state.isLoading {
                        loadingPlaceholder
                    } else {
                        errorPlaceholder
                    }
                }
                .processors([.resize(size: targetSize)])
            } else {
                // Нужно получить URL асинхронно
                asyncImageView
            }
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .contextMenu {
            Button {
                Task {
                    try? await FileDownloadHelper.downloadToDownloads(
                        fileID: attachment.fileID,
                        fileName: attachment.fileName,
                        fileService: container.fileService
                    )
                }
            } label: {
                Label("Скачать оригинал", systemImage: "arrow.down.circle")
            }
        }
    }

    // MARK: - Private Views

    @ViewBuilder
    private var asyncImageView: some View {
        AsyncImageURLView(
            fileID: previewFileID,
            fileService: container.fileService,
            targetSize: targetSize,
            onTap: onTap
        )
    }

    private var loadingPlaceholder: some View {
        RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                ProgressView()
                    .controlSize(.small)
            }
    }

    private var errorPlaceholder: some View {
        RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
            .onTapGesture(perform: onTap)
    }

    // MARK: - Private

    /// Приоритет превью: previewURL > fileID (через fileService)
    private var resolvedImageURL: URL? {
        // Если есть прямой previewURL - используем
        if let previewURL = attachment.previewURL, !previewURL.isEmpty {
            return URL(string: previewURL)
        }
        return nil
    }

    /// ID файла для превью
    private var previewFileID: String {
        if let previewFileID = attachment.previewFileID, !previewFileID.isEmpty {
            return previewFileID
        }
        return attachment.fileID
    }
}

// MARK: - Async Image Loader

private struct AsyncImageURLView: View {
    let fileID: String
    let fileService: FileServiceProtocol
    let targetSize: CGSize
    let onTap: () -> Void

    @State private var imageURL: URL?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let url = imageURL {
                LazyImage(url: url) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    } else if state.isLoading {
                        loadingPlaceholder
                    } else {
                        errorPlaceholder
                    }
                }
                .processors([.resize(size: targetSize)])
            } else if isLoading {
                loadingPlaceholder
            } else {
                errorPlaceholder
            }
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .clipped()
        .clipShape(RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous))
        .contentShape(Rectangle())
        .onTapGesture(perform: onTap)
        .task {
            await loadURL()
        }
    }

    private func loadURL() async {
        do {
            let urlString = try await fileService.getDownloadURL(fileID: fileID)
            imageURL = URL(string: urlString)
        } catch {
            // Ошибка - показываем плейсхолдер
        }
        isLoading = false
    }

    private var loadingPlaceholder: some View {
        RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                ProgressView()
                    .controlSize(.small)
            }
    }

    private var errorPlaceholder: some View {
        RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }
}

#Preview {
    ImageAttachmentView(
        attachment: MessageAttachment(
            id: 1,
            type: .image,
            fileID: "test",
            fileName: "photo.jpg",
            fileSize: 1_500_000,
            previewURL: nil
        ),
        targetSize: CGSize(width: 200, height: 200),
        onTap: {}
    )
    .environment(DependencyContainer())
}
