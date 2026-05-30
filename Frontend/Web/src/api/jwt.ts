// Декодирование payload JWT (без верификации — только чтобы достать userId на клиенте).
interface JwtPayload {
  [claim: string]: unknown;
}

function decodePayload(token: string): JwtPayload | null {
  const parts = token.split('.');
  if (parts.length < 2) return null;
  try {
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const json = decodeURIComponent(
      atob(b64)
        .split('')
        .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );
    return JSON.parse(json) as JwtPayload;
  } catch {
    return null;
  }
}

const ID_CLAIMS = [
  'sub',
  'userId',
  'user_id',
  'nameid',
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier',
];

// Достаёт идентификатор пользователя из access-токена.
export function userIdFromToken(token: string | null | undefined): string | null {
  if (!token) return null;
  const payload = decodePayload(token);
  if (!payload) return null;
  for (const claim of ID_CLAIMS) {
    const v = payload[claim];
    if (typeof v === 'string' && v) return v;
    if (typeof v === 'number') return String(v);
  }
  return null;
}
