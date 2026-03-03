//
//  MessageAttachmentExtensions.swift
//  Barkfluff
//
//  Расширения для MessageAttachment (iOS версия)
//

import Foundation
import BFCore

extension MessageAttachment {
    /// Расширение файла
    public var fileExtension: String {
        URL(fileURLWithPath: fileName).pathExtension.lowercased()
    }

    /// Длительность (для аудио/видео) - заглушка, т.к. в текущей модели нет этого поля
    public var duration: TimeInterval? {
        // TODO: Добавить поле duration в модель MessageAttachment
        nil
    }
}
