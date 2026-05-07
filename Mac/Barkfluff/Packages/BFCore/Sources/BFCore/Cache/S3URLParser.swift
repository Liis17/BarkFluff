//
//  S3URLParser.swift
//  BFCore
//
//  Извлечение fileID из presigned-URL S3.
//

import Foundation

public enum S3URLParser {

    /// Возвращает path-компонент URL без ведущего "/" — это и есть S3-ключ,
    /// который мы используем в качестве fileID для аватаров и обложек.
    ///
    /// Пример: `https://bucket.s3.endpoint/avatars/abc123?X-Amz-...` → `avatars/abc123`.
    public static func fileID(from urlString: String?) -> String? {
        guard let urlString, !urlString.isEmpty else { return nil }
        guard let comps = URLComponents(string: urlString) else { return nil }
        let path = comps.path
        guard !path.isEmpty else { return nil }
        let trimmed = path.hasPrefix("/") ? String(path.dropFirst()) : path
        return trimmed.isEmpty ? nil : trimmed
    }
}
