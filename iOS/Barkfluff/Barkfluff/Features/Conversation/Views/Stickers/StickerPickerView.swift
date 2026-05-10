//
//  StickerPickerView.swift
//  Barkfluff (iOS)
//
//  Bottom-sheet пикера стикеров: поиск по emoji + сетка стикеров +
//  полоса табов паков (с «Недавние» если есть).
//

import SwiftUI
import BFCore

struct StickerPickerView: View {
    @State private var viewModel: StickerPickerViewModel
    /// Стикер, для которого показывается увеличенный preview (long-press).
    @State private var previewSticker: Sticker?
    private let onStickerSelected: (Sticker) -> Void

    init(
        service: StickersServiceProtocol,
        recentStore: RecentStickersStore,
        onStickerSelected: @escaping (Sticker) -> Void
    ) {
        _viewModel = State(initialValue: StickerPickerViewModel(
            service: service,
            recentStore: recentStore
        ))
        self.onStickerSelected = onStickerSelected
    }

    var body: some View {
        VStack(spacing: 0) {
            searchField
            Divider()

            ZStack {
                if viewModel.isLoadingPacks && viewModel.packs.isEmpty {
                    ProgressView()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else if viewModel.packs.isEmpty, let error = viewModel.errorMessage {
                    errorView(error)
                } else if viewModel.packs.isEmpty {
                    emptyView
                } else {
                    StickersGridView(
                        stickers: viewModel.visibleStickers,
                        onTap: { sticker in
                            viewModel.didTap(sticker)
                        },
                        onLongPressStart: { sticker in
                            previewSticker = sticker
                        },
                        onLongPressEnd: {
                            previewSticker = nil
                        }
                    )
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)

            Divider()
            StickerPackTabsView(
                packs: viewModel.packs,
                selectedTab: viewModel.selectedTab,
                hasRecent: !viewModel.recentStickers.isEmpty,
                coverProvider: { viewModel.coverSticker(for: $0) },
                onSelect: { tab in
                    Task { await viewModel.select(tab: tab) }
                }
            )
        }
        .padding(.top, 16)
        .overlay {
            if let preview = previewSticker {
                stickerPreviewOverlay(preview)
            }
        }
        .task {
            viewModel.onStickerSelected = onStickerSelected
            await viewModel.loadIfNeeded()
        }
    }

    // MARK: - Subviews

    private var searchField: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass")
                .foregroundStyle(.secondary)
            TextField("Поиск по emoji…", text: $viewModel.searchText)
                .textFieldStyle(.plain)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var emptyView: some View {
        VStack(spacing: 8) {
            Image(systemName: "square.grid.2x2")
                .font(.system(size: 28))
                .foregroundStyle(.secondary)
            Text("Стикерпаков пока нет")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
        .padding()
    }

    private func errorView(_ text: String) -> some View {
        VStack(spacing: 8) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 24))
                .foregroundStyle(.orange)
            Text(text)
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal)
        }
    }

    private func stickerPreviewOverlay(_ sticker: Sticker) -> some View {
        ZStack {
            Rectangle()
                .fill(.black.opacity(0.35))
                .ignoresSafeArea()

            StickerImageView(fileID: sticker.fileID, size: 240)
                .padding(20)
                .background(
                    RoundedRectangle(cornerRadius: 20)
                        .fill(.ultraThinMaterial)
                )
        }
        .transition(.opacity)
        .allowsHitTesting(false)
    }
}
