//
//  GIFAttachmentView.swift
//  Barkfluff
//
//  Отображение GIF-вложения (Nuke 12+ поддерживает GIF автоматически)
//

import SwiftUI
import Nuke
import NukeUI
import BFCore

struct GIFAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize
    let onTap: () -> Void

    @Environment(DependencyContainer.self) private var container

    var body: some View {
        ZStack {
            if let gifURL = resolvedGIFURL {
                // Nuke 12+ автоматически воспроизводит GIF
                LazyImage(url: gifURL) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                            .frame(width: targetSize.width, height: targetSize.height)
                            .clipped()
                            .clipShape(RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous))
                    } else if state.isLoading {
                        loadingPlaceholder
                    } else {
                        errorPlaceholder
                    }
                }
            } else {
                // Асинхронная загрузка URL
                AsyncGIFView(
                    fileID: attachment.fileID,
                    fileService: container.fileService,
                    targetSize: targetSize,
                    onTap: onTap
                )
            }

            // GIF бейдж
            gifBadge
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .contentShape(Rectangle())
        .onTapGesture(perform: onTap)
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

    private var gifBadge: some View {
        Text("GIF")
            .font(.caption2)
            .fontWeight(.semibold)
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .glassEffect(.clear, in: .capsule)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .padding(6)
    }

    // MARK: - Private

    private var resolvedGIFURL: URL? {
        // Для GIF используем полный fileID (не preview)
        if let previewURL = attachment.previewURL, !previewURL.isEmpty {
            return URL(string: previewURL)
        }
        return nil
    }
}

// MARK: - Async GIF Loader

private struct AsyncGIFView: View {
    let fileID: String
    let fileService: FileServiceProtocol
    let targetSize: CGSize
    let onTap: () -> Void

    @State private var gifURL: URL?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let url = gifURL {
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
            await loadGIFURL()
        }
    }

    private func loadGIFURL() async {
        do {
            let urlString = try await fileService.getDownloadURL(fileID: fileID)
            gifURL = URL(string: urlString)
        } catch {
            // Ignore
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
    GIFAttachmentView(
        attachment: MessageAttachment(
            id: 1,
            type: .gif,
            fileID: "test",
            fileName: "animation.gif",
            fileSize: 2_500_000,
            previewURL: nil
        ),
        targetSize: CGSize(width: 200, height: 200),
        onTap: {}
    )
    .environment(DependencyContainer())
}
