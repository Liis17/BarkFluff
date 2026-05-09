//
//  RecentStickersStore.swift
//  Barkfluff
//
//  Хранит ID недавно использованных стикеров в UserDefaults
//  по образцу `recentEmojis` из EmojiPickerView.
//

import Foundation
import Observation

@Observable
final class RecentStickersStore {
    private let key = "recentStickerIDs"
    private let maxRecent = 32

    /// Самые свежие — в начале списка.
    private(set) var stickerIDs: [String]

    init() {
        self.stickerIDs = UserDefaults.standard.stringArray(forKey: "recentStickerIDs") ?? []
    }

    /// Добавить (или переместить наверх) стикер в список недавних.
    func add(_ id: String) {
        guard !id.isEmpty else { return }
        stickerIDs.removeAll { $0 == id }
        stickerIDs.insert(id, at: 0)
        if stickerIDs.count > maxRecent {
            stickerIDs = Array(stickerIDs.prefix(maxRecent))
        }
        UserDefaults.standard.set(stickerIDs, forKey: key)
    }

    /// Очистить список (например, при logout).
    func clear() {
        stickerIDs.removeAll()
        UserDefaults.standard.removeObject(forKey: key)
    }
}
