//
//  MediaUploadProgressView.swift
//  Barkfluff
//
//  Круговой индикатор прогресса загрузки поверх медиа (как в Telegram)
//

import SwiftUI

/// Круговой прогресс загрузки поверх медиа-вложений
struct MediaUploadProgressView: View {
    let progress: Double

    private let circleSize: CGFloat = 48
    private let lineWidth: CGFloat = 3

    var body: some View {
        ZStack {
            // Затемнение поверх медиа
            Color.black.opacity(0.35)

            // Фоновый круг
            Circle()
                .stroke(Color.white.opacity(0.3), lineWidth: lineWidth)
                .frame(width: circleSize, height: circleSize)

            // Прогресс
            Circle()
                .trim(from: 0, to: progress)
                .stroke(Color.white, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .frame(width: circleSize, height: circleSize)
                .rotationEffect(.degrees(-90))
                .animation(.linear(duration: 0.2), value: progress)

            // Процент
            Text("\(Int(progress * 100))%")
                .font(.system(size: 13, weight: .semibold, design: .rounded))
                .foregroundStyle(.white)
        }
    }
}

#Preview {
    VStack(spacing: 20) {
        ZStack {
            Color.blue
            MediaUploadProgressView(progress: 0.0)
        }
        .frame(width: 200, height: 150)
        .clipShape(RoundedRectangle(cornerRadius: 16))

        ZStack {
            Color.blue
            MediaUploadProgressView(progress: 0.45)
        }
        .frame(width: 200, height: 150)
        .clipShape(RoundedRectangle(cornerRadius: 16))

        ZStack {
            Color.blue
            MediaUploadProgressView(progress: 0.85)
        }
        .frame(width: 200, height: 150)
        .clipShape(RoundedRectangle(cornerRadius: 16))
    }
    .padding()
}
