// Тонкие обёртки над FilesApi (unary). Загрузка файла — REST, см. api/upload.ts.
import { filesClient } from '../transport';
import {
  GetTempDownloadUrlRequest,
  GetUploadUrlRequest,
  GetUserStorageInfoRequest,
} from '../../gen/files_api_pb';
import type { UploadFileType } from '../../gen/files_api_pb';

export interface FileUrl {
  fileId: string;
  url: string;
  previewUrl: string;
}

export async function getTempDownloadUrls(fileIds: string[]): Promise<FileUrl[]> {
  if (fileIds.length === 0) return [];
  const resp = await filesClient.getTempDownloadUrl(new GetTempDownloadUrlRequest({ fileIds }));
  return resp.fileUrls.map((f) => ({ fileId: f.fileId, url: f.url, previewUrl: f.previewUrl }));
}

// Шаг 1 загрузки: получить fileId + url (далее REST POST на /api/files/upload/{fileId}).
export async function getUploadUrl(fileType: UploadFileType): Promise<{ url: string; fileId: string }> {
  const resp = await filesClient.getUploadUrl(new GetUploadUrlRequest({ fileType }));
  return { url: resp.url, fileId: resp.fileId };
}

export async function getUserStorageInfo() {
  const resp = await filesClient.getUserStorageInfo(new GetUserStorageInfoRequest({}));
  return {
    totalUsed: resp.totalUsedStorage,
    limit: resp.storageLimit,
    byType: resp.storageByTypes,
  };
}
