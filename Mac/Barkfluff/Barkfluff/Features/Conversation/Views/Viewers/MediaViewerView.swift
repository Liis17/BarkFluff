//
//  MediaViewerView.swift
//  Barkfluff
//
//  Полноэкранный просмотр медиа с навигацией между вложениями
//

import SwiftUI
import BFCore

struct MediaViewerView: View {
    let attachments: [MessageAttachment]
    let initialIndex: Int

    @Environment(\.dismiss) private var dismiss

    @State private var currentIndex: Int
    @State private var showControls = true

    init(attachments: [MessageAttachment], initialIndex: Int = 0) {
        self.attachments = attachments
        self.initialIndex = initialIndex
        self._currentIndex = State(initialValue: initialIndex)
    }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            // TabView для навигации
            TabView(selection: $currentIndex) {
                ForEach(Array(attachments.enumerated()), id: \.element.id) { index, attachment in
                    viewerContent(for: attachment)
                        .tag(index)
                }
            }
            #if os(iOS)
            .tabViewStyle(.page(indexDisplayMode: .automatic))
            #endif

            // Кнопка закрытия
            if showControls {
                closeButton
            }

            // Счётчик
            if showControls && attachments.count > 1 {
                counterBadge
            }
        }
        #if os(iOS)
        .statusBar(hidden: true)
        #endif
        .onTapGesture {
            withAnimation(.easeInOut(duration: 0.2)) {
                showControls.toggle()
            }
        }
    }

    // MARK: - Private Views

    @ViewBuilder
    private func viewerContent(for attachment: MessageAttachment) -> some View {
        switch attachment.type {
        case .image, .sticker:
            ImageViewerView(attachment: attachment)
                .ignoresSafeArea()

        case .video:
            VideoPlayerView(attachment: attachment)
                .ignoresSafeArea()

        case .gif:
            ImageViewerView(attachment: attachment)
                .ignoresSafeArea()

        case .document, .audio, .voice:
            DocumentViewerPlaceholder(attachment: attachment)

        case .forwardedMessage:
            EmptyView()
        }
    }

    private var closeButton: some View {
        VStack {
            HStack {
                Spacer()
                Button {
                    dismiss()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.title)
                        .foregroundStyle(.white)
                }
                .buttonStyle(.plain)
                .glassEffect(.clear, in: .circle)
                .padding()
            }
            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topTrailing)
    }

    private var counterBadge: some View {
        VStack {
            Spacer()
            Text("\(currentIndex + 1) / \(attachments.count)")
                .font(.caption)
                .fontWeight(.medium)
                .foregroundStyle(.white)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .glassEffect(.clear, in: .capsule)
                .padding(.bottom, 40)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
    }
}

// MARK: - Document Viewer Placeholder

private struct DocumentViewerPlaceholder: View {
    let attachment: MessageAttachment

    @Environment(\.dismiss) private var dismiss
    @Environment(DependencyContainer.self) private var container

    @State private var isDownloading = false
    @State private var downloadedURL: URL?

    var body: some View {
        VStack(spacing: 24) {
            FileIconView(fileName: attachment.fileName, size: 80)

            VStack(spacing: 8) {
                Text(attachment.fileName)
                    .font(.headline)
                    .foregroundStyle(.white)

                Text(attachment.formattedSize)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            if isDownloading {
                ProgressView()
                    .controlSize(.large)
                    .tint(.white)
            } else if downloadedURL != nil {
                Button("Открыть") {
                    if let url = downloadedURL {
                        NSWorkspace.shared.open(url)
                    }
                }
                .buttonStyle(.glassProminent)
            } else {
                Button("Скачать") {
                    Task { await downloadDocument() }
                }
                .buttonStyle(.glassProminent)
            }
        }
        .padding()
    }

    private func downloadDocument() async {
        isDownloading = true

        do {
            let urlString = try await container.fileService.getDownloadURL(fileID: attachment.fileID)
            guard let url = URL(string: urlString) else { return }

            let (data, _) = try await URLSession.shared.data(from: url)

            // Сохраняем в Downloads
            let downloadsDir = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first!
            let destinationURL = downloadsDir.appendingPathComponent(attachment.fileName)
            try data.write(to: destinationURL)

            downloadedURL = destinationURL
        } catch {
            print("Download error: \(error)")
        }

        isDownloading = false
    }
}

#Preview {
    MediaViewerView(
        attachments: [
            MessageAttachment(id: 1, type: .image, fileID: "1", fileName: "photo1.jpg", fileSize: 1_000_000),
            MessageAttachment(id: 2, type: .image, fileID: "2", fileName: "photo2.jpg", fileSize: 1_000_000),
            MessageAttachment(id: 3, type: .video, fileID: "3", fileName: "video.mp4", fileSize: 10_000_000)
        ],
        initialIndex: 0
    )
    .environment(DependencyContainer())
}
