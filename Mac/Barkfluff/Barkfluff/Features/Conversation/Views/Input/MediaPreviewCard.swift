//
//  MediaPreviewCard.swift
//  Barkfluff
//
//  Карточка превью медиа-файла (изображение/видео)
//

import SwiftUI
import AVFoundation

struct MediaPreviewCard: View {
    let attachment: SelectedAttachment
    let progress: Double?
    let onRemove: () -> Void

    @State private var thumbnail: NSImage?

    var body: some View {
        ZStack(alignment: .topTrailing) {
            // Миниатюра
            Group {
                if let thumbnail {
                    Image(nsImage: thumbnail)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    Rectangle()
                        .fill(.quaternary)
                        .overlay {
                            ProgressView()
                                .controlSize(.small)
                        }
                }
            }
            .frame(width: 56, height: 56)
            .clipShape(RoundedRectangle(cornerRadius: 8))

            // Progress overlay
            if let progress, progress < 1.0 {
                RoundedRectangle(cornerRadius: 8)
                    .fill(.black.opacity(0.4))
                    .frame(width: 56, height: 56)
                    .overlay {
                        ProgressView(value: progress)
                            .progressViewStyle(.circular)
                            .tint(.white)
                            .controlSize(.small)
                    }
            }

            // Бейдж видео
            if attachment.isVideo {
                HStack(spacing: 2) {
                    Image(systemName: "play.fill")
                        .font(.system(size: 7))
                }
                .font(.caption2)
                .foregroundStyle(.white)
                .padding(.horizontal, 4)
                .padding(.vertical, 2)
                .background(.black.opacity(0.6))
                .clipShape(RoundedRectangle(cornerRadius: 4))
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomLeading)
                .padding(3)
            }

            // Кнопка удаления
            removeButton
        }
        .frame(width: 56, height: 56)
        .task(id: attachment.id) {
            await loadThumbnail()
        }
    }

    private var removeButton: some View {
        Button(action: onRemove) {
            Image(systemName: "xmark.circle.fill")
                .font(.system(size: 14))
                .foregroundStyle(.white)
                .shadow(color: .black.opacity(0.3), radius: 1, x: 0, y: 1)
        }
        .buttonStyle(.plain)
        .offset(x: 4, y: -4)
    }

    // MARK: - Thumbnail Loading

    private func loadThumbnail() async {
        switch attachment {
        case .fileURL(let url, _, _):
            // Security-scoped resource access
            guard url.startAccessingSecurityScopedResource() else {
                // Если нет доступа, пробуем без него (может работать для некоторых URL)
                if attachment.isVideo {
                    thumbnail = await generateVideoThumbnail(url: url)
                } else {
                    thumbnail = NSImage(contentsOf: url)
                }
                return
            }
            defer { url.stopAccessingSecurityScopedResource() }

            if attachment.isVideo {
                thumbnail = await generateVideoThumbnail(url: url)
            } else {
                thumbnail = NSImage(contentsOf: url)
            }
        case .imageData(let data, _, _):
            thumbnail = NSImage(data: data)
        }
    }

    private func generateVideoThumbnail(url: URL) async -> NSImage? {
        let asset = AVURLAsset(url: url)
        let generator = AVAssetImageGenerator(asset: asset)
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: 112, height: 112)

        do {
            let (cgImage, _) = try await generator.image(at: .zero)
            return NSImage(cgImage: cgImage, size: NSSize(width: cgImage.width, height: cgImage.height))
        } catch {
            return nil
        }
    }
}

#Preview {
    HStack(spacing: 8) {
        // Placeholder preview
        ZStack {
            Rectangle()
                .fill(.quaternary)
                .frame(width: 56, height: 56)
                .clipShape(RoundedRectangle(cornerRadius: 8))
            Image(systemName: "photo")
                .foregroundStyle(.secondary)
        }

        // With remove button
        ZStack(alignment: .topTrailing) {
            Rectangle()
                .fill(.blue.opacity(0.3))
                .frame(width: 56, height: 56)
                .clipShape(RoundedRectangle(cornerRadius: 8))

            Image(systemName: "xmark.circle.fill")
                .font(.system(size: 14))
                .foregroundStyle(.white)
                .shadow(color: .black.opacity(0.3), radius: 1)
                .offset(x: 4, y: -4)
        }
    }
    .padding()
}
