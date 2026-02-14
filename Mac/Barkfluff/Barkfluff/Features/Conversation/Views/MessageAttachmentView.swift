//
//  MessageAttachmentView.swift
//  Barkfluff
//
//  Отображение вложения в сообщении
//

import SwiftUI
import BFCore

struct MessageAttachmentView: View {
    let attachment: MessageAttachment

    var body: some View {
        Group {
            switch attachment.type {
            case .image, .gif:
                imageAttachment
            case .video:
                videoAttachment
            case .document, .audio:
                fileAttachment
            }
        }
    }

    @ViewBuilder
    private var imageAttachment: some View {
        if let previewURL = attachment.previewURL, let url = URL(string: previewURL) {
            AsyncImage(url: url) { phase in
                switch phase {
                case .success(let image):
                    image
                        .resizable()
                        .scaledToFit()
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                case .failure:
                    imagePlaceholder
                default:
                    ProgressView()
                        .frame(width: 200, height: 150)
                }
            }
            .frame(maxWidth: 300, maxHeight: 300)
        } else {
            imagePlaceholder
        }
    }

    private var imagePlaceholder: some View {
        RoundedRectangle(cornerRadius: 8)
            .fill(.fill.tertiary)
            .frame(width: 200, height: 150)
            .overlay {
                Image(systemName: "photo")
                    .font(.largeTitle)
                    .foregroundStyle(.secondary)
            }
    }

    private var videoAttachment: some View {
        RoundedRectangle(cornerRadius: 8)
            .fill(.fill.tertiary)
            .frame(width: 240, height: 160)
            .overlay {
                VStack(spacing: 8) {
                    Image(systemName: "play.circle.fill")
                        .font(.largeTitle)
                        .foregroundStyle(.white)

                    Text(attachment.fileName)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
    }

    private var fileAttachment: some View {
        HStack(spacing: 10) {
            Image(systemName: attachment.type.systemImage)
                .font(.title2)
                .foregroundStyle(Color.accentColor)
                .frame(width: 36, height: 36)

            VStack(alignment: .leading, spacing: 2) {
                Text(attachment.fileName)
                    .font(.subheadline)
                    .lineLimit(1)

                Text(attachment.formattedSize)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(8)
        .background(.fill.tertiary)
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

#Preview {
    VStack(spacing: 16) {
        MessageAttachmentView(attachment: MessageAttachment(
            id: 1,
            type: .image,
            fileID: "file1",
            fileName: "photo.jpg",
            fileSize: 1_500_000,
            previewURL: nil
        ))

        MessageAttachmentView(attachment: MessageAttachment(
            id: 2,
            type: .document,
            fileID: "file2",
            fileName: "document.pdf",
            fileSize: 3_200_000
        ))
    }
    .padding()
}
