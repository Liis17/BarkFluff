import { create } from 'zustand';
import type { Chat } from '../gen/messages_api_pb';
import type { Message, PinnedMessageInfo } from '../gen/shared_pb';

// Сортировка по возрастанию id (bigint) + дедупликация.
function mergeMessages(existing: Message[], incoming: Message[]): Message[] {
  const byId = new Map<string, Message>();
  for (const m of existing) byId.set(m.id.toString(), m);
  for (const m of incoming) byId.set(m.id.toString(), m);
  return [...byId.values()].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}

interface ChatState {
  chats: Chat[];
  chatsLoaded: boolean;
  messagesByChat: Record<string, Message[]>;
  pinnedByChat: Record<string, PinnedMessageInfo[]>;
  activeChatId: string | null;

  setChats: (chats: Chat[]) => void;
  setActiveChat: (id: string | null) => void;
  setMessages: (chatId: string, messages: Message[]) => void;
  mergeMessages: (chatId: string, messages: Message[]) => void;
  upsertMessage: (chatId: string, message: Message) => void;
  removeMessage: (chatId: string, messageId: bigint) => void;

  // Обновляет lastMessage чата и поднимает его наверх; опц. инкремент непрочитанных.
  bumpChatLastMessage: (chatId: string, message: Message, incUnread?: boolean) => void;
  resetUnread: (chatId: string) => void;

  setPinned: (chatId: string, pinned: PinnedMessageInfo[]) => void;
  addPinned: (chatId: string, info: PinnedMessageInfo) => void;
  removePinned: (chatId: string, messageId: bigint) => void;
  clearPinned: (chatId: string) => void;
}

export const useChatStore = create<ChatState>((set) => ({
  chats: [],
  chatsLoaded: false,
  messagesByChat: {},
  pinnedByChat: {},
  activeChatId: null,

  setChats: (chats) => set({ chats, chatsLoaded: true }),
  setActiveChat: (id) => set({ activeChatId: id }),

  setMessages: (chatId, messages) =>
    set((s) => ({ messagesByChat: { ...s.messagesByChat, [chatId]: mergeMessages([], messages) } })),

  mergeMessages: (chatId, messages) =>
    set((s) => ({
      messagesByChat: {
        ...s.messagesByChat,
        [chatId]: mergeMessages(s.messagesByChat[chatId] ?? [], messages),
      },
    })),

  upsertMessage: (chatId, message) =>
    set((s) => ({
      messagesByChat: {
        ...s.messagesByChat,
        [chatId]: mergeMessages(s.messagesByChat[chatId] ?? [], [message]),
      },
    })),

  removeMessage: (chatId, messageId) =>
    set((s) => ({
      messagesByChat: {
        ...s.messagesByChat,
        [chatId]: (s.messagesByChat[chatId] ?? []).filter((m) => m.id !== messageId),
      },
    })),

  bumpChatLastMessage: (chatId, message, incUnread = false) =>
    set((s) => {
      const idx = s.chats.findIndex((c) => c.id === chatId);
      if (idx === -1) return {};
      const chat = s.chats[idx];
      const updated = chat.clone();
      updated.lastMessage = message;
      if (incUnread) updated.countUnread = updated.countUnread + 1n;
      const rest = s.chats.filter((_, i) => i !== idx);
      return { chats: [updated, ...rest] };
    }),

  resetUnread: (chatId) =>
    set((s) => ({
      chats: s.chats.map((c) => {
        if (c.id !== chatId || c.countUnread === 0n) return c;
        const u = c.clone();
        u.countUnread = 0n;
        return u;
      }),
    })),

  setPinned: (chatId, pinned) =>
    set((s) => ({ pinnedByChat: { ...s.pinnedByChat, [chatId]: pinned } })),

  addPinned: (chatId, info) =>
    set((s) => {
      const list = s.pinnedByChat[chatId] ?? [];
      const mid = info.message?.id;
      if (mid != null && list.some((p) => p.message?.id === mid)) return {};
      return { pinnedByChat: { ...s.pinnedByChat, [chatId]: [info, ...list] } };
    }),

  removePinned: (chatId, messageId) =>
    set((s) => ({
      pinnedByChat: {
        ...s.pinnedByChat,
        [chatId]: (s.pinnedByChat[chatId] ?? []).filter((p) => p.message?.id !== messageId),
      },
    })),

  clearPinned: (chatId) =>
    set((s) => ({ pinnedByChat: { ...s.pinnedByChat, [chatId]: [] } })),
}));
