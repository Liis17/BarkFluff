//
//  PosterPreviewCard.swift
//  Barkfluff
//
//  Карточка превью профиля (постер 3:1 + аватар поверх + имя)
//  с кнопкой смены постера. Используется только в экране персонализации.
//

import SwiftUI
import BFCore
import UniformTypeIdentifiers

struct PosterPreviewCard: View {
    @Environment(DependencyContainer.self) private var container
    @Bindable var viewModel: PersonalizationSettingsViewModel

    @State private var showPosterPicker = false

    private static let avatarSize: CGFloat = 88
    private static let avatarOverlap: CGFloat = avatarSize / 2

    var body: some View {
        VStack(spacing: 0) {
            posterView
                .frame(maxWidth: .infinity)
                .aspectRatio(3.0, contentMode: .fit)
                .clipped()

            avatarView
                .padding(.top, -Self.avatarOverlap)
                .padding(.bottom, Theme.Spacing.sm)

            VStack(spacing: 2) {
                Text(displayName)
                    .font(.title3.bold())
                    .lineLimit(1)
                if let username = container.currentUser?.username, !username.isEmpty {
                    Text("@\(username)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.bottom, Theme.Spacing.md)

            Button {
                showPosterPicker = true
            } label: {
                Label(
                    viewModel.isUploadingPoster ? "Загрузка…" : "Установить новый постер",
                    systemImage: "photo.on.rectangle.angled"
                )
                .frame(maxWidth: .infinity)
            }
            .controlSize(.large)
            .disabled(viewModel.isUploadingPoster)
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.bottom, Theme.Spacing.md)
        }
        .background(.ultraThinMaterial)
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.xl, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.Radius.xl, style: .continuous)
                .strokeBorder(Color.gray.opacity(0.15), lineWidth: 1)
        )
        .fileImporter(
            isPresented: $showPosterPicker,
            allowedContentTypes: [.image],
            allowsMultipleSelection: false
        ) { result in
            Task { await viewModel.handlePosterSelection(result: result) }
        }
    }

    // MARK: - Subviews

    @ViewBuilder
    private var posterView: some View {
        if !viewModel.posterFileID.isEmpty {
            CachedImageView(
                fileID: viewModel.posterFileID,
                type: .image,
                content: { image in
                    image.resizable().aspectRatio(contentMode: .fill)
                },
                placeholder: { posterPlaceholder }
            )
        } else {
            posterPlaceholder
        }
    }

    private var posterPlaceholder: some View {
        LinearGradient(
            colors: [
                Color.accentColor.opacity(0.25),
                Color.accentColor.opacity(0.10)
            ],
            startPoint: .topLeading,
            endPoint: .bottomTrailing
        )
    }

    private var avatarView: some View {
        AvatarView(
            imageURL: container.currentUserAvatarURL,
            initials: container.currentUserInitials,
            size: Self.avatarSize
        )
        .overlay {
            Circle()
                .strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: 3)
        }
    }

    private var displayName: String {
        guard let user = container.currentUser else { return "Профиль" }
        let full = "\(user.firstName) \(user.lastName)".trimmingCharacters(in: .whitespaces)
        return full.isEmpty ? user.username : full
    }
}
