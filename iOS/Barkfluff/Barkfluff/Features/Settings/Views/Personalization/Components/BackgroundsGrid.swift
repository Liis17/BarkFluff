//
//  BackgroundsGrid.swift
//  Barkfluff (iOS)
//
//  Сетка фонов чата + ячейка добавления через PhotosPicker.
//  Long-press / контекстное меню — переключают delete-mode для удаления.
//

import SwiftUI
import PhotosUI
import BFCore

struct BackgroundsGrid: View {
    @Bindable var viewModel: PersonalizationSettingsViewModel
    @Bindable var settings: PersonalizationSettings

    private let columns: [GridItem] = [
        GridItem(.adaptive(minimum: 90), spacing: Theme.Spacing.sm)
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
                    Label("settings.personalization.background.remove", systemImage: "xmark.circle.fill")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
            }
        }
    }

    // MARK: - Add cell

    @ViewBuilder
    private var addCell: some View {
        if viewModel.deleteMode {
            Button {
                viewModel.deleteMode = false
            } label: {
                AddCellContent(systemImage: "checkmark")
            }
            .buttonStyle(.plain)
        } else {
            let icon: String = viewModel.isUploadingBackground ? "arrow.up.circle" : "plus"
            let disabled = viewModel.isUploadingBackground

            PhotosPicker(
                selection: $viewModel.selectedBackgroundItem,
                matching: .images,
                photoLibrary: .shared()
            ) {
                AddCellContent(systemImage: icon)
            }
            .buttonStyle(.plain)
            .disabled(disabled)
        }
    }
}

// MARK: - Add cell content

private struct AddCellContent: View {
    let systemImage: String

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(Color.gray.opacity(0.15))
            Image(systemName: systemImage)
                .font(.system(size: 24, weight: .medium))
                .foregroundStyle(.secondary)
        }
        .aspectRatio(0.5, contentMode: .fit)
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
