//
//  NSImage+JPEG.swift
//  Barkfluff (macOS)
//
//  Конвертация NSImage в JPEG Data через NSBitmapImageRep.
//  Общий extension для всего таргета — используется и кропером, и
//  ConversationViewModel при отправке вложений-изображений.
//

import AppKit

extension NSImage {
    /// Закодировать NSImage в JPEG с заданным качеством сжатия (0.0…1.0).
    /// Возвращает nil, если у изображения нет TIFF-представления или
    /// его не удалось распаковать в bitmap.
    func jpegData(compressionQuality: CGFloat) -> Data? {
        guard let tiffData = tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiffData) else { return nil }
        return bitmap.representation(using: .jpeg, properties: [.compressionFactor: compressionQuality])
    }
}
