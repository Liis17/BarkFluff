//
//  ProfileCardView.swift
//  Barkfluff
//
//  Карточка пользователя в сайдбаре профиля
//

import SwiftUI
import BFCore

struct ProfileCardView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        Group {
            if let user = container.currentUser {
                content(displayName: user.displayName, username: user.username)
            } else {
                // Skeleton: cold-start без cached currentUser. После Часть A
                // обычно сразу есть кеш, так что это видно только на первой
                // установке / после logout.
                content(
                    displayName: String(localized: "profile.card.fallback_name"),
                    username: String(localized: "profile.card.fallback_username")
                )
                .redacted(reason: .placeholder)
            }
        }
        .padding(.vertical, 4)
        // Карточка без дополнительного фона для растворения в сайдбаре
    }

    @ViewBuilder
    private func content(displayName: String, username: String?) -> some View {
        HStack(spacing: 12) {
            // Аватар 48pt (переиспользуем AvatarView)
            AvatarView(
                imageURL: container.currentUserAvatarURL,
                initials: container.currentUserInitials,
                size: 48
            )

            // Имя и юзернейм
            VStack(alignment: .leading, spacing: 2) {
                Text(displayName)
                    .font(.headline)
                    .lineLimit(1)

                if let username {
                    Text("@\(username)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            Spacer()
        }
    }
}

#Preview {
    ProfileCardView()
        .environment(DependencyContainer())
        .padding()
}
