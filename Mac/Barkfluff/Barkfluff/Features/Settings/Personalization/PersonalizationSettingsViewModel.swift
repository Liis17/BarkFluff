//
//  PersonalizationSettingsViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана персонализации:
//  тянет постер + список фонов с сервера, заливает новые картинки,
//  синхронизирует список фонов через UpdatePersonalization.
//

import SwiftUI
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

    /// fileID текущего постера профиля. Пустая строка — нет постера.
    var posterFileID: String = ""

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

    /// Подтянуть постер и список фонов с сервера.
    func load() async {
        isLoading = true
        errorMessage = nil
        do {
            let info = try await userService.getPersonalization()
            posterFileID = info.profilePosterFileID
            backgroundFileIDs = info.chatBackgroundFileIDs
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    // MARK: - Poster

    /// Обработать выбор постера через SwiftUI fileImporter.
    func handlePosterSelection(result: Result<[URL], Error>) async {
        switch result {
        case .success(let urls):
            guard let fileURL = urls.first else { return }
            await uploadPoster(from: fileURL)
        case .failure(let error):
            errorMessage = error.localizedDescription
        }
    }

    private func uploadPoster(from fileURL: URL) async {
        guard fileURL.startAccessingSecurityScopedResource() else {
            errorMessage = "Нет доступа к файлу"
            return
        }
        defer { fileURL.stopAccessingSecurityScopedResource() }

        isUploadingPoster = true
        errorMessage = nil
        do {
            let data = try Data(contentsOf: fileURL)
            let fileName = fileURL.lastPathComponent
            let fileID = try await fileService.uploadFile(
                data: data,
                fileName: fileName,
                fileType: .userProfilePoster
            )
            try await userService.setProfilePoster(fileID: fileID)
            posterFileID = fileID
            // Перечитать текущего пользователя, чтобы шапки профиля
            // (свой и в ProfileSidebarView) увидели новый постер сразу.
            await container.loadCurrentUser()
        } catch {
            errorMessage = error.localizedDescription
        }
        isUploadingPoster = false
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
            errorMessage = "Нет доступа к файлу"
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
