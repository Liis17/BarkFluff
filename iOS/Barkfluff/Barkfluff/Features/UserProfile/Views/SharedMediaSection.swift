//
//  SharedMediaSection.swift
//  Barkfluff (iOS)
//
//  Секция «Общий контент»: переключатель Медиа/Документы + грид.
//

import SwiftUI
import BFCore

struct SharedMediaSection: View {
    @Bindable var viewModel: UserProfilePanelViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Общий контент")
                    .font(.headline)
                Spacer()
            }
            .padding(.horizontal, 16)

            Picker("Тип", selection: $viewModel.selectedMediaFilter) {
                Text("Медиа").tag(SharedMediaFilter.media)
                Text("Документы").tag(SharedMediaFilter.documents)
            }
            .pickerStyle(.segmented)
            .padding(.horizontal, 16)
            .onChange(of: viewModel.selectedMediaFilter) { _, _ in
                Task { await viewModel.loadSharedMedia() }
            }

            if viewModel.isLoadingMedia && currentItems.isEmpty {
                HStack {
                    Spacer()
                    ProgressView()
                        .padding(.vertical, 24)
                    Spacer()
                }
            } else if currentItems.isEmpty {
                ContentUnavailableView(
                    "Пусто",
                    systemImage: viewModel.selectedMediaFilter == .media ? "photo" : "doc",
                    description: Text(viewModel.selectedMediaFilter == .media
                        ? "В этом чате пока нет общих фото и видео"
                        : "В этом чате пока нет общих документов")
                )
                .frame(height: 200)
            } else {
                switch viewModel.selectedMediaFilter {
                case .media:
                    SharedMediaGridView(items: viewModel.mediaItems)
                        .padding(.horizontal, 16)
                case .documents:
                    SharedDocumentsListView(items: viewModel.documentItems)
                        .padding(.horizontal, 16)
                }

                if viewModel.hasMoreMedia {
                    Button("Загрузить ещё") {
                        Task { await viewModel.loadMoreSharedMedia() }
                    }
                    .font(.callout)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 8)
                }
            }
        }
    }

    private var currentItems: [SharedMediaItem] {
        switch viewModel.selectedMediaFilter {
        case .media: return viewModel.mediaItems
        case .documents: return viewModel.documentItems
        }
    }
}
