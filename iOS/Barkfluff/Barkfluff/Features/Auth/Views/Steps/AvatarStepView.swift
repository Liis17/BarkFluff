//
//  AvatarStepView.swift
//  Barkfluff
//
//  Шаг 6: Загрузка аватара (iOS)
//

import SwiftUI
import PhotosUI
import BFCore

struct AvatarStepView: View {
    @Bindable var data: RegistrationData
    let fileService: FileServiceProtocol
    let userService: UserServiceProtocol

    @State private var isUploading = false
    @State private var uploadError: String?
    @State private var selectedItem: PhotosPickerItem?

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
                                Text("Добавить фото")
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
                    Text(data.avatarImage == nil ? "Выбрать фото" : "Изменить фото")
                }
                .buttonStyle(.bordered)
                .disabled(isUploading)

                if data.avatarImage != nil {
                    Button("Удалить фото", role: .destructive) {
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
            Text("Не обязательно — можно добавить позже в настройках")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .onChange(of: selectedItem) { _, newItem in
            Task { await handlePhotoSelection(newItem) }
        }
    }

    // MARK: - Actions

    private func handlePhotoSelection(_ item: PhotosPickerItem?) async {
        guard let item = item else { return }

        isUploading = true
        uploadError = nil
        defer { isUploading = false }

        do {
            guard let imageData = try await item.loadTransferable(type: Data.self) else {
                uploadError = "Не удалось загрузить изображение"
                return
            }

            guard let uiImage = UIImage(data: imageData),
                  let compressedData = uiImage.jpegData(compressionQuality: 0.7) else {
                uploadError = "Ошибка обработки изображения"
                return
            }

            let fileID = try await fileService.uploadFile(
                data: compressedData,
                fileName: "avatar.jpg",
                fileType: .userAvatar
            )

            try await userService.setProfilePicture(fileID: fileID)

            await MainActor.run {
                data.avatarData = compressedData
                data.avatarFileID = fileID
                data.avatarImage = Image(uiImage: uiImage)
            }

        } catch {
            uploadError = "Не удалось загрузить фото"
        }
    }

    private func removeAvatar() {
        data.avatarImage = nil
        data.avatarData = nil
        data.avatarFileID = nil
        selectedItem = nil
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
