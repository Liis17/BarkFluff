//
//  VideoAttachmentView.swift
//  Barkfluff
//
//  Отображение видео-вложения с превью и кнопкой Play
//

import SwiftUI
import Nuke
import NukeUI
import BFCore
import AVKit

struct VideoAttachmentView: View {
    let attachment: MessageAttachment
    let targetSize: CGSize
    let onTap: () -> Void

    @Environment(DependencyContainer.self) private var container

    var body: some View {
        ZStack {
            // Фон / Превью
            previewView

            // Кнопка Play
            playButton

            // Длительность (если есть)
            if let duration = attachmentDuration {
                durationBadge(duration)
            }
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .clipShape(RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous))
        .contentShape(Rectangle())
        .onTapGesture(perform: onTap)
    }

    // MARK: - Private Views

    @ViewBuilder
    private var previewView: some View {
        if let previewURL = resolvedPreviewURL {
            LazyImage(url: previewURL) { state in
                if let image = state.image {
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    placeholderView
                }
            }
            .processors([.resize(size: targetSize)])
        } else {
            AsyncPreviewView(
                previewFileID: previewFileID,
                fileService: container.fileService,
                targetSize: targetSize
            )
        }
    }

    private var placeholderView: some View {
        RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "video.fill")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }

    private var playButton: some View {
        // Используем .glassEffect(.clear) для мелких контролов поверх медиа
        Button { onTap() } label: {
            Image(systemName: "play.fill")
                .font(.title)
                .foregroundStyle(.white)
                .frame(width: 50, height: 50)
        }
        .buttonStyle(.plain)
        .glassEffect(.clear, in: .circle)
        .shadow(color: .black.opacity(0.3), radius: 4, x: 0, y: 2)
    }

    private func durationBadge(_ duration: String) -> some View {
        Text(duration)
            .font(.caption2)
            .fontWeight(.medium)
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .glassEffect(.clear, in: .capsule)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)
            .padding(6)
    }

    // MARK: - Private

    private var resolvedPreviewURL: URL? {
        if let previewURL = attachment.previewURL, !previewURL.isEmpty {
            return URL(string: previewURL)
        }
        return nil
    }

    private var previewFileID: String {
        if let previewFileID = attachment.previewFileID, !previewFileID.isEmpty {
            return previewFileID
        }
        return attachment.fileID
    }

    /// Извлечь длительность из имени файла или метаданных
    /// (в реальном приложении должно приходить с бэкенда)
    private var attachmentDuration: String? {
        // Пока заглушка - в реальности должно быть в attachment.metadata
        return nil
    }
}

// MARK: - Async Preview Loader

private struct AsyncPreviewView: View {
    let previewFileID: String
    let fileService: FileServiceProtocol
    let targetSize: CGSize

    @State private var previewURL: URL?

    var body: some View {
        Group {
            if let url = previewURL {
                LazyImage(url: url) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    } else {
                        placeholderView
                    }
                }
                .processors([.resize(size: targetSize)])
            } else {
                placeholderView
            }
        }
        .task {
            await loadPreviewURL()
        }
    }

    private func loadPreviewURL() async {
        do {
            let urlString = try await fileService.getDownloadURL(fileID: previewFileID)
            previewURL = URL(string: urlString)
        } catch {
            // Ignore
        }
    }

    private var placeholderView: some View {
        RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "video.fill")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }
}

#Preview {
    VideoAttachmentView(
        attachment: MessageAttachment(
            id: 1,
            type: .video,
            fileID: "test",
            fileName: "video.mp4",
            fileSize: 15_000_000,
            previewURL: nil
        ),
        targetSize: CGSize(width: 200, height: 150),
        onTap: {}
    )
    .environment(DependencyContainer())
}
