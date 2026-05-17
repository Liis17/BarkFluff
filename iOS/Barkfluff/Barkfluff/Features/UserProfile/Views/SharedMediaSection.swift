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
    @Environment(\.locale) private var locale

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("user_profile.shared_media.title")
                    .font(.headline)
                Spacer()
            }
            .padding(.horizontal, 16)

            Picker("user_profile.shared_media.picker.type", selection: $viewModel.selectedMediaFilter) {
                ForEach(SharedMediaFilter.allCases, id: \.self) { filter in
                    Text(filter.displayName(in: locale)).tag(filter)
                }
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
                    "user_profile.shared_media.empty.title",
                    systemImage: viewModel.selectedMediaFilter == .media ? "photo" : "doc",
                    description: Text(viewModel.selectedMediaFilter == .media
                        ? "user_profile.shared_media.empty.media"
                        : "user_profile.shared_media.empty.documents")
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
                    Button("user_profile.shared_media.load_more") {
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
