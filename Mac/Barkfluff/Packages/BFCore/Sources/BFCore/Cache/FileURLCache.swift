//
//  FileURLCache.swift
//  BFCore
//
//  Двухуровневый кеш URL файлов: runtime словарь + UserDefaults
//

import Foundation
import CryptoKit

/// Кеш временных URL для скачивания файлов.
/// Уровень 1: runtime in-memory словарь (быстро, сбрасывается при перезапуске).
/// Уровень 2: UserDefaults с suite (персистентный, переживает перезапуск).
public actor FileURLCache {
    private var runtimeCache: [String: String] = [:]
    private let defaults: UserDefaults

    public init() {
        self.defaults = UserDefaults(suiteName: "com.barkfluff.fileURLCache") ?? .standard
    }

    public func getURL(forFileID fileID: String) -> String? {
        if let url = runtimeCache[fileID] { return url }
        let key = cacheKey(fileID)
        if let url = defaults.string(forKey: key) {
            runtimeCache[fileID] = url
            return url
        }
        return nil
    }

    public func setURL(_ url: String, forFileID fileID: String) {
        runtimeCache[fileID] = url
        defaults.set(url, forKey: cacheKey(fileID))
    }

    public func clear() {
        runtimeCache.removeAll()
        defaults.removePersistentDomain(forName: "com.barkfluff.fileURLCache")
    }

    private func cacheKey(_ fileID: String) -> String {
        let data = Data(fileID.utf8)
        let hash = SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }
}
