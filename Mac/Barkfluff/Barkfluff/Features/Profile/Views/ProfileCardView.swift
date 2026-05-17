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
        HStack(spacing: 12) {
            // Аватар 48pt (переиспользуем AvatarView)
            AvatarView(
                imageURL: container.currentUserAvatarURL,
                initials: container.currentUserInitials,
                size: 48
            )

            // Имя и юзернейм
            VStack(alignment: .leading, spacing: 2) {
                Text(container.currentUser?.displayName ?? String(localized: "profile.card.fallback_name"))
                    .font(.headline)
                    .lineLimit(1)

                if let username = container.currentUser?.username {
                    Text("@\(username)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            Spacer()
        }
        .padding(.vertical, 4)
        // Карточка без дополнительного фона для растворения в сайдбаре
    }
}

#Preview {
    ProfileCardView()
        .environment(DependencyContainer())
        .padding()
}
