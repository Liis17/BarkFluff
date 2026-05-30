import type { Timestamp } from '@bufbuild/protobuf';

export function tsToDate(ts?: Timestamp): Date | null {
  if (!ts) return null;
  return new Date(Number(ts.seconds) * 1000 + Math.floor(ts.nanos / 1e6));
}

// Время сообщения: ЧЧ:ММ.
export function formatTime(ts?: Timestamp): string {
  const d = tsToDate(ts);
  if (!d) return '';
  return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
}

// Метка для списка чатов: время сегодня, иначе дата.
export function formatChatTime(ts?: Timestamp): string {
  const d = tsToDate(ts);
  if (!d) return '';
  const now = new Date();
  const sameDay = d.toDateString() === now.toDateString();
  if (sameDay) return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
  const sameYear = d.getFullYear() === now.getFullYear();
  return d.toLocaleDateString('ru-RU', sameYear ? { day: '2-digit', month: '2-digit' } : { day: '2-digit', month: '2-digit', year: '2-digit' });
}

// Разделитель дат в ленте сообщений.
export function formatDateSeparator(ts?: Timestamp): string {
  const d = tsToDate(ts);
  if (!d) return '';
  return d.toLocaleDateString('ru-RU', { day: 'numeric', month: 'long' });
}

export function dayKey(ts?: Timestamp): string {
  const d = tsToDate(ts);
  return d ? d.toDateString() : '';
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[1][0]).toUpperCase();
}

export function formatBytes(bytes: number | bigint): string {
  const n = typeof bytes === 'bigint' ? Number(bytes) : bytes;
  if (n < 1024) return `${n} Б`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} КБ`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} МБ`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} ГБ`;
}
