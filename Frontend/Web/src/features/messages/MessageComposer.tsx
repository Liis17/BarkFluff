import { useRef, useState, type KeyboardEvent } from 'react';
import { IconButton } from '../../components/IconButton';
import { sendMessage } from '../../api/services/messages';
import { uploadFile, uploadTypeForFile } from '../../api/upload';
import { useChatStore } from '../../state/chatStore';
import { formatBytes } from '../../utils/format';
import './MessageComposer.css';

export function MessageComposer({ chatId }: { chatId: string }) {
  const [text, setText] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [sending, setSending] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const textRef = useRef<HTMLTextAreaElement>(null);
  const upsertMessage = useChatStore((s) => s.upsertMessage);
  const bump = useChatStore((s) => s.bumpChatLastMessage);

  const canSend = (text.trim().length > 0 || files.length > 0) && !sending;

  async function send() {
    if (!canSend) return;
    setSending(true);
    try {
      const fileIds = await Promise.all(files.map((f) => uploadFile(f, uploadTypeForFile(f))));
      const msg = await sendMessage(chatId, text.trim(), fileIds);
      if (msg) {
        upsertMessage(chatId, msg);
        bump(chatId, msg);
      }
      setText('');
      setFiles([]);
      if (textRef.current) textRef.current.style.height = 'auto';
    } finally {
      setSending(false);
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void send();
    }
  }

  return (
    <div className="bf-composer">
      {files.length > 0 && (
        <div className="bf-composer__files">
          {files.map((f, i) => (
            <div className="bf-composer__chip" key={`${f.name}-${i}`}>
              <span className="material-symbols-rounded">attach_file</span>
              <span className="bf-composer__chipname">{f.name}</span>
              <span className="bf-composer__chipsize">{formatBytes(f.size)}</span>
              <button
                className="material-symbols-rounded bf-composer__chipx"
                onClick={() => setFiles((fs) => fs.filter((_, j) => j !== i))}
                aria-label="Убрать"
              >
                close
              </button>
            </div>
          ))}
        </div>
      )}
      <div className="bf-composer__row">
        <IconButton icon="attach_file" onClick={() => fileInput.current?.click()} aria-label="Прикрепить" />
        <input
          ref={fileInput}
          type="file"
          multiple
          hidden
          onChange={(e) => {
            const list = Array.from(e.target.files ?? []);
            if (list.length) setFiles((fs) => [...fs, ...list]);
            e.target.value = '';
          }}
        />
        <textarea
          ref={textRef}
          className="bf-composer__input"
          placeholder="Сообщение…"
          value={text}
          rows={1}
          onKeyDown={onKeyDown}
          onChange={(e) => {
            setText(e.target.value);
            e.target.style.height = 'auto';
            e.target.style.height = `${Math.min(e.target.scrollHeight, 160)}px`;
          }}
        />
        <IconButton
          icon={sending ? 'hourglass_empty' : 'send'}
          filled
          onClick={() => void send()}
          disabled={!canSend}
          aria-label="Отправить"
          className="bf-composer__send"
        />
      </div>
    </div>
  );
}
