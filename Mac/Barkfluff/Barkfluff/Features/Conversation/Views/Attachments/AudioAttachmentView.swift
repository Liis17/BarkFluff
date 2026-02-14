//
//  AudioAttachmentView.swift
//  Barkfluff
//
//  Отображение аудио-вложения
//

import SwiftUI
import BFCore
import AVFoundation

struct AudioAttachmentView: View {
    let attachment: MessageAttachment
    let onTap: () -> Void

    @State private var isPlaying = false
    @State private var progress: Double = 0

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 12) {
                // Кнопка Play/Pause
                playButton

                // Информация и прогресс
                VStack(alignment: .leading, spacing: 4) {
                    Text(attachment.fileName)
                        .font(.subheadline)
                        .lineLimit(1)
                        .foregroundStyle(.primary)

                    // Прогресс бар
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            Capsule()
                                .fill(.secondary.opacity(0.3))

                            Capsule()
                                .fill(Color.accentColor)
                                .frame(width: geo.size.width * progress)
                        }
                    }
                    .frame(height: 4)

                    // Длительность (placeholder)
                    Text("--:--")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Spacer()
            }
            .padding(12)
            .background(.fill.tertiary)
            .clipShape(RoundedRectangle(cornerRadius: 12))
        }
        .buttonStyle(.plain)
    }

    private var playButton: some View {
        Button {
            isPlaying.toggle()
        } label: {
            Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                .font(.title2)
                .foregroundStyle(.white)
                .frame(width: 40, height: 40)
                .background(Circle().fill(Color.accentColor))
        }
        .buttonStyle(.plain)
    }
}

#Preview {
    VStack(spacing: 12) {
        AudioAttachmentView(
            attachment: MessageAttachment(
                id: 1,
                type: .audio,
                fileID: "audio1",
                fileName: "voice_message.mp3",
                fileSize: 250_000
            ),
            onTap: {}
        )

        AudioAttachmentView(
            attachment: MessageAttachment(
                id: 2,
                type: .audio,
                fileID: "audio2",
                fileName: "podcast_episode.mp3",
                fileSize: 45_000_000
            ),
            onTap: {}
        )
    }
    .padding()
    .frame(width: 300)
}
