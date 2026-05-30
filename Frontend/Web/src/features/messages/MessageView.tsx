import { useEffect, useLayoutEffect, useRef } from 'react';
import { useChatStore } from '../../state/chatStore';
import { useAuth } from '../../state/AuthContext';
import { Avatar } from '../../components/Avatar';
import { useChatDisplay } from '../chats/useChatDisplay';
import { dayKey, formatDateSeparator } from '../../utils/format';
import { markAsRead } from '../../api/services/messages';
import { MessageBubble } from './MessageBubble';
import { MessageComposer } from './MessageComposer';
import { PinnedBar } from './PinnedBar';
import { useMessages } from './useMessages';
import './MessageView.css';

export function MessageView({ chatId }: { chatId: string }) {
  const { currentUserId } = useAuth();
  const chat = useChatStore((s) => s.chats.find((c) => c.id === chatId));
  const resetUnread = useChatStore((s) => s.resetUnread);
  const { name, picture } = useChatDisplay(chat);
  const { messages, loading, loadingOlder, hasMoreOlder, loadOlder } = useMessages(chatId);

  const scrollRef = useRef<HTMLDivElement>(null);
  const prevHeightRef = useRef(0);
  const didInitialScroll = useRef(false);

  // Сброс флага автоскролла при смене чата.
  useEffect(() => {
    didInitialScroll.current = false;
  }, [chatId]);

  // Автоскролл вниз при первой загрузке сообщений.
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    if (!didInitialScroll.current && messages.length > 0) {
      el.scrollTop = el.scrollHeight;
      didInitialScroll.current = true;
    } else if (prevHeightRef.current > 0) {
      // Сохраняем позицию после подгрузки старых.
      el.scrollTop = el.scrollHeight - prevHeightRef.current;
      prevHeightRef.current = 0;
    }
  }, [messages]);

  const onScroll = () => {
    const el = scrollRef.current;
    if (!el || loadingOlder || !hasMoreOlder) return;
    if (el.scrollTop < 100 && messages.length > 0) {
      prevHeightRef.current = el.scrollHeight;
      void loadOlder();
    }
  };

  // Отмечаем входящие непрочитанные как прочитанные.
  useEffect(() => {
    if (currentUserId == null || messages.length === 0) return;
    const unread = messages
      .filter((m) => m.senderId.toString() !== currentUserId && !m.readBy.some((id) => id.toString() === currentUserId))
      .map((m) => m.id);
    if (unread.length === 0) return;
    markAsRead(unread)
      .then(() => resetUnread(chatId))
      .catch(() => {});
  }, [messages, currentUserId, chatId, resetUnread]);

  let lastDay = '';

  return (
    <section className="bf-msgview">
      <header className="bf-msgview__header">
        <Avatar name={name} src={picture} size={40} />
        <div className="bf-msgview__headinfo">
          <span className="bf-msgview__name">{name}</span>
          {chat?.isGroupChat && (
            <span className="bf-msgview__sub">{chat.members.length} участников</span>
          )}
        </div>
      </header>

      <PinnedBar chatId={chatId} />

      <div className="bf-msgview__scroll" ref={scrollRef} onScroll={onScroll}>
        {loadingOlder && <div className="bf-msgview__loader">Загрузка…</div>}
        {loading && messages.length === 0 && <div className="bf-msgview__loader">Загрузка…</div>}
        {!loading && messages.length === 0 && (
          <div className="bf-msgview__empty">Нет сообщений</div>
        )}
        {messages.map((m) => {
          const isOwn = currentUserId != null && m.senderId.toString() === currentUserId;
          const day = dayKey(m.sentAt);
          const showDay = day !== lastDay;
          lastDay = day;
          return (
            <div key={m.id.toString()}>
              {showDay && <div className="bf-msgview__daysep">{formatDateSeparator(m.sentAt)}</div>}
              <MessageBubble
                chatId={chatId}
                message={m}
                isOwn={isOwn}
                showSender={!!chat?.isGroupChat && !isOwn}
              />
            </div>
          );
        })}
      </div>

      <MessageComposer chatId={chatId} />
    </section>
  );
}
