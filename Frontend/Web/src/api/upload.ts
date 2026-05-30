// Загрузка файла: резерв слота (gRPC GetUploadUrl) → REST POST. Порт wwwroot/js/app/files.js.
import { UploadFileType } from '../gen/files_api_pb';
import { getUploadUrl } from './services/files';

export async function uploadFile(file: File, fileType: UploadFileType): Promise<string> {
  const { fileId } = await getUploadUrl(fileType);
  if (!fileId) throw new Error('no_upload_url');

  const form = new FormData();
  form.append('file', file, file.name);

  const resp = await fetch(`/api/files/upload/${fileId}`, { method: 'POST', body: form });
  if (!resp.ok) throw new Error(`upload_failed_${resp.status}`);
  const body = (await resp.json()) as { fileId: string };
  return body.fileId;
}

// MIME/расширение → UploadFileType (для вложений сообщений).
export function uploadTypeForFile(file: File): UploadFileType {
  const mime = file.type || '';
  if (mime.startsWith('image/gif')) return UploadFileType.MESSAGE_ATTACHMENT_GIF;
  if (mime.startsWith('image/')) return UploadFileType.MESSAGE_ATTACHMENT_IMAGE;
  if (mime.startsWith('video/')) return UploadFileType.MESSAGE_ATTACHMENT_VIDEO;
  if (mime.startsWith('audio/')) return UploadFileType.MESSAGE_ATTACHMENT_AUDIO;

  const ext = (file.name.split('.').pop() ?? '').toLowerCase();
  if (ext === 'gif') return UploadFileType.MESSAGE_ATTACHMENT_GIF;
  if (['jpg', 'jpeg', 'png', 'webp', 'bmp', 'avif', 'heic', 'heif', 'tiff', 'tif', 'svg', 'ico'].includes(ext))
    return UploadFileType.MESSAGE_ATTACHMENT_IMAGE;
  if (['mp4', 'mov', 'avi', 'mkv', 'webm', 'm4v'].includes(ext)) return UploadFileType.MESSAGE_ATTACHMENT_VIDEO;
  if (['mp3', 'ogg', 'wav', 'aac', 'flac', 'm4a'].includes(ext)) return UploadFileType.MESSAGE_ATTACHMENT_AUDIO;
  return UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT;
}
