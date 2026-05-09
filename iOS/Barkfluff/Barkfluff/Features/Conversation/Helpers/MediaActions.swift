//
//  MediaActions.swift
//  Barkfluff (iOS)
//
//  Хелперы для медиа-действий из контекстного меню сообщений:
//  копирование изображения в UIPasteboard, сохранение медиа в Photos,
//  экспорт документов через UIActivityViewController.
//

import Foundation
import UIKit
import Photos
import BFCore

@MainActor
enum MediaActions {

    // MARK: - Public API

    /// Скопировать изображение в системный буфер обмена.
    /// Если файл ещё не в локальном кеше — сначала скачает его через `mediaCacheManager`.
    static func copyImageToPasteboard(_ attachment: MessageAttachment, container: DependencyContainer) async {
        do {
            let cacheURL = try await container.mediaCacheManager.resolveURL(
                for: attachment.fileID,
                type: attachment.type.cacheType
            )
            guard let img = UIImage(contentsOfFile: cacheURL.path) else { return }
            UIPasteboard.general.image = img
        } catch {
            // Тихий фейл — не ломаем UI.
        }
    }

    /// Сохранить одно или несколько изображений в Photos через PHPhotoLibrary.
    /// Требует NSPhotoLibraryAddUsageDescription в Info.plist.
    static func saveImages(_ attachments: [MessageAttachment], container: DependencyContainer) async {
        guard !attachments.isEmpty else { return }
        let granted = await ensurePhotosAddPermission()
        guard granted else { return }

        var urls: [URL] = []
        for attachment in attachments {
            if let url = try? await container.mediaCacheManager.resolveURL(
                for: attachment.fileID,
                type: attachment.type.cacheType
            ) {
                urls.append(url)
            }
        }
        guard !urls.isEmpty else { return }

        do {
            try await PHPhotoLibrary.shared().performChanges {
                for url in urls {
                    PHAssetChangeRequest.creationRequestForAssetFromImage(atFileURL: url)
                }
            }
        } catch {
            // Тихий фейл — не ломаем UI; пользователь увидит, что фото не появилось.
        }
    }

    /// Сохранить документы / аудио. На iOS — открываем системный share sheet,
    /// пользователь сам выбирает «Сохранить в Файлы» / «Поделиться».
    static func saveDocuments(_ attachments: [MessageAttachment], container: DependencyContainer) async {
        guard !attachments.isEmpty else { return }

        var urls: [URL] = []
        for attachment in attachments {
            if let url = try? await container.mediaCacheManager.resolveURL(
                for: attachment.fileID,
                type: attachment.type.cacheType
            ) {
                urls.append(url)
            }
        }
        guard !urls.isEmpty else { return }

        await presentShareSheet(items: urls)
    }

    // MARK: - Private

    private static func ensurePhotosAddPermission() async -> Bool {
        let status = PHPhotoLibrary.authorizationStatus(for: .addOnly)
        if status == .authorized || status == .limited { return true }
        if status == .denied || status == .restricted { return false }

        let newStatus = await withCheckedContinuation { (cont: CheckedContinuation<PHAuthorizationStatus, Never>) in
            PHPhotoLibrary.requestAuthorization(for: .addOnly) { cont.resume(returning: $0) }
        }
        return newStatus == .authorized || newStatus == .limited
    }

    /// Показать UIActivityViewController с переданными элементами над текущим окном.
    static func presentShareSheet(items: [Any]) async {
        guard let scene = UIApplication.shared.connectedScenes
            .compactMap({ $0 as? UIWindowScene })
            .first(where: { $0.activationState == .foregroundActive }),
              let keyWindow = scene.windows.first(where: { $0.isKeyWindow }) ?? scene.windows.first,
              let root = keyWindow.rootViewController else { return }

        let activity = UIActivityViewController(activityItems: items, applicationActivities: nil)

        // Предотвращаем краш на iPad: указываем источник popover'а.
        if let pop = activity.popoverPresentationController {
            pop.sourceView = root.view
            pop.sourceRect = CGRect(x: root.view.bounds.midX, y: root.view.bounds.midY, width: 0, height: 0)
            pop.permittedArrowDirections = []
        }

        // Находим «верхний» уже презентованный контроллер, чтобы не падать с
        // «attempt to present X on Y while Z is presenting».
        var topController: UIViewController = root
        while let presented = topController.presentedViewController {
            topController = presented
        }
        topController.present(activity, animated: true)
    }
}
