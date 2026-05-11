//
//  SplashView.swift
//  Barkfluff
//
//  Сплеш-скрин при старте: логотип + название + индикатор.
//  Показывается поверх RootView, пока приложение инициализируется
//  и пока в .main не загружена первая партия чатов.
//

import SwiftUI

struct SplashView: View {
    var body: some View {
        ZStack {
            Theme.Colors.background
                .ignoresSafeArea()

            VStack(spacing: Theme.Spacing.lg) {
                Image("BrandLogo")
                    .resizable()
                    .interpolation(.high)
                    .scaledToFit()
                    .frame(width: 128, height: 128)
                    .clipShape(RoundedRectangle(cornerRadius: 28, style: .continuous))
                    .shadow(color: .black.opacity(0.15), radius: 12, x: 0, y: 6)

                Text("BarkFluff")
                    .font(.largeTitle.weight(.semibold))
                    .foregroundStyle(.primary)

                ProgressView()
                    .progressViewStyle(.circular)
                    .controlSize(.regular)
                    .padding(.top, Theme.Spacing.md)
            }
        }
    }
}

#Preview {
    SplashView()
}
