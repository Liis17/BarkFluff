import { useEffect } from 'react';
import { useChatStore } from '../../state/chatStore';
import { listPinnedMessages, unpinAll as apiUnpinAll } from '../../api/services/messages';
import { IconButton } from '../../components/IconButton';
import './PinnedBar.css';

export function PinnedBar({ chatId }: { chatId: string }) {
  const pinned = useChatStore((s) => s.pinnedByChat[chatId]);
  const setPinned = useChatStore((s) => s.setPinned);
  const clearPinned = useChatStore((s) => s.clearPinned);

  useEffect(() => {
    let cancelled = false;
    listPinnedMessages(chatId)
      .then((list) => {
        if (!cancelled) setPinned(chatId, list);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [chatId, setPinned]);

  if (!pinned || pinned.length === 0) return null;
  const top = pinned[0];
  const text = top.message?.content?.text || 'Вложение';

  async function onUnpinAll() {
    await apiUnpinAll(chatId);
    clearPinned(chatId);
  }

  return (
    <div className="bf-pinbar">
      <span className="material-symbols-rounded bf-pinbar__icon">push_pin</span>
      <div className="bf-pinbar__body">
        <span className="bf-pinbar__title">
          Закреплённые{pinned.length > 1 ? ` · ${pinned.length}` : ''}
        </span>
        <span className="bf-pinbar__text">{text}</span>
      </div>
      <IconButton icon="close" onClick={() => void onUnpinAll()} aria-label="Открепить все" />
    </div>
  );
}
