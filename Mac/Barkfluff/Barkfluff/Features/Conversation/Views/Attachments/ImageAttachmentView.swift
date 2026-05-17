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
            CachedImageView(
                fileID: previewCacheFileID,
                type: .preview,
                presignedURLHint: attachment.previewURL,
                processors: [.resize(size: targetSize)],
                content: { image in
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                        .frame(width: targetSize.width, height: targetSize.height)
                        .clipped()
                        .clipShape(RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous))
                        .contentShape(Rectangle())
                        .onTapGesture(perform: onTap)
                },
                placeholder: { loadingPlaceholder }
            )
        }
        .frame(width: targetSize.width, height: targetSize.height)
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

    private var loadingPlaceholder: some View {
        RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                ProgressView()
                    .controlSize(.small)
            }
    }

    private var errorPlaceholder: some View {
        RoundedRectangle(cornerRadius: CGFloat(container.personalizationSettings.bubbleCornerRadius), style: .continuous)
            .fill(.fill.tertiary)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
            .onTapGesture(perform: onTap)
    }

    // MARK: - Private

    /// fileID, по которому кешируем превью.
    /// Приоритет: явный previewFileID → S3-key из previewURL → основной fileID.
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
