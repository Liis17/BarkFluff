import { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { useChatStore } from '../../state/chatStore';
import { ChatList } from './ChatList';
import { MessageView } from '../messages/MessageView';
import './ChatsPage.css';

export function ChatsPage() {
  const { chatId } = useParams();
  const setActiveChat = useChatStore((s) => s.setActiveChat);

  useEffect(() => {
    setActiveChat(chatId ?? null);
    return () => setActiveChat(null);
  }, [chatId, setActiveChat]);

  return (
    <div className={`bf-chats ${chatId ? 'bf-chats--detail' : ''}`}>
      <ChatList />
      <div className="bf-chats__detail">
        {chatId ? (
          <MessageView key={chatId} chatId={chatId} />
        ) : (
          <div className="bf-chats__placeholder">
            <span className="material-symbols-rounded">forum</span>
            <p>Выберите чат, чтобы начать общение</p>
          </div>
        )}
      </div>
    </div>
  );
}
