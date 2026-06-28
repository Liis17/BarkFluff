//
//  ProfileHeaderSection.swift
//  Barkfluff (iOS)
//
//  Шапка профиля: постер 3:1 (для DM) + аватар, наполовину свисающий вниз с постера,
//  + имя + @username (тап копирует) + статус.
//

import SwiftUI
import UIKit
import BFCore

struct ProfileHeaderSection: View {
    @Bindable var viewModel: UserProfilePanelViewModel

    private static let avatarSize: CGFloat = 96
    private static let avatarOverlap: CGFloat = avatarSize / 2

    private var posterIsShown: Bool {
        !viewModel.isGroupChat && viewModel.posterFileID != nil
    }

    var body: some View {
        VStack(spacing: 0) {
            if posterIsShown {
                posterView
            }

            AvatarView(
                imageURL: viewModel.avatarURL,
                initials: viewModel.initials,
                size: Self.avatarSize,
                isOnline: viewModel.onlineStatus.isOnline,
                showOnlineIndicator: !viewModel.isGroupChat
            )
            .overlay {
                if !viewModel.isGroupChat {
                    Circle()
                        .strokeBorder(Color(uiColor: .systemBackground), lineWidth: 3)
                }
            }
            .padding(.top, posterIsShown ? -Self.avatarOverlap : 16)
            .padding(.bottom, 8)

            VStack(spacing: 4) {
                Text(viewModel.displayName)
                    .font(.title2)
                    .fontWeight(.semibold)
                    .multilineTextAlignment(.center)

                if let username = viewModel.username {
                    Button {
                        copyToPasteboard("@\(username)")
                    } label: {
                        Text("@\(username)")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                    .buttonStyle(.plain)
                    .contextMenu {
                        Button {
                            copyToPasteboard("@\(username)")
                        } label: {
                            Label("user_profile.copy", systemImage: "doc.on.doc")
                        }
                    }
                }

                if !viewModel.isGroupChat {
                    OnlineStatusText(status: viewModel.onlineStatus)
                        .font(.caption)
                } else {
                    Text("user_profile.members_count \(viewModel.memberCount)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 16)
        }
    }

    @ViewBuilder
    private var posterView: some View {
        GeometryReader { geo in
            Group {
                if let fileID = viewModel.posterFileID {
                    CachedImageView(
                        fileID: fileID,
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
                } else {
                    Color(uiColor: .systemGray5)
                }
            }
            .frame(width: geo.size.width, height: geo.size.width / 3)
            .clipped()
        }
        .aspectRatio(3.0, contentMode: .fit)
        .frame(maxWidth: .infinity)
    }

    private func copyToPasteboard(_ value: String) {
        UIPasteboard.general.string = value
        UINotificationFeedbackGenerator().notificationOccurred(.success)
    }
}
