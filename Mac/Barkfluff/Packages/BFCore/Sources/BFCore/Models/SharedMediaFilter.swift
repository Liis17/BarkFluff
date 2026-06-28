//
//  SharedMediaFilter.swift
//  BFCore
//
//  Фильтр для общего медиа
//

import Foundation

/// Фильтр для отображения общего медиа
public enum SharedMediaFilter: String, CaseIterable, Sendable {
    case media
    case documents

    /// Локализованное название (использует системную локаль).
    public var displayName: String { displayName(in: .current) }

    /// Локализованное название с явной локалью (для реактивного UI).
    public func displayName(in locale: Locale) -> String {
        switch self {
        case .media:     return String(localized: "bfcore.shared_media.filter.media", bundle: .module, locale: locale)
        case .documents: return String(localized: "bfcore.shared_media.filter.documents", bundle: .module, locale: locale)
        }
    }

    public var systemImage: String {
        switch self {
        case .media: return "photo.on.rectangle"
        case .documents: return "doc.on.doc"
        }
    }
}
