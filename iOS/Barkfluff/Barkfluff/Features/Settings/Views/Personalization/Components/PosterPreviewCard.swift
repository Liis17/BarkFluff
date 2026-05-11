//
//  PosterPreviewCard.swift
//  Barkfluff (iOS)
//
//  Карточка превью профиля (постер 3:1 + аватар поверх + имя)
//  с кнопкой смены постера через PhotosPicker.
//

import SwiftUI
import PhotosUI
import BFCore

struct PosterPreviewCard: View {
    @Environment(DependencyContainer.self) private var container
    @Bindable var viewModel: PersonalizationSettingsViewModel

    private static let avatarSize: CGFloat = 88
    private static let avatarOverlap: CGFloat = avatarSize / 2

    var body: some View {
        VStack(spacing: 0) {
            // GeometryReader снаружи зажат через aspectRatio(.fit) — получает
            // фрейм 3:1 от ширины родителя. Внутри постер рисуется на этой
            // конкретной геометрии (явные width/height), без зависимости от
            // intrinsic size картинки, иначе .fill раздул бы view до исходного
            // размера декодированного изображения.
            GeometryReader { geo in
                posterView
                    .frame(width: geo.size.width, height: geo.size.height)
                    .clipped()
            }
            .aspectRatio(3.0, contentMode: .fit)
            .frame(maxWidth: .infinity)

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

            let posterButtonTitle: String = viewModel.isUploadingPoster
                ? "Загрузка…"
                : "Установить новый постер"
            let posterButtonDisabled = viewModel.isUploadingPoster

            PhotosPicker(
                selection: $viewModel.selectedPosterItem,
                matching: .images,
                photoLibrary: .shared()
            ) {
                Label(posterButtonTitle, systemImage: "photo.on.rectangle.angled")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .disabled(posterButtonDisabled)
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.bottom, Theme.Spacing.md)
        }
        // clipShape оставлен, чтобы постер сверху имел те же углы, что и Section.
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.xl, style: .continuous))
        .fullScreenCover(isPresented: $viewModel.showPosterCropper) {
            if let pendingImage = viewModel.pendingPosterImage {
                ImageCropperView(
                    image: pendingImage,
                    aspectRatio: 3,
                    outputWidth: 1500,
                    onCancel: {
                        viewModel.showPosterCropper = false
                        viewModel.cancelPosterCropping()
                    },
                    onCrop: { cropped in
                        viewModel.showPosterCropper = false
                        Task { await viewModel.uploadPoster(cropped) }
                    }
                )
            }
        }
    }

    // MARK: - Subviews

    @ViewBuilder
    private var posterView: some View {
        if !viewModel.posterFileID.isEmpty {
            CachedImageView(
                fileID: viewModel.posterFileID,
                type: .poster,
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
                .strokeBorder(Color(uiColor: .systemBackground), lineWidth: 3)
        }
    }

    private var displayName: String {
        guard let user = container.currentUser else { return "Профиль" }
        let full = "\(user.firstName) \(user.lastName)".trimmingCharacters(in: .whitespaces)
        return full.isEmpty ? user.username : full
    }
}
