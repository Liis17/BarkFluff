//
//  VideoPlayerView.swift
//  Barkfluff
//
//  Полноэкранный просмотр видео с AVKit
//

import SwiftUI
import AVKit
import BFCore

struct VideoPlayerView: View {
    let attachment: MessageAttachment

    @Environment(\.dismiss) private var dismiss
    @Environment(DependencyContainer.self) private var container

    @State private var player: AVPlayer?
    @State private var isLoading = true
    @State private var error: Error?

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            if let player = player {
                VideoPlayer(player: player)
                    .ignoresSafeArea()
            } else if isLoading {
                ProgressView()
                    .controlSize(.large)
                    .tint(.white)
            } else if error != nil {
                errorView
            }
        }
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Готово") {
                    player?.pause()
                    dismiss()
                }
                .buttonStyle(.glass)
            }
        }
        .task {
            await loadVideo()
        }
        .onDisappear {
            player?.pause()
        }
    }

    // MARK: - Private Views

    private var errorView: some View {
        VStack(spacing: 16) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)

            Text("Не удалось загрузить видео")
                .font(.headline)
                .foregroundStyle(.secondary)

            Button("Закрыть") {
                dismiss()
            }
            .buttonStyle(.glassProminent)
        }
    }

    // MARK: - Private Methods

    private func loadVideo() async {
        do {
            // Сначала пробуем previewURL, затем получаем через fileService
            var videoURL: URL?

            if let previewURL = attachment.previewURL, !previewURL.isEmpty {
                videoURL = URL(string: previewURL)
            } else {
                let urlString = try await container.fileService.getDownloadURL(fileID: attachment.fileID)
                videoURL = URL(string: urlString)
            }

            if let url = videoURL {
                player = AVPlayer(url: url)
                player?.play()
            } else {
                error = NSError(domain: "VideoPlayerView", code: -1)
            }
        } catch {
            self.error = error
        }

        isLoading = false
    }
}

#Preview {
    VideoPlayerView(
        attachment: MessageAttachment(
            id: 1,
            type: .video,
            fileID: "test",
            fileName: "video.mp4",
            fileSize: 15_000_000
        )
    )
    .environment(DependencyContainer())
}
