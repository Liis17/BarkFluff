//
//  AvatarStepView.swift
//  Barkfluff
//
//  Шаг 6: Загрузка аватара (iOS).
//  После выбора картинки открывается квадратный кропер; на сервер уходит
//  только обрезанная картинка.
//

import SwiftUI
import PhotosUI
import BFCore

struct AvatarStepView: View {
    @Bindable var data: RegistrationData
    let fileService: FileServiceProtocol
    let userService: UserServiceProtocol

    @State private var isUploading = false
    @State private var uploadError: LocalizedStringResource?
    @State private var selectedItem: PhotosPickerItem?
    @State private var pendingImage: UIImage?
    @State private var showCropper: Bool = false

    var body: some View {
        VStack(spacing: 20) {
            // Аватар
            ZStack {
                if let image = data.avatarImage {
                    image
                        .resizable()
                        .scaledToFill()
                        .frame(width: 120, height: 120)
                        .clipShape(Circle())
                } else {
                    Circle()
                        .fill(Color(uiColor: .tertiarySystemFill))
                        .frame(width: 120, height: 120)
                        .overlay(
                            VStack(spacing: 8) {
                                Image(systemName: "person.fill")
                                    .font(.system(size: 40))
                                    .foregroundStyle(.tertiary)
                                Text("auth.register.step.avatar.add")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                        )
                }

                if isUploading {
                    Circle()
                        .fill(.black.opacity(0.5))
                        .frame(width: 120, height: 120)

                    ProgressView()
                        .controlSize(.large)
                        .tint(.white)
                }
            }

            // Кнопки
            VStack(spacing: 8) {
                PhotosPicker(
                    selection: $selectedItem,
                    matching: .images,
                    photoLibrary: .shared()
                ) {
                    Text(data.avatarImage == nil ? "auth.register.step.avatar.choose" : "auth.register.step.avatar.change")
                }
                .buttonStyle(.bordered)
                .disabled(isUploading)

                if data.avatarImage != nil {
                    Button("auth.register.step.avatar.remove", role: .destructive) {
                        removeAvatar()
                    }
                    .buttonStyle(.plain)
                    .font(.subheadline)
                }
            }

            // Ошибка
            if let error = uploadError {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            }

            // Подсказка
            Text("auth.register.step.avatar.hint")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .onChange(of: selectedItem) { _, newItem in
            Task { await handlePhotoSelection(newItem) }
        }
        .fullScreenCover(isPresented: $showCropper) {
            if let pendingImage {
                ImageCropperView(
                    image: pendingImage,
                    aspectRatio: 1,
                    outputWidth: 1024,
                    onCancel: {
                        showCropper = false
                        self.pendingImage = nil
                        selectedItem = nil
                    },
                    onCrop: { cropped in
                        showCropper = false
                        Task { await uploadCropped(cropped) }
                    }
                )
            }
        }
    }

    // MARK: - Actions

    private func handlePhotoSelection(_ item: PhotosPickerItem?) async {
        guard let item = item else { return }

        uploadError = nil

        do {
            guard let imageData = try await item.loadTransferable(type: Data.self),
                  let uiImage = UIImage(data: imageData) else {
                uploadError = LocalizedStringResource("auth.register.step.avatar.error.load")
                return
            }

            await MainActor.run {
                pendingImage = uiImage
                showCropper = true
            }
        } catch {
            uploadError = LocalizedStringResource("auth.register.step.avatar.error.load_photo")
        }
    }

    private func uploadCropped(_ image: UIImage) async {
        isUploading = true
        uploadError = nil
        defer {
            isUploading = false
            pendingImage = nil
            selectedItem = nil
        }

        guard let compressedData = image.jpegData(compressionQuality: 0.85) else {
            uploadError = LocalizedStringResource("auth.register.step.avatar.error.process")
            return
        }

        do {
            let fileID = try await fileService.uploadFile(
                data: compressedData,
                fileName: "avatar.jpg",
                fileType: .userAvatar
            )

            try await userService.setProfilePicture(fileID: fileID)

            await MainActor.run {
                data.avatarData = compressedData
                data.avatarFileID = fileID
                data.avatarImage = Image(uiImage: image)
            }
        } catch {
            uploadError = LocalizedStringResource("auth.register.step.avatar.error.upload")
        }
    }

    private func removeAvatar() {
        data.avatarImage = nil
        data.avatarData = nil
        data.avatarFileID = nil
        selectedItem = nil
        pendingImage = nil
    }
}

#Preview {
    AvatarStepView(
        data: RegistrationData(),
        fileService: DependencyContainer().fileService,
        userService: DependencyContainer().userService
    )
    .padding()
}
