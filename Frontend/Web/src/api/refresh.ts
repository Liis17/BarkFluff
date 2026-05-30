// Обновление access-токена по refresh-токену (single-flight) + получение валидного токена.
// Порт логики refreshToken/getValidToken из wwwroot/js/app/clients.js.
import type { Timestamp } from '@bufbuild/protobuf';
import { CreateTokenRequest } from '../gen/identity_api_pb';
import { rawIdentityClient } from './baseTransport';
import { buildHeaders } from './metadata';
import { tokenStore } from './tokenStore';

// Событие потери авторизации — AuthContext слушает его, чистит состояние и уводит на /login.
export const AUTH_LOST_EVENT = 'bf:auth-lost';
function emitAuthLost(): void {
  window.dispatchEvent(new Event(AUTH_LOST_EVENT));
}

export function timestampToMs(ts: Timestamp | undefined): number {
  return ts ? Number(ts.seconds) * 1000 + Math.floor(ts.nanos / 1e6) : 0;
}

let refreshPromise: Promise<string | null> | null = null;

export function refreshToken(): Promise<string | null> {
  if (refreshPromise) return refreshPromise;

  const rt = tokenStore.getRefreshToken();
  if (!rt) {
    tokenStore.clear();
    emitAuthLost();
    return Promise.resolve(null);
  }

  refreshPromise = (async () => {
    try {
      const resp = await rawIdentityClient.createToken(
        new CreateTokenRequest({ refreshToken: rt }),
        { headers: buildHeaders() },
      );
      const at = resp.accessToken;
      if (!at?.value) {
        tokenStore.clear();
        emitAuthLost();
        return null;
      }
      const stored = tokenStore.get();
      if (!stored) return null;
      stored.accessToken = at.value;
      stored.accessTokenExpiration = timestampToMs(at.expirationDate) || Date.now() + 3_600_000;
      tokenStore.save(stored);
      return at.value;
    } catch {
      tokenStore.clear();
      emitAuthLost();
      return null;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

// Валидный access-токен: текущий, если не истёк, иначе через refresh.
export function getValidToken(): Promise<string | null> {
  if (!tokenStore.isAccessExpired()) {
    return Promise.resolve(tokenStore.getAccessToken());
  }
  return refreshToken();
}
