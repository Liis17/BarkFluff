//
//  AttachmentNotifications.swift
//  Barkfluff
//
//  Notification names для вложений
//

import Foundation

extension Notification.Name {
    /// Уведомление при нажатии на вложение
    /// - userInfo["attachment"]: MessageAttachment - вложение
    /// - userInfo["allAttachments"]: [MessageAttachment] - все вложения в сообщении
    static let attachmentTapped = Notification.Name("attachmentTapped")
}
