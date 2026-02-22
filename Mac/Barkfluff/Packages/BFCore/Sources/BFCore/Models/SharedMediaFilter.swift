//
//  SharedMediaFilter.swift
//  BFCore
//
//  Фильтр для общего медиа
//

import Foundation

/// Фильтр для отображения общего медиа
public enum SharedMediaFilter: String, CaseIterable, Sendable {
    case media = "Медиа"
    case documents = "Файлы"

    public var displayName: String {
        rawValue
    }

    public var systemImage: String {
        switch self {
        case .media: return "photo.on.rectangle"
        case .documents: return "doc.on.doc"
        }
    }
}
