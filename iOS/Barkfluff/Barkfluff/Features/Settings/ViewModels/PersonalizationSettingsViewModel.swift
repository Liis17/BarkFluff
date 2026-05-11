//
//  PersonalizationSettingsViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel экрана персонализации: тянет постер + список фонов с сервера,
//  заливает новые картинки через PhotosPicker, синхронизирует список фонов
//  через UpdatePersonalization. iOS-аналог macOS-версии, но c PhotosPicker
//  вместо fileImporter.
//

import SwiftUI
import PhotosUI
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

    // MARK: - PhotosPicker selection

    /// Выбранный пользователем элемент для постера. didSet → загрузить картинку в pendingPosterImage
    /// и показать кропер 3:1. Сама заливка на сервер — после onCrop.
    var selectedPosterItem: PhotosPickerItem? {
        didSet {
            guard let item = selectedPosterItem else { return }
            Task { await loadPosterForCropping(from: item) }
        }
    }

    /// Выбранный пользователем элемент для нового фона. didSet → addBackground.
    /// Фон не кропается — для него полно-сетка с aspect-fill в гриде.
    var selectedBackgroundItem: PhotosPickerItem? {
        didSet {
            guard let item = selectedBackgroundItem else { return }
            Task { await addBackground(from: item) }
        }
    }

    // MARK: - Poster cropper state

    /// Картинка постера, ждущая обрезки в кропере 3:1.
    var pendingPosterImage: UIImage?

    /// Флаг показа модального экрана кропера постера.
    var showPosterCropper: Bool = false

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

    /// Прочитать выбранную в PhotosPicker картинку и показать кропер 3:1.
    private func loadPosterForCropping(from item: PhotosPickerItem) async {
        do {
            guard let data = try await item.loadTransferable(type: Data.self),
                  let image = UIImage(data: data) else {
                errorMessage = "Не удалось прочитать выбранное изображение"
                selectedPosterItem = nil
                return
            }
            pendingPosterImage = image
            showPosterCropper = true
        } catch {
            errorMessage = error.localizedDescription
            selectedPosterItem = nil
        }
    }

    /// Залить уже обрезанный в кропере постер: JPEG 0.85 → upload → setProfilePoster.
    func uploadPoster(_ image: UIImage) async {
        isUploadingPoster = true
        errorMessage = nil
        defer {
            isUploadingPoster = false
            pendingPosterImage = nil
            selectedPosterItem = nil
        }

        guard let data = image.jpegData(compressionQuality: 0.85) else {
            errorMessage = "Ошибка обработки изображения"
            return
        }

        do {
            let fileID = try await fileService.uploadFile(
                data: data,
                fileName: "poster.jpg",
                fileType: .userProfilePoster
            )
            try await userService.setProfilePoster(fileID: fileID)
            posterFileID = fileID
            // Перечитать текущего пользователя, чтобы шапки профиля
            // (в боковой панели и UserProfilePanelView) подхватили новый постер.
            await container.loadCurrentUser()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Сбросить выбор постера без загрузки (вызывается при отмене в кропере).
    func cancelPosterCropping() {
        pendingPosterImage = nil
        selectedPosterItem = nil
    }

    // MARK: - Backgrounds

    private func addBackground(from item: PhotosPickerItem) async {
        isUploadingBackground = true
        errorMessage = nil
        defer {
            isUploadingBackground = false
            selectedBackgroundItem = nil
        }

        do {
            guard let data = try await item.loadTransferable(type: Data.self) else {
                errorMessage = "Не удалось прочитать выбранное изображение"
                return
            }
            let fileName = "background.\(item.supportedContentTypes.first?.preferredFilenameExtension ?? "jpg")"
            // Паритет с Android/macOS: фоны заливаются как messageAttachmentImage.
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
