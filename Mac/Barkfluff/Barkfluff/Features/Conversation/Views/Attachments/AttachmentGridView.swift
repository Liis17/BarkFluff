//
//  AttachmentGridView.swift
//  Barkfluff
//
//  Сетка вложений с автоматической раскладкой
//

import SwiftUI
import BFCore

struct AttachmentGridView: View {
    let attachments: [MessageAttachment]
    let isOwn: Bool
    let onTap: (MessageAttachment) -> Void

    var body: some View {
        let layout = AttachmentLayoutCalculator.calculateLayout(
            for: attachments,
            containerWidth: 300
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
            ImageAttachmentView(
                attachment: attachment,
                targetSize: size,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .video:
            VideoAttachmentView(
                attachment: attachment,
                targetSize: size,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .gif:
            GIFAttachmentView(
                attachment: attachment,
                targetSize: size,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .document:
            // Документы не в сетке, а списком
            DocumentAttachmentView(
                attachment: attachment,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .audio, .voice:
            AudioAttachmentView(
                attachment: attachment,
                onTap: { onTap(attachment) }
            )
            .frame(width: frame.width, height: frame.height)
            .position(
                x: frame.midX,
                y: frame.midY
            )

        case .sticker:
            ImageAttachmentView(
                attachment: attachment,
                targetSize: size,
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
                RoundedRectangle(cornerRadius: 8)
                    .fill(.black.opacity(0.6))
            )
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .position(
                x: frame.midX,
                y: frame.midY
            )
    }
}

// MARK: - Array Extension

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}

// MARK: - LayoutItem Identifiable

extension LayoutItem: Identifiable {
    public var id: Int { index }
}

// MARK: - Preview

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
