//
//  BadgeView.swift
//  Barkfluff (iOS)
//
//  Компонент для отображения бейджей пользователя
//

import SwiftUI
import BFCore

/// Псевдоним для бейджа из BFCore (избегаем конфликта с системным типом iOS 26)
typealias AppBadge = BFCore.UserBadge

/// Представление бейджа пользователя
struct BadgeView: View {
    let badge: AppBadge
    var size: Size = .medium

    enum Size {
        case small
        case medium
        case large

        var iconSize: CGFloat {
            switch self {
            case .small: return 16
            case .medium: return 24
            case .large: return 32
            }
        }

        var textSize: Font {
            switch self {
            case .small: return .caption2
            case .medium: return .caption
            case .large: return .subheadline
            }
        }
    }

    var body: some View {
        HStack(spacing: 6) {
            if let iconURL = badge.iconURL, let url = URL(string: iconURL) {
                AsyncImage(url: url) { image in
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                } placeholder: {
                    Image(systemName: "rosette")
                        .foregroundStyle(Color.accentColor)
                }
                .frame(width: size.iconSize, height: size.iconSize)
            } else {
                Image(systemName: "rosette")
                    .font(.system(size: size.iconSize * 0.8))
                    .foregroundStyle(Color.accentColor)
            }

            Text(badge.name)
                .font(size.textSize)
                .lineLimit(1)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(Color.accentColor.opacity(0.1))
        .clipShape(Capsule())
    }
}

// MARK: - Badge Icon Only

/// Только иконка бейджа (без текста)
struct BadgeIconView: View {
    let badge: AppBadge
    var size: CGFloat = 24

    var body: some View {
        Group {
            if let iconURL = badge.iconURL, let url = URL(string: iconURL) {
                AsyncImage(url: url) { image in
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                } placeholder: {
                    Image(systemName: "rosette")
                        .foregroundStyle(Color.accentColor)
                }
            } else {
                Image(systemName: "rosette")
                    .font(.system(size: size * 0.7))
                    .foregroundStyle(Color.accentColor)
            }
        }
        .frame(width: size, height: size)
        .accessibilityLabel(badge.name)
    }
}

// MARK: - Badges Row

/// Ряд бейджей с горизонтальной прокруткой
struct BadgesRowView: View {
    let badges: [AppBadge]
    var showNames: Bool = false

    var body: some View {
        if badges.isEmpty {
            EmptyView()
        } else if showNames {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(badges) { badge in
                        BadgeView(badge: badge, size: .small)
                    }
                }
            }
        } else {
            HStack(spacing: 4) {
                ForEach(badges.prefix(5)) { badge in
                    BadgeIconView(badge: badge, size: 20)
                }

                if badges.count > 5 {
                    Text("+\(badges.count - 5)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Color.secondary.opacity(0.1))
                        .clipShape(Capsule())
                }
            }
        }
    }
}
