//
//  BackgroundsGrid.swift
//  Barkfluff
//
//  Сетка 3xN с фонами чата + ячейка добавления.
//  Long-press / контекстное меню — переключают delete-mode для удаления.
//

import SwiftUI
import BFCore
import UniformTypeIdentifiers

struct BackgroundsGrid: View {
    @Bindable var viewModel: PersonalizationSettingsViewModel
    @Bindable var settings: PersonalizationSettings

    @State private var showImporter = false

    // Адаптивная сетка: ширина окна Settings определяет число колонок.
    // 80pt минимум на ячейку → ~5–6 колонок при 540pt окне, больше на широких.
    private let columns: [GridItem] = [
        GridItem(.adaptive(minimum: 80), spacing: Theme.Spacing.sm)
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.md) {
            LazyVGrid(columns: columns, spacing: Theme.Spacing.sm) {
                addCell

                ForEach(viewModel.backgroundFileIDs, id: \.self) { fileID in
                    BackgroundCell(
                        fileID: fileID,
                        isSelected: settings.currentBackgroundFileID == fileID,
                        isDeleteMode: viewModel.deleteMode,
                        onTap: {
                            if viewModel.deleteMode {
                                Task { await viewModel.deleteBackground(fileID: fileID) }
                            } else {
                                viewModel.selectBackground(fileID: fileID)
                            }
                        },
                        onContextDelete: {
                            Task { await viewModel.deleteBackground(fileID: fileID) }
                        },
                        onEnterDeleteMode: {
                            viewModel.deleteMode = true
                        }
                    )
                }
            }

            if !settings.currentBackgroundFileID.isEmpty {
                Button(role: .destructive) {
                    viewModel.clearBackgroundSelection()
                } label: {
                    Label("settings.personalization.backgrounds.remove_chat_bg", systemImage: "xmark.circle.fill")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .controlSize(.regular)
            }
        }
        .fileImporter(
            isPresented: $showImporter,
            allowedContentTypes: [.image],
            allowsMultipleSelection: false
        ) { result in
            Task { await viewModel.handleBackgroundSelection(result: result) }
        }
    }

    // MARK: - Add cell

    private var addCell: some View {
        Button {
            if viewModel.deleteMode {
                viewModel.deleteMode = false
            } else {
                showImporter = true
            }
        } label: {
            ZStack {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(Color.gray.opacity(0.15))
                Image(systemName: viewModel.deleteMode ? "checkmark" : (viewModel.isUploadingBackground ? "arrow.up.circle" : "plus"))
                    .font(.system(size: 24, weight: .medium))
                    .foregroundStyle(.secondary)
            }
            // Вертикальный 1:2 (ширина:высота) — паритет с ячейками фона.
            .aspectRatio(0.5, contentMode: .fit)
        }
        .buttonStyle(.plain)
        .disabled(viewModel.isUploadingBackground && !viewModel.deleteMode)
    }
}

// MARK: - Cell

private struct BackgroundCell: View {
    let fileID: String
    let isSelected: Bool
    let isDeleteMode: Bool
    let onTap: () -> Void
    let onContextDelete: () -> Void
    let onEnterDeleteMode: () -> Void

    var body: some View {
        // Вертикальный 1:2 (ширина:высота): держателем фрейма выступает Color.clear,
        // в overlay — собственно картинка через CachedImageView.
        Color.clear
            .aspectRatio(0.5, contentMode: .fit)
            .overlay {
                CachedImageView(
                    fileID: fileID,
                    type: .image,
                    content: { image in
                        image.resizable().aspectRatio(contentMode: .fill)
                    },
                    placeholder: {
                        Color.gray.opacity(0.2)
                    }
                )
            }
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay {
                if isSelected {
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .strokeBorder(Color.accentColor, lineWidth: 3)
                }
            }
            .overlay(alignment: .topTrailing) {
                if isDeleteMode {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 22))
                        .foregroundStyle(.white, .red)
                        .padding(4)
                }
            }
            .contentShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            .onTapGesture { onTap() }
            .onLongPressGesture(minimumDuration: 0.4) {
                onEnterDeleteMode()
            }
            .contextMenu {
                Button(role: .destructive) {
                    onContextDelete()
                } label: {
                    Label("common.delete", systemImage: "trash")
                }
            }
    }
}
