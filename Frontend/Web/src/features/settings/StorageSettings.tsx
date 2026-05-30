import { useEffect, useState } from 'react';
import { getUserStorageInfo } from '../../api/services/files';
import { formatBytes } from '../../utils/format';
import { UploadFileType, type GetUserStorageInfoResponse_StorageByType } from '../../gen/files_api_pb';

const TYPE_LABEL: Partial<Record<UploadFileType, string>> = {
  [UploadFileType.USER_AVATAR]: 'Аватары',
  [UploadFileType.MESSAGE_ATTACHMENT_IMAGE]: 'Изображения',
  [UploadFileType.MESSAGE_ATTACHMENT_VIDEO]: 'Видео',
  [UploadFileType.MESSAGE_ATTACHMENT_GIF]: 'GIF',
  [UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT]: 'Документы',
  [UploadFileType.MESSAGE_ATTACHMENT_AUDIO]: 'Аудио',
  [UploadFileType.MESSAGE_ATTACHMENT_VOICE]: 'Голосовые',
  [UploadFileType.MESSAGE_ATTACHMENT_STICKER]: 'Стикеры',
  [UploadFileType.CHAT_PICTURE]: 'Картинки чатов',
  [UploadFileType.USER_PROFILE_POSTER]: 'Постеры профиля',
};

export function StorageSettings() {
  const [info, setInfo] = useState<{ used: bigint; limit: bigint; byType: GetUserStorageInfoResponse_StorageByType[] } | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    getUserStorageInfo()
      .then((r) => setInfo({ used: r.totalUsed, limit: r.limit, byType: r.byType }))
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  const pct = info && info.limit > 0n ? Math.min(100, Number((info.used * 100n) / info.limit)) : 0;

  return (
    <div className="bf-setcard">
      <h2 className="bf-setcard__title">Хранилище</h2>
      {loading && <p className="bf-set-hint">Загрузка…</p>}
      {info && (
        <>
          <p className="bf-setrow__label">
            {formatBytes(info.used)} из {formatBytes(info.limit)}
          </p>
          <div className="bf-storage__bar">
            <div className="bf-storage__fill" style={{ width: `${pct}%` }} />
          </div>
          {info.byType.map((t, i) => (
            <div className="bf-setrow" key={i}>
              <span className="bf-setrow__label">{TYPE_LABEL[t.fileType] ?? `Тип ${t.fileType}`}</span>
              <span className="bf-setrow__sub">{formatBytes(t.usedStorage)}</span>
            </div>
          ))}
        </>
      )}
    </div>
  );
}
