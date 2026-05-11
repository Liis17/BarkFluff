//
//  PosterPreviewCard.swift
//  Barkfluff
//
//  Карточка превью профиля (постер 3:1 + аватар поверх + имя)
//  с кнопкой смены постера. Используется только в экране персонализации.
//

import SwiftUI
import AppKit
import BFCore
import UniformTypeIdentifiers

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

            Button {
                print("[BarkFluff] poster button tapped")
                openPosterPicker()
            } label: {
                Label(
                    viewModel.isUploadingPoster ? "Загрузка…" : "Установить новый постер",
                    systemImage: "photo.on.rectangle.angled"
                )
                .padding(.horizontal, 8)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(viewModel.isUploadingPoster)
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.bottom, Theme.Spacing.md)
        }
        // Без собственного фона/бордера: карточка живёт внутри Form Section
        // и наследует её стиль (rounded card на ультратонком материале).
        // clipShape оставлен, чтобы постер сверху имел те же углы, что и Section.
        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.xl, style: .continuous))
    }

    /// Открыть NSOpenPanel явно, прочитать выбранное изображение, показать кропер
    /// в отдельном NSWindow (через `CropperWindowController`) и при успехе — залить
    /// результат. Полностью обходим SwiftUI `.sheet`/`.fileImporter` — на macOS 26
    /// внутри Form.grouped Section они ломают hit-testing соседних контролов.
    private func openPosterPicker() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        panel.allowedContentTypes = [.image]
        panel.message = "Выберите изображение для постера"
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            Task { @MainActor in
                presentCropper(for: url)
            }
        }
    }

    @MainActor
    private func presentCropper(for url: URL) {
        do {
            let data = try Data(contentsOf: url)
            guard let image = NSImage(data: data) else {
                viewModel.errorMessage = "Не удалось прочитать изображение"
                return
            }
            CropperWindowController.shared.present(
                image: image,
                aspectRatio: 3,
                outputWidth: 1500
            ) { cropped in
                Task { await viewModel.uploadPoster(cropped) }
            }
        } catch {
            viewModel.errorMessage = error.localizedDescription
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
                .strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: 3)
        }
    }

    private var displayName: String {
        guard let user = container.currentUser else { return "Профиль" }
        let full = "\(user.firstName) \(user.lastName)".trimmingCharacters(in: .whitespaces)
        return full.isEmpty ? user.username : full
    }
}
