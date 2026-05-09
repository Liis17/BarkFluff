//
//  AttachmentGridView.swift
//  Barkfluff
//
//  Сетка вложений с автоматической раскладкой (iOS версия)
//  Использует Nuke для кэширования изображений
//

import SwiftUI
import Nuke
import NukeUI
import BFCore


struct AttachmentGridView: View {
    let attachments: [MessageAttachment]
    let isOwn: Bool
    let onTap: (MessageAttachment) -> Void

    @Environment(DependencyContainer.self) private var container

    var body: some View {
        let layout = AttachmentLayoutCalculator.calculateLayout(
            for: attachments,
            containerWidth: 260
        )

        ZStack {
            ForEach(layout.items) { item in
                if let attachment = attachments[safe: item.index] {
                    attachmentView(
                        for: attachment,
                        size: item.size,
                        frame: item.frame
                    )
                }
            }

            // Бейдж "+N" для скрытых вложений
            if layout.hiddenCount > 0, let lastItem = layout.items.last {
                hiddenCountBadge(
                    count: layout.hiddenCount,
                    frame: lastItem.frame
                )
            }
        }
        .frame(width: layout.containerSize.width, height: layout.containerSize.height)
    }

    // MARK: - Private Views

    @ViewBuilder
    private func attachmentView(
        for attachment: MessageAttachment,
        size: CGSize,
        frame: CGRect
    ) -> some View {
        switch attachment.type {
        case .image:
            CachedImageURLView(
                fileID: previewFileID(for: attachment),
                targetSize: size,
                fileService: container.fileService,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .clipped()
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .video:
            ZStack {
                CachedImageURLView(
                    fileID: previewFileID(for: attachment),
                    targetSize: size,
                    fileService: container.fileService,
                    onTap: { }
                )
                .frame(width: frame.width, height: frame.height)
                .clipped()
                .position(
                    x: frame.midX,
                    y: frame.midY
                )

                // Play icon
                Image(systemName: "play.circle.fill")
                    .font(.system(size: 36))
                    .foregroundStyle(.white)
                    .shadow(radius: 4)
                    .position(
                        x: frame.midX,
                        y: frame.midY
                    )
            }

        case .gif:
            CachedImageURLView(
                fileID: previewFileID(for: attachment),
                targetSize: size,
                fileService: container.fileService,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .clipped()
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .document:
            DocumentAttachmentView(
                attachment: attachment,
                uploadProgress: nil
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .audio, .voice:
            AudioAttachmentView(
                attachment: attachment,
                uploadProgress: nil
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .sticker:
            ImageAttachmentView(
                attachment: attachment,
                isOwn: isOwn,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .forwardedMessage:
            EmptyView() // отрисовывается отдельно в MessageBubbleView
        }
    }

    private func hiddenCountBadge(count: Int, frame: CGRect) -> some View {
        Text("+\(count)")
            .font(.title2)
            .fontWeight(.semibold)
            .foregroundStyle(.white)
            .frame(width: frame.width, height: frame.height)
            .background(
                RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous)
                    .fill(.black.opacity(0.6))
            )
            .clipShape(RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous))
            .position(
                x: frame.midX,
                y: frame.midY
            )
    }

    /// Получить ID файла для превью (приоритет previewFileID)
    private func previewFileID(for attachment: MessageAttachment) -> String {
        if let previewFileID = attachment.previewFileID, !previewFileID.isEmpty {
            return previewFileID
        }
        return attachment.fileID
    }
}

// MARK: - Array Extension

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}

// MARK: - Cached Image Loader (с Nuke)

/// Загрузчик изображений с кэшированием через Nuke по fileID
struct CachedImageURLView: View {
    let fileID: String
    let targetSize: CGSize
    let fileService: FileServiceProtocol
    let onTap: () -> Void

    @Environment(DependencyContainer.self) private var container
    @State private var imageRequest: ImageRequest?
    @State private var isLoading = true
    @State private var hasError = false

    var body: some View {
        Group {
            if let request = imageRequest {
                LazyImage(request: request) { state in
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
                .pipeline(container.imagePipeline)
            } else if isLoading {
                loadingPlaceholder
            } else {
                errorPlaceholder
            }
        }
        .frame(width: targetSize.width, height: targetSize.height)
        .clipped()
        .contentShape(Rectangle())
        .onTapGesture(perform: onTap)
        .task(id: fileID) {
            await loadURL()
        }
    }

    private func loadURL() async {
        isLoading = true
        hasError = false
        do {
            let urlString = try await fileService.getDownloadURL(fileID: fileID)
            if let url = URL(string: urlString) {
                imageRequest = ImageRequest(url: url, userInfo: [.imageIdKey: fileID])
            }
        } catch {
            hasError = true
        }
        isLoading = false
    }

    private var loadingPlaceholder: some View {
        Color(uiColor: .tertiarySystemFill)
            .overlay {
                ProgressView()
                    .controlSize(.regular)
                    .tint(.secondary)
            }
    }

    private var errorPlaceholder: some View {
        Color(uiColor: .tertiarySystemFill)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }
}

#Preview {
    VStack(spacing: 24) {
        // 1 вложение
        AttachmentGridView(
            attachments: [
                MessageAttachment(
                    id: 1,
                    type: .image,
                    fileID: "1",
                    fileName: "photo.jpg",
                    fileSize: 1_000_000
                )
            ],
            isOwn: false,
            onTap: { _ in }
        )
        .border(.gray)

        // 2 вложения
        AttachmentGridView(
            attachments: [
                MessageAttachment(id: 1, type: .image, fileID: "1", fileName: "1.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 2, type: .image, fileID: "2", fileName: "2.jpg", fileSize: 1_000_000)
            ],
            isOwn: false,
            onTap: { _ in }
        )
        .border(.gray)

        // 3 вложения
        AttachmentGridView(
            attachments: [
                MessageAttachment(id: 1, type: .image, fileID: "1", fileName: "1.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 2, type: .image, fileID: "2", fileName: "2.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 3, type: .image, fileID: "3", fileName: "3.jpg", fileSize: 1_000_000)
            ],
            isOwn: false,
            onTap: { _ in }
        )
        .border(.gray)

        // 4+ вложения
        AttachmentGridView(
            attachments: [
                MessageAttachment(id: 1, type: .image, fileID: "1", fileName: "1.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 2, type: .image, fileID: "2", fileName: "2.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 3, type: .image, fileID: "3", fileName: "3.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 4, type: .image, fileID: "4", fileName: "4.jpg", fileSize: 1_000_000),
                MessageAttachment(id: 5, type: .image, fileID: "5", fileName: "5.jpg", fileSize: 1_000_000)
            ],
            isOwn: false,
            onTap: { _ in }
        )
        .border(.gray)
    }
    .padding()
    .environment(DependencyContainer())
}
