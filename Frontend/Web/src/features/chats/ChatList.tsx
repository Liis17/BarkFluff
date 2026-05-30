import { NavLink } from 'react-router-dom';
import type { Chat } from '../../gen/messages_api_pb';
import { Avatar } from '../../components/Avatar';
import { formatChatTime } from '../../utils/format';
import { useChats } from './useChats';
import { useChatDisplay } from './useChatDisplay';
import './ChatList.css';

function lastMessagePreview(chat: Chat): string {
  const m = chat.lastMessage;
  if (!m) return 'Нет сообщений';
  const text = m.content?.text?.trim();
  if (text) return text;
  const att = m.content?.attachments?.[0];
  if (att) return att.fileName || 'Вложение';
  return '';
}

function ChatListItem({ chat }: { chat: Chat }) {
  const { name, picture } = useChatDisplay(chat);
  const unread = Number(chat.countUnread);

  return (
    <NavLink
      to={`/chats/${chat.id}`}
      className={({ isActive }) => `bf-chatitem ${isActive ? 'is-active' : ''}`}
    >
      <Avatar name={name} src={picture} size={52} />
      <div className="bf-chatitem__body">
        <div className="bf-chatitem__row">
          <span className="bf-chatitem__name">{name}</span>
          <span className="bf-chatitem__time">{formatChatTime(chat.lastMessage?.sentAt)}</span>
        </div>
        <div className="bf-chatitem__row">
          <span className="bf-chatitem__preview">{lastMessagePreview(chat)}</span>
          {unread > 0 && <span className="bf-chatitem__badge">{unread > 99 ? '99+' : unread}</span>}
        </div>
      </div>
    </NavLink>
  );
}

export function ChatList() {
  const { chats, loading, error } = useChats();

  return (
    <aside className="bf-chatlist">
      <header className="bf-chatlist__header">
        <h1 className="bf-chatlist__title">Чаты</h1>
      </header>
      <div className="bf-chatlist__items">
        {loading && <p className="bf-chatlist__hint">Загрузка…</p>}
        {error && <p className="bf-chatlist__hint bf-chatlist__hint--error">{error}</p>}
        {!loading && !error && chats.length === 0 && (
          <p className="bf-chatlist__hint">Пока нет чатов</p>
        )}
        {chats.map((chat) => (
          <ChatListItem key={chat.id} chat={chat} />
        ))}
      </div>
    </aside>
  );
}
