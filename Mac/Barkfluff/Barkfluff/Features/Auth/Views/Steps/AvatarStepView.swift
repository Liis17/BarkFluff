//
//  AvatarStepView.swift
//  Barkfluff
//
//  Шаг 6: Загрузка аватара (опционально).
//  После выбора файла открывается квадратный кропер; на сервер уходит
//  только обрезанная картинка.
//

import SwiftUI
import UniformTypeIdentifiers
import AppKit
import BFCore

/// Шаг регистрации: загрузка аватара
struct AvatarStepView: View {
    @Bindable var data: RegistrationData
    let fileService: FileServiceProtocol
    let userService: UserServiceProtocol

    @State private var isUploading = false
    @State private var uploadError: String?
    @State private var showFilePicker = false
    @State private var pendingImage: NSImage?
    @State private var showCropper: Bool = false

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Текущий аватар или плейсхолдер
            ZStack {
                if let image = data.avatarImage {
                    image
                        .resizable()
                        .scaledToFill()
                        .frame(width: 120, height: 120)
                        .clipShape(Circle())
                } else {
                    Circle()
                        .fill(Color.gray.opacity(0.2))
                        .frame(width: 120, height: 120)
                        .overlay(
                            Image(systemName: "person.fill")
                                .font(.system(size: 50))
                                .foregroundStyle(.gray)
                        )
                }

                if isUploading {
                    Circle()
                        .fill(Color.black.opacity(0.5))
                        .frame(width: 120, height: 120)
                    ProgressView()
                        .tint(.white)
                }
            }

            // Кнопки
            VStack(spacing: Theme.Spacing.sm) {
                Button(action: { showFilePicker = true }) {
                    Label(
                        data.avatarImage == nil ? "Выбрать фото" : "Изменить фото",
                        systemImage: "photo"
                    )
                }
                .buttonStyle(.bordered)
                .disabled(isUploading)

                if data.avatarImage != nil {
                    Button("Удалить", role: .destructive) {
                        data.avatarImage = nil
                        data.avatarData = nil
                        data.avatarFileID = nil
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(.red)
                }
            }

            if let error = uploadError {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            }

            Text("Вы можете пропустить этот шаг и добавить аватар позже")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .fileImporter(
            isPresented: $showFilePicker,
            allowedContentTypes: [UTType.image],
            allowsMultipleSelection: false
        ) { result in
            Task { await handleFileSelection(result) }
        }
        .sheet(isPresented: $showCropper) {
            if let pendingImage {
                ImageCropperView(
                    image: pendingImage,
                    aspectRatio: 1,
                    outputWidth: 1024,
                    onCancel: {
                        showCropper = false
                        self.pendingImage = nil
                    },
                    onCrop: { cropped in
                        showCropper = false
                        Task { await uploadCropped(cropped) }
                    }
                )
            }
        }
    }

    private func handleFileSelection(_ result: Result<[URL], Error>) async {
        switch result {
        case .success(let urls):
            guard let url = urls.first else { return }
            await loadImageForCropping(from: url)
        case .failure(let error):
            uploadError = error.localizedDescription
        }
    }

    private func loadImageForCropping(from url: URL) async {
        uploadError = nil
        guard url.startAccessingSecurityScopedResource() else {
            uploadError = "Нет доступа к файлу"
            return
        }
        defer { url.stopAccessingSecurityScopedResource() }

        do {
            let imageData = try Data(contentsOf: url)
            guard let nsImage = NSImage(data: imageData) else {
                uploadError = "Не удалось прочитать изображение"
                return
            }
            await MainActor.run {
                pendingImage = nsImage
                showCropper = true
            }
        } catch {
            uploadError = "Не удалось прочитать файл: \(error.localizedDescription)"
        }
    }

    private func uploadCropped(_ image: NSImage) async {
        isUploading = true
        uploadError = nil
        defer {
            isUploading = false
            pendingImage = nil
        }

        guard let compressedData = image.jpegData(compressionQuality: 0.85) else {
            uploadError = "Ошибка обработки изображения"
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
                data.avatarImage = Image(nsImage: image)
            }
        } catch {
            uploadError = "Не удалось загрузить фото: \(error.localizedDescription)"
        }
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
