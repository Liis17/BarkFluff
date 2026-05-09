//
//  ProfileHeaderSection.swift
//  Barkfluff (iOS)
//
//  Шапка профиля: постер 3:1 (для DM) + аватар + имя + статус.
//

import SwiftUI
import BFCore

struct ProfileHeaderSection: View {
    @Bindable var viewModel: UserProfilePanelViewModel

    var body: some View {
        VStack(spacing: 12) {
            // Постер 3:1 (только для DM с posterFileID)
            if let posterFileID = viewModel.posterFileID {
                CachedImageView(
                    fileID: posterFileID,
                    type: .image,
                    content: { image in
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    },
                    placeholder: {
                        Color(uiColor: .systemGray5)
                    }
                )
                .aspectRatio(3, contentMode: .fit)
                .frame(maxWidth: .infinity)
                .clipped()
            }

            AvatarView(
                imageURL: viewModel.avatarURL,
                initials: viewModel.initials,
                size: 96,
                isOnline: viewModel.onlineStatus.isOnline,
                showOnlineIndicator: !viewModel.isGroupChat
            )

            VStack(spacing: 4) {
                Text(viewModel.displayName)
                    .font(.title2)
                    .fontWeight(.semibold)
                    .multilineTextAlignment(.center)

                if let username = viewModel.username {
                    Text("@\(username)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }

                if !viewModel.isGroupChat {
                    OnlineStatusText(status: viewModel.onlineStatus)
                        .font(.caption)
                } else {
                    Text("\(viewModel.memberCount) участников")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, viewModel.posterFileID == nil ? 16 : 0)
    }
}
