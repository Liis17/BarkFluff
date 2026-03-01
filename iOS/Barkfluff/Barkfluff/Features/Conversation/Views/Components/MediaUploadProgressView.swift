//
//  MediaUploadProgressView.swift
//  Barkfluff
//
//  Индикатор прогресса загрузки медиа (iOS версия)
//

import SwiftUI

/// Индикатор прогресса загрузки медиа (Telegram-стиль)
struct MediaUploadProgressView: View {
    let progress: Double

    var body: some View {
        ZStack {
            // Полупрозрачный overlay
            Color.black.opacity(0.3)

            // Круговой прогресс
            ZStack {
                Circle()
                    .stroke(Color.white.opacity(0.3), lineWidth: 3)
                    .frame(width: 44, height: 44)

                Circle()
                    .trim(from: 0, to: progress)
                    .stroke(Color.white, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                    .frame(width: 44, height: 44)
                    .rotationEffect(.degrees(-90))

                // Процент
                Text("\(Int(progress * 100))%")
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundStyle(.white)
            }
        }
    }
}

#Preview {
    ZStack {
        Color.gray
            .frame(width: 200, height: 200)

        MediaUploadProgressView(progress: 0.65)
    }
}
