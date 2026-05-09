//
//  StickerPickerViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel пикера стикеров: загрузка паков, выбор активного таба,
//  фильтрация по emoji, обработка тапа.
//

import Foundation
import Observation
import BFCore

@MainActor
@Observable
final class StickerPickerViewModel {

    /// Идентификатор активной вкладки.
    enum Tab: Hashable {
        case recent
        case pack(id: String)
    }

    // MARK: - State

    var packs: [StickerPack] = []
    var selectedTab: Tab?
    private(set) var stickersByPack: [String: [Sticker]] = [:]
    private(set) var recentStickers: [Sticker] = []
    var searchText = ""
    var isLoadingPacks = false
    var isLoadingPack = false
    var errorMessage: String?

    var onStickerSelected: ((Sticker) -> Void)?

    // MARK: - Dependencies

    private let service: StickersServiceProtocol
    private let recentStore: RecentStickersStore

    init(service: StickersServiceProtocol, recentStore: RecentStickersStore) {
        self.service = service
        self.recentStore = recentStore
    }

    // MARK: - Public

    func loadIfNeeded() async {
        guard packs.isEmpty, !isLoadingPacks else {
            await reconcileRecents()
            return
        }
        isLoadingPacks = true
        defer { isLoadingPacks = false }
        do {
            let loaded = try await service.listPacks(forceRefresh: false)
            packs = loaded
            errorMessage = nil

            for pack in loaded {
                if stickersByPack[pack.id] == nil {
                    if let (_, stickers) = try? await service.getPack(id: pack.id, forceRefresh: false) {
                        stickersByPack[pack.id] = stickers
                    }
                }
            }

            await reconcileRecents()

            if selectedTab == nil {
                if !recentStickers.isEmpty {
                    selectedTab = .recent
                } else if let first = loaded.first {
                    selectedTab = .pack(id: first.id)
                }
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func select(tab: Tab) async {
        selectedTab = tab
        guard case .pack(let id) = tab else { return }
        if stickersByPack[id] != nil { return }
        isLoadingPack = true
        defer { isLoadingPack = false }
        do {
            let (_, stickers) = try await service.getPack(id: id, forceRefresh: false)
            stickersByPack[id] = stickers
            await service.prefetchStickerImages(stickers)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func didTap(_ sticker: Sticker) {
        recentStore.add(sticker.id)
        onStickerSelected?(sticker)
    }

    var visibleStickers: [Sticker] {
        let base: [Sticker]
        switch selectedTab {
        case .recent:
            base = recentStickers
        case .pack(let id):
            base = stickersByPack[id] ?? []
        case .none:
            base = []
        }
        let q = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty else { return base }
        return base.filter { $0.emoji.contains(q) }
    }

    func coverSticker(for pack: StickerPack) -> Sticker? {
        guard let stickers = stickersByPack[pack.id] else { return nil }
        if !pack.coverStickerID.isEmpty,
           let cover = stickers.first(where: { $0.id == pack.coverStickerID }) {
            return cover
        }
        return stickers.first
    }

    // MARK: - Private

    private func reconcileRecents() async {
        var resolved: [Sticker] = []
        resolved.reserveCapacity(recentStore.stickerIDs.count)
        for id in recentStore.stickerIDs {
            if let sticker = await service.sticker(byID: id) {
                resolved.append(sticker)
            }
        }
        recentStickers = resolved
    }
}
