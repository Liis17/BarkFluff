import { useCallback, useEffect, useRef, useState } from 'react';
import { listMessages } from '../../api/services/messages';
import { useChatStore } from '../../state/chatStore';

// Загрузка истории сообщений чата + пагинация вверх.
// Семантика ListMessages (порт main.js): начальная — from=firstUnread||0, before=30, after=10;
// догрузка старых — from=oldestId, before=30, after=0.
export function useMessages(chatId: string) {
  const messages = useChatStore((s) => s.messagesByChat[chatId]);
  const setMessages = useChatStore((s) => s.setMessages);
  const mergeMessages = useChatStore((s) => s.mergeMessages);
  const chats = useChatStore((s) => s.chats);

  const [loading, setLoading] = useState(false);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [hasMoreOlder, setHasMoreOlder] = useState(true);
  const loadedFor = useRef<string | null>(null);

  useEffect(() => {
    if (loadedFor.current === chatId) return;
    loadedFor.current = chatId;
    setHasMoreOlder(true);
    let cancelled = false;
    setLoading(true);
    const chat = chats.find((c) => c.id === chatId);
    const fromId = chat?.firstUnreadMessageId ?? 0n;
    listMessages(chatId, { fromMessageId: fromId, offsetBefore: 30, offsetAfter: 10 })
      .then((msgs) => {
        if (!cancelled) setMessages(chatId, msgs);
      })
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chatId]);

  const loadOlder = useCallback(async () => {
    const current = useChatStore.getState().messagesByChat[chatId] ?? [];
    if (current.length === 0 || loadingOlder || !hasMoreOlder) return;
    setLoadingOlder(true);
    try {
      const oldestId = current[0].id;
      const older = await listMessages(chatId, { fromMessageId: oldestId, offsetBefore: 30, offsetAfter: 0 });
      const fresh = older.filter((m) => !current.some((c) => c.id === m.id));
      if (fresh.length === 0) setHasMoreOlder(false);
      else mergeMessages(chatId, fresh);
    } finally {
      setLoadingOlder(false);
    }
  }, [chatId, loadingOlder, hasMoreOlder, mergeMessages]);

  return { messages: messages ?? [], loading, loadingOlder, hasMoreOlder, loadOlder };
}
