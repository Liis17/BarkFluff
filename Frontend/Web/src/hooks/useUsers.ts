import { useEffect, useReducer } from 'react';
import { getUser } from '../api/services/users';
import type { User } from '../gen/users_api_pb';

// Кэш профилей пользователей (для имён/аватаров в ЛС).
const cache = new Map<string, User>();
const inflight = new Set<string>();

export function useUser(userId: bigint | string | null | undefined): User | undefined {
  const [, force] = useReducer((x: number) => x + 1, 0);
  const key = userId != null ? userId.toString() : '';

  useEffect(() => {
    if (!key || cache.has(key) || inflight.has(key)) return;
    inflight.add(key);
    let cancelled = false;
    getUser(key)
      .then((u) => {
        if (u) cache.set(key, u);
        if (!cancelled) force();
      })
      .catch(() => {})
      .finally(() => inflight.delete(key));
    return () => {
      cancelled = true;
    };
  }, [key]);

  return key ? cache.get(key) : undefined;
}
