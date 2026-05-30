import { useEffect, useState } from 'react';
import { listChats } from '../../api/services/messages';
import { useChatStore } from '../../state/chatStore';

// Загружает список чатов в стор (один раз при монтировании).
export function useChats() {
  const chats = useChatStore((s) => s.chats);
  const chatsLoaded = useChatStore((s) => s.chatsLoaded);
  const setChats = useChatStore((s) => s.setChats);
  const [loading, setLoading] = useState(!chatsLoaded);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    listChats()
      .then((r) => {
        if (!cancelled) setChats(r.chats);
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Не удалось загрузить чаты');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [setChats]);

  return { chats, loading, error };
}
