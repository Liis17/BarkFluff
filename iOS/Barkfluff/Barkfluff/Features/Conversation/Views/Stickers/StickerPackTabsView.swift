//
//  StickerPackTabsView.swift
//  Barkfluff (iOS)
//
//  Полоса табов пикера: «Недавние» (если есть) + по табу на каждый стикерпак.
//

import SwiftUI
import BFCore

struct StickerPackTabsView: View {
    let packs: [StickerPack]
    let selectedTab: StickerPickerViewModel.Tab?
    let hasRecent: Bool
    let coverProvider: (StickerPack) -> Sticker?
    let onSelect: (StickerPickerViewModel.Tab) -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 4) {
                if hasRecent {
                    recentTab
                }
                ForEach(packs) { pack in
                    packTab(pack)
                }
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
        }
    }

    // MARK: - Subviews

    private var recentTab: some View {
        let isActive = selectedTab == .recent
        return Button {
            onSelect(.recent)
        } label: {
            Image(systemName: "clock")
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(isActive ? Color.accentColor : .secondary)
                .frame(width: 36, height: 36)
                .background(
                    RoundedRectangle(cornerRadius: 8)
                        .fill(isActive ? Color.accentColor.opacity(0.15) : .clear)
                )
        }
        .buttonStyle(.plain)
    }

    private func packTab(_ pack: StickerPack) -> some View {
        let tab: StickerPickerViewModel.Tab = .pack(id: pack.id)
        let isActive = selectedTab == tab
        let cover = coverProvider(pack)
        return Button {
            onSelect(tab)
        } label: {
            ZStack {
                if let cover {
                    StickerImageView(fileID: cover.fileID, size: 28) {
                        initialFallback(pack: pack)
                    }
                } else {
                    initialFallback(pack: pack)
                        .frame(width: 28, height: 28)
                }
            }
            .frame(width: 36, height: 36)
            .background(
                RoundedRectangle(cornerRadius: 8)
                    .fill(isActive ? Color.accentColor.opacity(0.15) : .clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .strokeBorder(isActive ? Color.accentColor : .clear, lineWidth: 1.5)
            )
        }
        .buttonStyle(.plain)
    }

    private func initialFallback(pack: StickerPack) -> some View {
        Text(String(pack.name.prefix(1)).uppercased())
            .font(.system(size: 14, weight: .semibold))
            .foregroundStyle(.secondary)
    }
}
