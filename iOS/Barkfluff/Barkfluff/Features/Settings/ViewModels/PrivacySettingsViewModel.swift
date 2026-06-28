//
//  PrivacySettingsViewModel.swift
//  Barkfluff (iOS)
//

import SwiftUI
import Observation
import BFCore
import BFNetworking

@MainActor
@Observable
final class PrivacySettingsViewModel {

    var settings: PrivacySettingsInfo?
    var isLoading: Bool = false
    var isSaving: Bool = false
    var errorMessage: String?

    weak var dependencyContainer: DependencyContainer?

    func load() async {
        guard let dc = dependencyContainer else { return }
        isLoading = true
        errorMessage = nil
        do {
            settings = try await dc.userService.getPrivacySettings()
        } catch {
            errorMessage = String(localized: "settings.privacy.error.load")
        }
        isLoading = false
    }

    /// Применить мутацию к локальной копии и отправить на сервер.
    /// При ошибке возвращаем UI в исходное состояние.
    func update(_ mutate: (inout PrivacySettingsInfo) -> Void) async {
        guard let dc = dependencyContainer, var current = settings else { return }
        let previous = current
        mutate(&current)
        guard current != previous else { return }
        settings = current

        isSaving = true
        do {
            try await dc.userService.updatePrivacySettings(current)
            errorMessage = nil
        } catch {
            settings = previous
            errorMessage = String(localized: "settings.privacy.error.save")
        }
        isSaving = false
    }
}
