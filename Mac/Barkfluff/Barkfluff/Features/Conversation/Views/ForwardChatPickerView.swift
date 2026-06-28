//
//  ForwardChatPickerView.swift
//  Barkfluff
//
//  Sheet выбора чатов для пересылки сообщения.
//  Мультивыбор + опциональный комментарий + кнопка отправки.
//

import SwiftUI
import BFCore

/// Sheet выбора целевых чатов при пересылке сообщения.
struct ForwardChatPickerView: View {
    let messageID: Int64
    /// Используется только для совместимости с существующим SheetType — фильтрацию по нему не делаем.
    let sourceChatID: String

    @Environment(\.dismiss) private var dismiss
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: ForwardChatPickerViewModel?

    var body: some View {
        NavigationStack {
            Group {
                if let viewModel {
                    pickerContent(viewModel: viewModel)
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .navigationTitle(navigationTitle)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("common.cancel") { dismiss() }
                }
            }
        }
        .frame(minWidth: 400, minHeight: 520)
        .task {
            if viewModel == nil {
                viewModel = ForwardChatPickerViewModel(
                    allChats: coordinator.chatListViewModel?.chats ?? [],
                    messageService: container.messageService,
                    messageID: messageID
                )
            }
        }
    }

    private var navigationTitle: String {
        let count = viewModel?.selectedChatIDs.count ?? 0
        if count > 0 {
            return String(localized: "conversation.forward.title_count \(count)")
        }
        return String(localized: "conversation.forward.title")
    }

    @ViewBuilder
    private func pickerContent(viewModel: ForwardChatPickerViewModel) -> some View {
        VStack(spacing: 0) {
            if viewModel.allChats.isEmpty {
                ContentUnavailableView(
                    "conversation.forward.no_chats.title",
                    systemImage: "message",
                    description: Text("conversation.forward.no_chats.description")
                )
            } else {
                chatList(viewModel: viewModel)
            }

            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .padding(.horizontal, Theme.Spacing.md)
                    .padding(.top, Theme.Spacing.xs)
            }

            Divider()

            commentBar(viewModel: viewModel)
        }
    }

    @ViewBuilder
    private func chatList(viewModel: ForwardChatPickerViewModel) -> some View {
        ScrollView {
            LazyVStack(spacing: 0) {
                ForEach(viewModel.filteredChats) { chat in
                    ForwardChatRow(
                        chat: chat,
                        isSelected: viewModel.selectedChatIDs.contains(chat.id)
                    ) {
                        viewModel.toggle(chatID: chat.id)
                    }
                    Divider()
                        .padding(.leading, 60)
                }
            }
        }
        .searchable(
            text: Bindable(viewModel).searchText,
            placement: .toolbar,
            prompt: Text("conversation.forward.search_placeholder")
        )
    }

    @ViewBuilder
    private func commentBar(viewModel: ForwardChatPickerViewModel) -> some View {
        HStack(alignment: .center, spacing: Theme.Spacing.sm) {
            HStack(alignment: .center, spacing: Theme.Spacing.xs) {
                TextField("conversation.forward.comment_placeholder", text: Bindable(viewModel).commentText, axis: .vertical)
                    .textFieldStyle(.plain)
                    .lineLimit(1...4)
                    .onSubmit { performForward(viewModel: viewModel) }
            }
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.vertical, Theme.Spacing.sm)
            .glassEffect(.regular, in: .capsule)

            Button {
                performForward(viewModel: viewModel)
            } label: {
                Group {
                    if viewModel.isSending {
                        ProgressView()
                            .controlSize(.small)
                            .tint(.white)
                    } else {
                        Image(systemName: "paperplane.fill")
                            .font(.title3)
                            .fontWeight(.semibold)
                            .foregroundStyle(.white)
                    }
                }
                .frame(width: 36, height: 36)
                .background(
                    Circle()
                        .fill(viewModel.canSend ? Color.accentColor : Color.gray.opacity(0.4))
                )
            }
            .buttonStyle(.plain)
            .disabled(!viewModel.canSend)
            .help("conversation.forward.send_tooltip")
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.sm)
    }

    private func performForward(viewModel: ForwardChatPickerViewModel) {
        guard viewModel.canSend else { return }
        Task {
            let result = await viewModel.forwardToSelected()
            if result.success > 0 {
                dismiss()
            }
        }
    }
}

/// Строка чата в модалке пересылки: только аватар, имя и галочка.
private struct ForwardChatRow: View {
    let chat: Chat
    let isSelected: Bool
    let onTap: () -> Void

    @State private var isHovered = false

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: Theme.Spacing.md) {
                AvatarView(
                    imageURL: chat.pictureURL,
                    initials: chat.avatarInitials,
                    size: 40
                )

                Text(chat.title)
                    .font(.body)
                    .foregroundStyle(.primary)
                    .lineLimit(1)
                    .frame(maxWidth: .infinity, alignment: .leading)

                Image(systemName: isSelected ? "checkmark.circle.fill" : "circle")
                    .font(.title3)
                    .foregroundStyle(isSelected ? Color.accentColor : .secondary.opacity(0.5))
            }
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.vertical, Theme.Spacing.sm)
            .contentShape(Rectangle())
            .background {
                if isSelected {
                    Color.accentColor.opacity(0.08)
                } else if isHovered {
                    Color.gray.opacity(0.08)
                } else {
                    Color.clear
                }
            }
        }
        .buttonStyle(.plain)
        .onHover { isHovered = $0 }
    }
}

#Preview {
    ForwardChatPickerView(messageID: 42, sourceChatID: "src-chat")
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
