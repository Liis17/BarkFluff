//
//  SharedMediaSection.swift
//  Barkfluff
//
//  Секция общих файлов чата
//

import SwiftUI
import BFCore

/// Секция общих медиа файлов
struct SharedMediaSection: View {
    @Bindable var viewModel: UserProfilePanelViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            // Заголовок + Segmented Picker
            HStack {
                Label("Вложения", systemImage: "photo.on.rectangle.angled")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.secondary)

                Spacer()
            }

            // Сегментированный переключатель: Медиа / Файлы
            Picker("Тип", selection: $viewModel.selectedMediaFilter) {
                ForEach(SharedMediaFilter.allCases, id: \.self) { filter in
                    Text(filter.displayName).tag(filter)
                }
            }
            .pickerStyle(.segmented)

            // Контент в зависимости от фильтра
            switch viewModel.selectedMediaFilter {
            case .media:
                if viewModel.mediaItems.isEmpty && !viewModel.isLoadingMedia {
                    emptyMediaPlaceholder(
                        icon: "photo.on.rectangle",
                        text: "Нет медиа"
                    )
                } else {
                    SharedMediaGridView(
                        items: viewModel.mediaItems,
                        fileService: viewModel.fileService,
                        isLoading: viewModel.isLoadingMedia,
                        onLoadMore: {
                            Task { await viewModel.loadMoreSharedMedia() }
                        }
                    )
                }

            case .documents:
                if viewModel.documentItems.isEmpty && !viewModel.isLoadingMedia {
                    emptyMediaPlaceholder(
                        icon: "doc.text",
                        text: "Нет файлов"
                    )
                } else {
                    SharedDocumentsListView(
                        items: viewModel.documentItems,
                        fileService: viewModel.fileService,
                        isLoading: viewModel.isLoadingMedia,
                        onLoadMore: {
                            Task { await viewModel.loadMoreSharedMedia() }
                        }
                    )
                }
            }
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.md)
        .onChange(of: viewModel.selectedMediaFilter) {
            Task { await viewModel.loadSharedMedia() }
        }
    }

    private func emptyMediaPlaceholder(icon: String, text: String) -> some View {
        VStack(spacing: Theme.Spacing.sm) {
            Image(systemName: icon)
                .font(.largeTitle)
                .foregroundStyle(.quaternary)
            Text(text)
                .font(.callout)
                .foregroundStyle(.tertiary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, Theme.Spacing.xl)
    }
}

#Preview {
    let container = DependencyContainer()
    let chat = Chat(id: "1", title: "Тест", isGroupChat: false, members: [])

    @Bindable var vm = UserProfilePanelViewModel(
        chat: chat,
        currentUserID: 0,
        userService: container.userService,
        chatService: container.chatService,
        sharedMediaService: container.sharedMediaService,
        fileService: container.fileService,
        onlineStatusService: container.onlineStatusService
    )

    SharedMediaSection(viewModel: vm)
        .padding()
        .frame(width: 300)
}
