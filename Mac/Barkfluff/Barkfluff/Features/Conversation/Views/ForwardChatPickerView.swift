//
//  ForwardChatPickerView.swift
//  Barkfluff
//
//  Sheet выбора чата для пересылки сообщения
//

import SwiftUI
import BFCore

/// Sheet выбора целевого чата при пересылке сообщения.
struct ForwardChatPickerView: View {
    let messageID: Int64
    let sourceChatID: String

    @Environment(\.dismiss) private var dismiss
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: ForwardChatPickerViewModel?
    @State private var selectedChatID: String?

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
            .navigationTitle("Переслать сообщение")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Отмена") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Переслать") { performForward() }
                        .disabled(selectedChatID == nil || (viewModel?.isSending ?? false))
                }
            }
        }
        .frame(minWidth: 380, minHeight: 480)
        .task {
            if viewModel == nil {
                viewModel = ForwardChatPickerViewModel(
                    allChats: coordinator.chatListViewModel?.chats ?? [],
                    messageService: container.messageService,
                    messageID: messageID,
                    sourceChatID: sourceChatID
                )
            }
        }
    }

    @ViewBuilder
    private func pickerContent(viewModel: ForwardChatPickerViewModel) -> some View {
        VStack(spacing: 0) {
            if viewModel.allChats.isEmpty {
                ContentUnavailableView(
                    "Нет чатов",
                    systemImage: "message",
                    description: Text("Сначала откройте любой чат, затем повторите попытку.")
                )
            } else {
                List(selection: $selectedChatID) {
                    ForEach(viewModel.filteredChats) { chat in
                        ChatRowView(
                            chat: chat,
                            currentUserID: container.currentUserID,
                            onlineStatusService: container.onlineStatusService
                        )
                        .tag(chat.id)
                    }
                }
                .listStyle(.inset)
                .searchable(
                    text: Bindable(viewModel).searchText,
                    placement: .toolbar,
                    prompt: "Поиск чата"
                )
            }

            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .padding(Theme.Spacing.sm)
            }

            if viewModel.isSending {
                ProgressView()
                    .padding(Theme.Spacing.sm)
            }
        }
    }

    private func performForward() {
        guard
            let viewModel,
            let targetID = selectedChatID,
            let target = viewModel.allChats.first(where: { $0.id == targetID })
        else { return }

        Task {
            let success = await viewModel.forward(to: target)
            if success {
                // lastMessage в sidebar обновится через real-time стрим NewMessages
                dismiss()
            }
        }
    }
}

#Preview {
    ForwardChatPickerView(messageID: 42, sourceChatID: "src-chat")
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
