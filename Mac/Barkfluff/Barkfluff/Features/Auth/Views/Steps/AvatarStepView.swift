//
//  AvatarStepView.swift
//  Barkfluff
//
//  Шаг 6: Загрузка аватара (опционально)
//

import SwiftUI
import UniformTypeIdentifiers
import BFCore

/// Шаг регистрации: загрузка аватара
struct AvatarStepView: View {
    @Bindable var data: RegistrationData
    let fileService: FileServiceProtocol
    let userService: UserServiceProtocol

    @State private var isUploading = false
    @State private var uploadError: String?
    @State private var showFilePicker = false

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

                // Индикатор загрузки
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

            // Ошибка
            if let error = uploadError {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            }

            // Подсказка
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
    }

    private func handleFileSelection(_ result: Result<[URL], Error>) async {
        switch result {
        case .success(let urls):
            guard let url = urls.first else { return }
            await uploadAvatar(from: url)

        case .failure(let error):
            uploadError = error.localizedDescription
        }
    }

    private func uploadAvatar(from url: URL) async {
        isUploading = true
        uploadError = nil
        defer { isUploading = false }

        do {
            // Читаем данные
            guard url.startAccessingSecurityScopedResource() else {
                throw NSError(domain: "AvatarStepView", code: 1, userInfo: [NSLocalizedDescriptionKey: "Нет доступа к файлу"])
            }
            defer { url.stopAccessingSecurityScopedResource() }

            let imageData = try Data(contentsOf: url)

            // Загружаем файл
            let fileID = try await fileService.uploadFile(
                data: imageData,
                fileName: url.lastPathComponent,
                fileType: .userAvatar
            )

            // Устанавливаем как аватар
            try await userService.setProfilePicture(fileID: fileID)

            // Обновляем UI
            await MainActor.run {
                data.avatarData = imageData
                data.avatarFileID = fileID
                data.avatarImage = Image(nsImage: NSImage(data: imageData) ?? NSImage())
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
