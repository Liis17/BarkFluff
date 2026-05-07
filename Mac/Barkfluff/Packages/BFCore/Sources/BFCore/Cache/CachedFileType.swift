//
//  CachedFileType.swift
//  BFCore
//

import Foundation

/// Тип кешируемого файла на диске.
///
/// rawValue используется как имя поддиректории (с суффиксом `s`) и как значение
/// колонки `type` в таблице `cached_file`.
public enum CachedFileType: String, Sendable, Hashable, CaseIterable {
    case avatar
    case image
    case video
    case gif
    case document
    case audio
    case voice
    case sticker
    case preview
}
