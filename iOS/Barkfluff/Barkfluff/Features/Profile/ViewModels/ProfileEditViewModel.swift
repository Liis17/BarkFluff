//
//  ProfileEditViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel для редактирования профиля.
//  iOS-адаптация: вместо fileImporter с security-scoped resource — PhotosPicker.
//

import SwiftUI
import PhotosUI
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
    var showError = false
    var errorMessage = ""
    var usernameStatus: UsernameValidationStatus?

    // MARK: - Avatar selection (PhotosPicker)

    var selectedAvatarItem: PhotosPickerItem? {
        didSet {
            guard let item = selectedAvatarItem else { return }
            Task { await loadAvatar(from: item) }
        }
    }

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
            if firstName != originalFirstName || lastName != originalLastName {
                try await userService.changeName(firstName: firstName, lastName: lastName)
            }

            if username != originalUsername && usernameStatus == .available {
                try await userService.changeUsername(newUsername: username)
            }

            if bio != originalBio {
                try await userService.changeBio(newBio: bio)
            }

            originalFirstName = firstName
            originalLastName = lastName
            originalUsername = username
            originalBio = bio
            originalUsernameValue = username

            if var user = container.currentUser {
                user.firstName = firstName
                user.lastName = lastName
                user.username = username
                user.bio = bio.isEmpty ? nil : bio
                container.currentUser = user
            }

        } catch {
            errorMessage = error.localizedDescription
            showError = true
        }

        isSaving = false
    }

    /// Валидация имени пользователя с debounce
    func validateUsername() {
        usernameCheckTask?.cancel()

        let newUsername = username

        if newUsername == originalUsernameValue {
            usernameStatus = .unchanged
            return
        }

        if newUsername.count < 3 {
            usernameStatus = .tooShort
            return
        }

        let allowedCharacters = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_")
        if !newUsername.unicodeScalars.allSatisfy({ allowedCharacters.contains($0) }) {
            usernameStatus = .invalid
            return
        }

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

    /// Обработка выбора аватара через PhotosPicker.
    /// PhotosPicker.loadTransferable отдаёт уже декодированные байты;
    /// никакого security-scoped resource на iOS не требуется.
    private func loadAvatar(from item: PhotosPickerItem) async {
        do {
            guard let data = try await item.loadTransferable(type: Data.self) else {
                errorMessage = "Не удалось прочитать выбранное изображение"
                showError = true
                return
            }

            // Имя файла определяем эвристикой по типу контента;
            // PhotosPickerItem не отдаёт оригинальное имя.
            let fileName: String = {
                if let utType = item.supportedContentTypes.first {
                    return "avatar.\(utType.preferredFilenameExtension ?? "jpg")"
                }
                return "avatar.jpg"
            }()

            let fileID = try await fileService.uploadFile(
                data: data,
                fileName: fileName,
                fileType: .userAvatar
            )

            try await userService.setProfilePicture(fileID: fileID)

            await loadProfile()

        } catch {
            errorMessage = error.localizedDescription
            showError = true
        }
    }
}
