import { useEffect, useReducer } from 'react';
import { getTempDownloadUrls, type FileUrl } from '../api/services/files';

// Кэш временных URL файлов (батч-запрос недостающих). Порт urlCache из wwwroot/js/app/files.js.
const cache = new Map<string, FileUrl>();

export function useFileUrls(fileIds: string[]): Record<string, FileUrl> {
  const [, force] = useReducer((x: number) => x + 1, 0);
  const key = fileIds.join(',');

  useEffect(() => {
    const missing = fileIds.filter((id) => id && !cache.has(id));
    if (missing.length === 0) return;
    let cancelled = false;
    getTempDownloadUrls(missing)
      .then((urls) => {
        urls.forEach((u) => cache.set(u.fileId, u));
        if (!cancelled) force();
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  const out: Record<string, FileUrl> = {};
  for (const id of fileIds) {
    const u = cache.get(id);
    if (u) out[id] = u;
  }
  return out;
}
