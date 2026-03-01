//
//  AttachmentNotifications.swift
//  Barkfluff
//
//  Notification names для вложений
//

import Foundation

extension Notification.Name {
    /// Уведомление при нажатии на медиа-вложение (фото, видео, GIF)
    /// - userInfo["attachment"]: MessageAttachment - вложение
    /// - userInfo["allAttachments"]: [MessageAttachment] - все медиа-вложения в сообщении
    /// - userInfo["messageText"]: String? - текст сообщения (опционально)
    static let attachmentTapped = Notification.Name("attachmentTapped")

    /// Уведомление при нажатии на документ или аудио (скачивание, не viewer)
    /// - userInfo["attachment"]: MessageAttachment - вложение
    static let documentDownloadRequested = Notification.Name("documentDownloadRequested")
}
