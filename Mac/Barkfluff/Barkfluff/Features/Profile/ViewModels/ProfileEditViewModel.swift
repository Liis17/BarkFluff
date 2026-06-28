//
//  ProfileEditViewModel.swift
//  Barkfluff
//
//  ViewModel для редактирования профиля
//

import SwiftUI
import AppKit
import BFCore
import BFNetworking

@MainActor
@Observable
final class ProfileEditViewModel {
    // MARK: - Dependencies

    private let userService: UserServiceProtocol
    private let fileService: FileServiceProtocol
    private let container: DependencyContainer

    // MARK: - Form State

    var firstName: String = ""
    var lastName: String = ""
    var username: String = ""
    var bio: String = ""
    var avatarURL: String?

    // Original values (для отслеживания изменений)
    private var originalFirstName: String = ""
    private var originalLastName: String = ""
    private var originalUsername: String = ""
    private var originalBio: String = ""

    // MARK: - UI State

    var isLoading = false
    var isSaving = false
    var showAvatarPicker = false
    var showError = false
    var errorMessage = ""
    var usernameStatus: UsernameValidationStatus?

    // MARK: - Avatar cropper state

    /// Картинка из fileImporter, ждущая обрезки в кропере 1:1.
    var pendingAvatarImage: NSImage?

    /// Флаг показа модального экрана кропера.
    var showCropper: Bool = false

    /// Флаг загрузки уже обрезанной картинки на сервер.
    var isUploadingAvatar: Bool = false

    // MARK: - Username validation

    private var usernameCheckTask: Task<Void, Never>?
    private var originalUsernameValue: String = ""

    // MARK: - Computed

    var hasChanges: Bool {
        firstName != originalFirstName ||
        lastName != originalLastName ||
        username != originalUsername ||
        bio != originalBio
    }

    var currentInitials: String {
        let first = firstName.prefix(1)
        let last = lastName.prefix(1)
        return last.isEmpty ? String(firstName.prefix(2)).uppercased() : "\(first)\(last)".uppercased()
    }

    var canSave: Bool {
        hasChanges && !isSaving && usernameStatus != .taken && usernameStatus != .tooShort && usernameStatus != .invalid
    }

    // MARK: - Init

    init(userService: UserServiceProtocol, fileService: FileServiceProtocol, container: DependencyContainer) {
        self.userService = userService
        self.fileService = fileService
        self.container = container
    }

    // MARK: - Methods

    /// Загрузить профиль пользователя
    func loadProfile() async {
        isLoading = true
        errorMessage = ""

        do {
            let user = try await userService.getCurrentUser()

            firstName = user.firstName
            lastName = user.lastName
            username = user.username
            bio = user.bio ?? ""
            avatarURL = user.profilePicturePreviewURL

            originalFirstName = user.firstName
            originalLastName = user.lastName
            originalUsername = user.username
            originalBio = user.bio ?? ""
            originalUsernameValue = user.username

            // Обновить контейнер
            container.currentUser = user
        } catch {
            errorMessage = error.localizedDescription
            showError = true
        }

        isLoading = false
    }

    /// Сохранить изменения
    func saveChanges() async {
        guard hasChanges else { return }

        isSaving = true
        errorMessage = ""

        do {
            // Сохраняем изменения последовательно
            if firstName != originalFirstName || lastName != originalLastName {
                try await userService.changeName(firstName: firstName, lastName: lastName)
            }

            if username != originalUsername && usernameStatus == .available {
                try await userService.changeUsername(newUsername: username)
            }

            if bio != originalBio {
                try await userService.changeBio(newBio: bio)
            }

            // Обновляем оригинальные значения
            originalFirstName = firstName
            originalLastName = lastName
            originalUsername = username
            originalBio = bio
            originalUsernameValue = username

            // Обновить пользователя в контейнере (inline — мгновенный UI)
            if var user = container.currentUser {
                user.firstName = firstName
                user.lastName = lastName
                user.username = username
                user.bio = bio.isEmpty ? nil : bio
                container.currentUser = user
            }

            // Write-through: ревалидация дёргает getCurrentUser и записывает
            // свежий profile в SQLite, чтобы следующий cold-start показал его сразу.
            await container.revalidateCurrentUser()

        } catch {
            errorMessage = error.localizedDescription
            showError = true
        }

        isSaving = false
    }

    /// Валидация имени пользователя с debounce
    func validateUsername() {
        // Отменяем предыдущую проверку
        usernameCheckTask?.cancel()

        let newUsername = username

        // Если username не изменился
        if newUsername == originalUsernameValue {
            usernameStatus = .unchanged
            return
        }

        // Проверка на длину
        if newUsername.count < 3 {
            usernameStatus = .tooShort
            return
        }

        // Проверка на допустимые символы (буквы, цифры, подчёркивание)
        let allowedCharacters = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_")
        if !newUsername.unicodeScalars.allSatisfy({ allowedCharacters.contains($0) }) {
            usernameStatus = .invalid
            return
        }

        // Debounce 500ms
        usernameStatus = .checking
        usernameCheckTask = Task {
            try? await Task.sleep(for: .milliseconds(500))

            guard !Task.isCancelled else { return }

            do {
                let exists = try await userService.checkUsernameExists(username: newUsername)
                if !Task.isCancelled {
                    usernameStatus = exists ? .taken : .available
                }
            } catch {
                if !Task.isCancelled {
                    usernameStatus = nil
                }
            }
        }
    }

    /// Обработка выбора аватара через fileImporter: читаем файл, превращаем
    /// в NSImage и показываем кропер 1:1. Сама заливка — после onCrop через uploadCropped.
    func handleAvatarSelection(result: Result<[URL], Error>) async {
        switch result {
        case .success(let urls):
            guard let fileURL = urls.first else { return }

            guard fileURL.startAccessingSecurityScopedResource() else {
                errorMessage = String(localized: "profile.edit.error.no_file_access")
                showError = true
                return
            }
            defer { fileURL.stopAccessingSecurityScopedResource() }

            do {
                let data = try Data(contentsOf: fileURL)
                guard let image = NSImage(data: data) else {
                    errorMessage = String(localized: "profile.edit.error.read_image")
                    showError = true
                    return
                }
                pendingAvatarImage = image
                showCropper = true
            } catch {
                errorMessage = error.localizedDescription
                showError = true
            }

        case .failure(let error):
            errorMessage = error.localizedDescription
            showError = true
        }
    }

    /// Залить уже обрезанную в кропере картинку: JPEG 0.85 → upload → setProfilePicture.
    func uploadCropped(_ image: NSImage) async {
        isUploadingAvatar = true
        defer {
            isUploadingAvatar = false
            pendingAvatarImage = nil
        }

        guard let data = image.jpegData(compressionQuality: 0.85) else {
            errorMessage = String(localized: "profile.edit.error.process_image")
            showError = true
            return
        }

        do {
            let fileID = try await fileService.uploadFile(
                data: data,
                fileName: "avatar.jpg",
                fileType: .userAvatar
            )
            try await userService.setProfilePicture(fileID: fileID)
            await loadProfile()
            // Write-through: новый аватар попадает в SQLite-кеш.
            await container.revalidateCurrentUser()
        } catch {
            errorMessage = error.localizedDescription
            showError = true
        }
    }

    /// Отменить кроп — сбросить pending state.
    func cancelAvatarCropping() {
        pendingAvatarImage = nil
    }

    /// Выход из аккаунта
    func logout() async {
        // Это будет обработано через AppCoordinator
    }
}
