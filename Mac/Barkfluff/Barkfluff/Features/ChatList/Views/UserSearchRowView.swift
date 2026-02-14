//
//  UserSearchRowView.swift
//  Barkfluff
//
//  Строка результата поиска пользователя
//

import SwiftUI
import BFCore

struct UserSearchRowView: View {
    let user: User

    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            AvatarView(
                imageURL: user.profilePicturePreviewURL,
                initials: user.initials,
                size: 36
            )

            VStack(alignment: .leading, spacing: 2) {
                Text(user.displayName)
                    .font(.headline)
                    .lineLimit(1)

                Text("@\(user.username)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .padding(.vertical, 2)
    }
}
