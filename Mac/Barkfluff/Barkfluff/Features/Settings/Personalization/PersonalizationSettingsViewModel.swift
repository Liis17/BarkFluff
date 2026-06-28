//
//  PersonalizationSettingsViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана персонализации:
//  тянет постер + список фонов с сервера, заливает новые картинки,
//  синхронизирует список фонов через UpdatePersonalization.
//

import SwiftUI
import AppKit
import BFCore
import BFNetworking

@MainActor
@Observable
final class PersonalizationSettingsViewModel {

    // MARK: - Dependencies

    private let userService: UserServiceProtocol
    private let fileService: FileServiceProtocol
    private let container: DependencyContainer

    // MARK: - Server-synced state

    /// fileID текущего постера профиля. Берётся напрямую из `container.currentUser`,
    /// чтобы превью отрисовывалось мгновенно из уже загруженного `currentUser`
    /// и не мигало плейсхолдером во время `getPersonalization`.
    /// Пустая строка — постера нет.
    var posterFileID: String {
        container.currentUser?.profilePosterFileID ?? ""
    }

    /// Полный список фонов чата с сервера (включает выбранный).
    var backgroundFileIDs: [String] = []

    // MARK: - UI state

    var isLoading = false
    var isUploadingPoster = false
    var isUploadingBackground = false
    var errorMessage: String?
    var deleteMode = false


    // MARK: - Init

    init(
        userService: UserServiceProtocol,
        fileService: FileServiceProtocol,
        container: DependencyContainer
    ) {
        self.userService = userService
        self.fileService = fileService
        self.container = container
    }

    // MARK: - Loading

    /// Подтянуть список фонов с сервера.
    /// Постер берём из `container.currentUser` (уже загружен при старте сессии).
    func load() async {
        isLoading = true
        errorMessage = nil
        do {
            let info = try await userService.getPersonalization()
            backgroundFileIDs = info.chatBackgroundFileIDs
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    // MARK: - Poster

    /// Залить уже обрезанный в кропере постер: JPEG 0.85 → upload → setProfilePoster.
    /// Кроп управляется самой View (через `CropperWindowController`), сюда
    /// прилетает уже готовый `NSImage`.
    func uploadPoster(_ image: NSImage) async {
        isUploadingPoster = true
        errorMessage = nil
        defer { isUploadingPoster = false }

        guard let data = image.jpegData(compressionQuality: 0.85) else {
            errorMessage = String(localized: "personalization.error.process_image")
            return
        }

        do {
            let fileID = try await fileService.uploadFile(
                data: data,
                fileName: "poster.jpg",
                fileType: .userProfilePoster
            )
            try await userService.setProfilePoster(fileID: fileID)
            // Write-through: ждём сетевой ответ getCurrentUser и пишем в SQLite,
            // чтобы шапки профиля и `posterFileID` (computed из currentUser)
            // увидели новый постер сразу + он попал в кеш на следующий cold-start.
            await container.revalidateCurrentUser()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Backgrounds

    /// Обработать выбор фона через SwiftUI fileImporter.
    func handleBackgroundSelection(result: Result<[URL], Error>) async {
        switch result {
        case .success(let urls):
            guard let fileURL = urls.first else { return }
            await addBackground(from: fileURL)
        case .failure(let error):
            errorMessage = error.localizedDescription
        }
    }

    private func addBackground(from fileURL: URL) async {
        guard fileURL.startAccessingSecurityScopedResource() else {
            errorMessage = String(localized: "personalization.error.file_access")
            return
        }
        defer { fileURL.stopAccessingSecurityScopedResource() }

        isUploadingBackground = true
        errorMessage = nil
        do {
            let data = try Data(contentsOf: fileURL)
            let fileName = fileURL.lastPathComponent
            // Паритет с Android: фоны загружаются как messageAttachmentImage.
            let fileID = try await fileService.uploadFile(
                data: data,
                fileName: fileName,
                fileType: .messageAttachmentImage
            )
            if !backgroundFileIDs.contains(fileID) {
                backgroundFileIDs.append(fileID)
            }
            try await userService.updatePersonalization(
                posterFileID: posterFileID,
                backgroundFileIDs: backgroundFileIDs
            )
        } catch {
            errorMessage = error.localizedDescription
        }
        isUploadingBackground = false
    }

    /// Удалить фон. Если он был выбран — обнуляет текущий выбор.
    func deleteBackground(fileID: String) async {
        backgroundFileIDs.removeAll { $0 == fileID }
        if container.personalizationSettings.currentBackgroundFileID == fileID {
            container.personalizationSettings.currentBackgroundFileID = ""
        }
        do {
            try await userService.updatePersonalization(
                posterFileID: posterFileID,
                backgroundFileIDs: backgroundFileIDs
            )
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Установить выбранный фон (локально).
    func selectBackground(fileID: String) {
        container.personalizationSettings.currentBackgroundFileID = fileID
    }

    /// Снять выбор фона.
    func clearBackgroundSelection() {
        container.personalizationSettings.currentBackgroundFileID = ""
    }
}
