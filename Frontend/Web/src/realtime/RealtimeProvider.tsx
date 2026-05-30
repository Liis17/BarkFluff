import { useEffect, type ReactNode } from 'react';
import { createClient } from '@connectrpc/connect';
import { UpdatesApi } from '../gen/updates_api_connect';
import { baseTransport } from '../api/baseTransport';
import { useAuth } from '../state/AuthContext';
import { useChatStore } from '../state/chatStore';
import { listMessages, listPinnedMessages } from '../api/services/messages';
import { runStream, type StreamHandle } from './streams';

const updatesClient = createClient(UpdatesApi, baseTransport);

// Дозагрузка новых сообщений активного чата после переоткрытия стрима
// (server-streaming не реплеит пропущенное — инвариант из realtime.js).
async function resyncActiveChat() {
  const st = useChatStore.getState();
  const chatId = st.activeChatId;
  if (!chatId) return;
  const msgs = st.messagesByChat[chatId] ?? [];
  const lastId = msgs.length ? msgs[msgs.length - 1].id : 0n;
  try {
    const newer = await listMessages(chatId, { fromMessageId: lastId, offsetAfter: 30, offsetBefore: 0 });
    if (newer.length) useChatStore.getState().mergeMessages(chatId, newer);
  } catch {
    /* ignore */
  }
}

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { isAuthed, currentUserId } = useAuth();

  useEffect(() => {
    if (!isAuthed) return;
    const store = useChatStore.getState;
    const handles: StreamHandle[] = [];

    // Новое сообщение: добавить, поднять чат, инкремент непрочитанных (если не активный/чужое).
    handles.push(
      runStream({
        label: 'new-messages',
        open: (_req, o) => updatesClient.subscribeNewMessages({}, o),
        onReopen: () => void resyncActiveChat(),
        onEvent: (e) => {
          if (!e.message) return;
          const s = store();
          s.upsertMessage(e.chatId, e.message);
          const isOwn = currentUserId != null && e.message.senderId.toString() === currentUserId;
          const incUnread = !isOwn && s.activeChatId !== e.chatId;
          s.bumpChatLastMessage(e.chatId, e.message, incUnread);
        },
      }),
    );

    // Прочитано: обновить read_by у сообщения.
    handles.push(
      runStream({
        label: 'read',
        open: (_req, o) => updatesClient.subscribeMessagesRead({}, o),
        onEvent: (e) => {
          const s = store();
          const target = (s.messagesByChat[e.chatId] ?? []).find((m) => m.id === e.messageId);
          if (target) {
            const c = target.clone();
            c.readBy = e.newReadBy;
            s.upsertMessage(e.chatId, c);
          }
        },
      }),
    );

    // Отредактировано.
    handles.push(
      runStream({
        label: 'edited',
        open: (_req, o) => updatesClient.subscribeMessagesEdited({}, o),
        onEvent: (e) => {
          if (e.message) store().upsertMessage(e.chatId, e.message);
        },
      }),
    );

    // Удалено.
    handles.push(
      runStream({
        label: 'deleted',
        open: (_req, o) => updatesClient.subscribeMessagesDeleted({}, o),
        onEvent: (e) => store().removeMessage(e.chatId, e.messageId),
      }),
    );

    // Закреп/откреп: перезагружаем список закреплённых, если чат был открыт.
    const reloadPinnedIfLoaded = (chatId: string) => {
      if (store().pinnedByChat[chatId] === undefined) return;
      listPinnedMessages(chatId)
        .then((list) => store().setPinned(chatId, list))
        .catch(() => {});
    };
    handles.push(
      runStream({
        label: 'pinned',
        open: (_req, o) => updatesClient.subscribeMessagesPinned({}, o),
        onEvent: (e) => reloadPinnedIfLoaded(e.chatId),
      }),
    );
    handles.push(
      runStream({
        label: 'unpinned',
        open: (_req, o) => updatesClient.subscribeMessagesUnpinned({}, o),
        onEvent: (e) => store().removePinned(e.chatId, e.messageId),
      }),
    );
    handles.push(
      runStream({
        label: 'all-unpinned',
        open: (_req, o) => updatesClient.subscribeAllMessagesUnpinned({}, o),
        onEvent: (e) => store().clearPinned(e.chatId),
      }),
    );

    // При возврате вкладки в фокус — дозагружаем активный чат (мог пропустить события).
    const onVisible = () => {
      if (document.visibilityState === 'visible') void resyncActiveChat();
    };
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      document.removeEventListener('visibilitychange', onVisible);
      handles.forEach((h) => h.stop());
    };
  }, [isAuthed, currentUserId]);

  return <>{children}</>;
}
