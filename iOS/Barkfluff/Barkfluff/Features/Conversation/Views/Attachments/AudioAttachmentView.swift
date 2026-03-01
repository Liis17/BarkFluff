//
//  AudioAttachmentView.swift
//  Barkfluff
//
//  Отображение аудио во вложении (iOS версия)
//

import SwiftUI
import BFCore

/// Отображение аудио во вложении
struct AudioAttachmentView: View {
    let attachment: MessageAttachment
    var uploadProgress: Double?
    var onTap: (() -> Void)?

    @State private var isPlaying = false
    @State private var playbackProgress: Double = 0

    var body: some View {
        Button {
            onTap?()
        } label: {
            HStack(spacing: Theme.Spacing.sm) {
                // Кнопка play/pause
                ZStack {
                    Circle()
                        .fill(Color.accentColor)
                        .frame(width: 40, height: 40)

                    if let progress = uploadProgress, progress < 1.0 {
                        CircularProgressView(progress: progress)
                            .frame(width: 40, height: 40)
                    } else {
                        Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                            .font(.system(size: 16))
                            .foregroundStyle(.white)
                            .offset(x: isPlaying ? 0 : 2)
                    }
                }

                // Waveform и информация
                VStack(alignment: .leading, spacing: 4) {
                    // Имя файла или "Голосовое сообщение"
                    Text(attachment.type == .audio && attachment.fileName.hasPrefix("voice_")
                         ? "Голосовое сообщение"
                         : attachment.fileName)
                        .font(.subheadline)
                        .fontWeight(.medium)
                        .foregroundStyle(.primary)
                        .lineLimit(1)

                    // Прогресс бар (waveform placeholder)
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            // Background waveform
                            HStack(spacing: 1) {
                                ForEach(0..<30, id: \.self) { i in
                                    RoundedRectangle(cornerRadius: 1)
                                        .fill(Color(uiColor: .systemGray4))
                                        .frame(width: 2, height: CGFloat.random(in: 4...16))
                                }
                            }

                            // Progress
                            RoundedRectangle(cornerRadius: 1)
                                .fill(Color.accentColor)
                                .frame(width: geo.size.width * playbackProgress, height: 2)
                                .frame(height: 16, alignment: .leading)
                        }
                    }
                    .frame(height: 16)

                    // Размер файла
                    Text(attachment.formattedSize)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Spacer()
            }
            .padding(Theme.Spacing.sm)
            .background(Color(uiColor: .tertiarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
        }
        .buttonStyle(.plain)
    }
}

#Preview {
    VStack(spacing: 16) {
        AudioAttachmentView(
            attachment: MessageAttachment(
                id: 1,
                type: .audio,
                fileID: "1",
                fileName: "voice_001.m4a",
                fileSize: 50000,
                previewURL: nil
            )
        )

        AudioAttachmentView(
            attachment: MessageAttachment(
                id: 2,
                type: .audio,
                fileID: "2",
                fileName: "music.mp3",
                fileSize: 3000000,
                previewURL: nil
            ),
            uploadProgress: 0.3
        )
    }
    .padding()
}
