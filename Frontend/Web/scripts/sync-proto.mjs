// Синхронизирует proto-файлы из общего источника истины Shared/BarkFluff.Proto
// в локальную папку proto/ (buf v2 не разрешает module-path с '..', а прямой
// input на Shared подхватывает дубли из bin/obj). Запуск: npm run sync-proto
import { copyFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const src = join(here, '..', '..', '..', 'Shared', 'BarkFluff.Proto');
const dst = join(here, '..', 'proto');

// Только то, что нужно веб-клиенту (MVP + задел).
const files = [
  'shared.proto',
  'identity_api.proto',
  'users_api.proto',
  'messages_api.proto',
  'updates_api.proto',
  'files_api.proto',
];

mkdirSync(dst, { recursive: true });
for (const f of files) {
  copyFileSync(join(src, f), join(dst, f));
  console.log(`synced ${f}`);
}
