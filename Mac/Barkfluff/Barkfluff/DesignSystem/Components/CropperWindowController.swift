//
//  CropperWindowController.swift
//  Barkfluff (macOS)
//
//  Презентер для `ImageCropperView` через отдельное `NSWindow`.
//  Обходит SwiftUI `.sheet` / `.fileImporter`, которые на macOS 26 внутри
//  Form.grouped Section ломают hit-testing соседних контролов (Button
//  становится визуально активной, но клики не доходят).
//
//  Singleton-инстанс держит ссылку на окно, чтобы оно не уничтожилось до
//  завершения работы. Параллельно может быть открыт только один кропер.
//

import AppKit
import SwiftUI

@MainActor
final class CropperWindowController {

    static let shared = CropperWindowController()

    private var window: NSWindow?

    private init() {}

    /// Показать кропер в новом окне. Окно автоматически закрывается на
    /// «Отмена» / «Готово»; `onCrop` вызывается только при «Готово».
    func present(
        image: NSImage,
        aspectRatio: CGFloat,
        outputWidth: CGFloat,
        onCrop: @escaping (NSImage) -> Void
    ) {
        // Если уже открыто — закрываем предыдущее окно.
        close()

        let cropper = ImageCropperView(
            image: image,
            aspectRatio: aspectRatio,
            outputWidth: outputWidth,
            onCancel: { [weak self] in
                self?.close()
            },
            onCrop: { [weak self] cropped in
                self?.close()
                onCrop(cropped)
            }
        )

        let hosting = NSHostingController(rootView: cropper)
        let window = NSWindow(contentViewController: hosting)
        window.styleMask = [.titled, .closable, .resizable]
        window.title = "Обрезать изображение"
        window.setContentSize(NSSize(width: 900, height: 640))
        window.center()
        window.isReleasedWhenClosed = false
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        self.window = window
    }

    private func close() {
        window?.close()
        window = nil
    }
}
