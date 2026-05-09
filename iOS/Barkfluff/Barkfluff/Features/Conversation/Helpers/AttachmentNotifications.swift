//
//  AttachmentNotifications.swift
//  Barkfluff (iOS)
//
//  Notification names для вложений
//

import Foundation

extension Notification.Name {
    /// Уведомление при нажатии на медиа-вложение (фото, видео, GIF)
    /// - userInfo["attachment"]: MessageAttachment
    /// - userInfo["allAttachments"]: [MessageAttachment]
    /// - userInfo["messageText"]: String?
    static let attachmentTapped = Notification.Name("attachmentTapped")

    /// Уведомление при нажатии на документ или аудио (скачивание, не viewer)
    /// - userInfo["attachment"]: MessageAttachment
    static let documentDownloadRequested = Notification.Name("documentDownloadRequested")
}
