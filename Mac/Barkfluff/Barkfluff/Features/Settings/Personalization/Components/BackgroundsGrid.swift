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

    private let columns: [GridItem] = Array(
        repeating: GridItem(.flexible(), spacing: Theme.Spacing.sm),
        count: 3
    )

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
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
                Button("Убрать фон чата", role: .destructive) {
                    viewModel.clearBackgroundSelection()
                }
                .buttonStyle(.borderless)
                .controlSize(.small)
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
            .aspectRatio(1, contentMode: .fit)
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
        ZStack {
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
            .aspectRatio(1, contentMode: .fill)
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay {
                if isSelected {
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .strokeBorder(Color.accentColor, lineWidth: 3)
                }
            }

            if isDeleteMode {
                VStack {
                    HStack {
                        Spacer()
                        Image(systemName: "xmark.circle.fill")
                            .font(.system(size: 22))
                            .foregroundStyle(.white, .red)
                            .padding(4)
                    }
                    Spacer()
                }
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
                Label("Удалить", systemImage: "trash")
            }
        }
    }
}
