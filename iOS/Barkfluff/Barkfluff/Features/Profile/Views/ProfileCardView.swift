//
//  ProfileCardView.swift
//  Barkfluff (iOS)
//
//  Карточка пользователя в шапке таба профиля.
//

import SwiftUI
import BFCore

struct ProfileCardView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        HStack(spacing: 12) {
            AvatarView(
                imageURL: container.currentUserAvatarURL,
                initials: container.currentUserInitials,
                size: 56
            )

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
    }
}
