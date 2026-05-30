import { useState, type KeyboardEvent } from 'react';
import type { Message } from '../../gen/shared_pb';
import { useUser } from '../../hooks/useUsers';
import { useChatStore } from '../../state/chatStore';
import { formatTime } from '../../utils/format';
import {
  deleteMessage as apiDelete,
  editMessage as apiEdit,
  pinMessage as apiPin,
  unpinMessage as apiUnpin,
} from '../../api/services/messages';
import { Attachments } from './Attachments';
import './MessageBubble.css';

interface MessageBubbleProps {
  chatId: string;
  message: Message;
  isOwn: boolean;
  /** показывать имя отправителя (групповой чат, входящее) */
  showSender: boolean;
}

export function MessageBubble({ chatId, message, isOwn, showSender }: MessageBubbleProps) {
  const sender = useUser(showSender ? message.senderId : null);
  const upsertMessage = useChatStore((s) => s.upsertMessage);
  const removeMessage = useChatStore((s) => s.removeMessage);
  const addPinned = useChatStore((s) => s.addPinned);
  const removePinned = useChatStore((s) => s.removePinned);
  const isPinned = useChatStore((s) =>
    (s.pinnedByChat[chatId] ?? []).some((p) => p.message?.id === message.id),
  );

  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');

  const text = message.content?.text ?? '';
  const attachments = message.content?.attachments ?? [];
  const readByOther = message.readBy.some((id) => id !== message.senderId);

  async function saveEdit() {
    const newText = draft.trim();
    if (!newText) return;
    const filesIds = attachments.map((a) => a.fileId).filter(Boolean);
    const updated = await apiEdit(message.id, newText, filesIds);
    if (updated) upsertMessage(chatId, updated);
    setEditing(false);
  }

  async function onDelete() {
    if (!window.confirm('Удалить сообщение?')) return;
    await apiDelete(message.id);
    removeMessage(chatId, message.id);
  }

  async function onTogglePin() {
    if (isPinned) {
      await apiUnpin(chatId, message.id);
      removePinned(chatId, message.id);
    } else {
      const info = await apiPin(chatId, message.id);
      if (info) addPinned(chatId, info);
    }
  }

  function onEditKey(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void saveEdit();
    } else if (e.key === 'Escape') {
      setEditing(false);
    }
  }

  return (
    <div className={`bf-bubble ${isOwn ? 'bf-bubble--out' : 'bf-bubble--in'}`}>
      <div className="bf-bubble__box">
        {showSender && sender && (
          <span className="bf-bubble__sender">
            {`${sender.firstName} ${sender.lastName}`.trim() || sender.username}
          </span>
        )}
        {attachments.length > 0 && <Attachments attachments={attachments} />}

        {editing ? (
          <div className="bf-bubble__edit">
            <textarea
              className="bf-bubble__editinput"
              value={draft}
              autoFocus
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={onEditKey}
              rows={2}
            />
            <div className="bf-bubble__editactions">
              <button onClick={() => setEditing(false)}>Отмена</button>
              <button onClick={() => void saveEdit()}>Сохранить</button>
            </div>
          </div>
        ) : (
          text && <span className="bf-bubble__text">{text}</span>
        )}

        <span className="bf-bubble__meta">
          {isPinned && <span className="material-symbols-rounded bf-bubble__pinmark">push_pin</span>}
          {message.isEdited && <span className="bf-bubble__edited">изм.</span>}
          <span className="bf-bubble__time">{formatTime(message.sentAt)}</span>
          {isOwn && (
            <span className="material-symbols-rounded bf-bubble__check">
              {readByOther ? 'done_all' : 'done'}
            </span>
          )}
        </span>
      </div>

      {!editing && (
        <div className="bf-bubble__actions">
          <button
            className="material-symbols-rounded"
            title={isPinned ? 'Открепить' : 'Закрепить'}
            onClick={() => void onTogglePin()}
          >
            push_pin
          </button>
          {isOwn && (
            <button
              className="material-symbols-rounded"
              title="Редактировать"
              onClick={() => {
                setDraft(text);
                setEditing(true);
              }}
            >
              edit
            </button>
          )}
          {isOwn && (
            <button className="material-symbols-rounded" title="Удалить" onClick={() => void onDelete()}>
              delete
            </button>
          )}
        </div>
      )}
    </div>
  );
}
