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
        .clipShape(RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous))
        .contentShape(Rectangle())
        .onTapGesture(perform: onTap)
        .contextMenu {
            Button {
                Task {
                    try? await FileDownloadHelper.downloadToDownloads(
                        fileID: attachment.fileID,
                        fileName: attachment.fileName,
                        fileService: container.fileService,
                        mediaCacheManager: container.mediaCacheManager,
                        cacheType: attachment.type.cacheType
                    )
                }
            } label: {
                Label("conversation.message.context_menu.download_original", systemImage: "arrow.down.circle")
            }
        }
    }

    // MARK: - Private Views

    @ViewBuilder
    private var previewView: some View {
        CachedImageView(
            fileID: previewCacheFileID,
            type: .preview,
            presignedURLHint: attachment.previewURL,
            processors: [.resize(size: targetSize)],
            content: { image in
                image
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            },
            placeholder: { placeholderView }
        )
    }

    private var placeholderView: some View {
        RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous)
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

    private var previewCacheFileID: String {
        if let previewFileID = attachment.previewFileID, !previewFileID.isEmpty {
            return previewFileID
        }
        if let previewURL = attachment.previewURL,
           let fid = S3URLParser.fileID(from: previewURL) {
            return fid
        }
        return attachment.fileID
    }

    private var attachmentDuration: String? {
        return nil
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
