//
//  BadgesView.swift
//  Barkfluff
//
//  Отображение баджей пользователя
//

import SwiftUI
import BFCore

struct BadgesView: View {
    let badges: [AppBadge]

    var body: some View {
        if badges.isEmpty {
            ContentUnavailableView(
                "Нет баджей",
                systemImage: "star",
                description: Text("У пользователя пока нет баджей")
            )
        } else {
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 120))], spacing: 16) {
                    ForEach(badges) { badge in
                        BadgeCardView(badge: badge)
                    }
                }
                .padding()
            }
        }
    }
}

private struct BadgeCardView: View {
    let badge: AppBadge

    var body: some View {
        VStack(spacing: 8) {
            if let iconURL = badge.iconURL, let url = URL(string: iconURL) {
                AsyncImage(url: url) { image in
                    image
                        .resizable()
                        .scaledToFit()
                } placeholder: {
                    Image(systemName: "star.fill")
                        .font(.title)
                        .foregroundStyle(.yellow)
                }
                .frame(width: 48, height: 48)
            } else {
                Image(systemName: "star.fill")
                    .font(.title)
                    .foregroundStyle(.yellow)
                    .frame(width: 48, height: 48)
            }

            Text(badge.name)
                .font(.caption)
                .fontWeight(.medium)
                .lineLimit(1)

            Text(badge.description)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .lineLimit(2)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding()
        .glassCard()
    }
}

#Preview {
    BadgesView(badges: [
        AppBadge(id: "1", name: "OG", description: "Один из первых пользователей"),
        AppBadge(id: "2", name: "Разработчик", description: "Участник команды разработки")
    ])
    .frame(width: 400, height: 300)
}
