import { MessageAttachmentType, type MessageAttachment } from '../../gen/shared_pb';
import { useFileUrls } from '../../hooks/useFileUrls';
import { formatBytes } from '../../utils/format';
import './Attachments.css';

const IMAGE_TYPES = new Set([
  MessageAttachmentType.IMAGE,
  MessageAttachmentType.GIF,
  MessageAttachmentType.STICKER,
]);

export function Attachments({ attachments }: { attachments: MessageAttachment[] }) {
  // Собираем все file_id (превью и оригинал) одним батчем.
  const ids = attachments.flatMap((a) => [a.fileId, a.previewFileId].filter(Boolean));
  const urls = useFileUrls(ids);
  if (attachments.length === 0) return null;

  return (
    <div className="bf-atts">
      {attachments.map((a) => {
        const main = urls[a.fileId];
        const preview = a.previewUrl || urls[a.previewFileId]?.url || main?.previewUrl;
        const isImage = IMAGE_TYPES.has(a.type) || a.imageWidth > 0;

        if (isImage) {
          const src = preview || main?.url;
          return (
            <a key={a.id.toString()} href={main?.url} target="_blank" rel="noreferrer" className="bf-atts__img">
              {src ? <img src={src} alt={a.fileName} loading="lazy" /> : <div className="bf-atts__ph" />}
            </a>
          );
        }

        return (
          <a
            key={a.id.toString()}
            href={main?.url}
            target="_blank"
            rel="noreferrer"
            className="bf-atts__file"
            download={a.fileName || undefined}
          >
            <span className="material-symbols-rounded">
              {a.type === MessageAttachmentType.VIDEO
                ? 'movie'
                : a.type === MessageAttachmentType.AUDIO || a.type === MessageAttachmentType.VOICE
                  ? 'audio_file'
                  : 'description'}
            </span>
            <span className="bf-atts__fileinfo">
              <span className="bf-atts__filename">{a.fileName || 'Файл'}</span>
              <span className="bf-atts__filesize">{formatBytes(a.attachmentSize)}</span>
            </span>
          </a>
        );
      })}
    </div>
  );
}
