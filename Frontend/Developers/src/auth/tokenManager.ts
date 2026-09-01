import { createClient } from '@connectrpc/connect';
import type { Interceptor } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { IdentityApi } from '../gen/identity_api_connect';
import { CreateTokenRequest } from '../gen/identity_api_pb';

export interface AuthState {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiration: number;
  refreshTokenExpiration: number;
}

export const AUTH_KEY = 'barkfluff_dev_auth';
export const AUTH_CHANGED_EVENT = 'barkfluff-auth-changed';

// Обновляем токен чуть раньше формального истечения, чтобы избежать гонки с сетевой задержкой запроса
const REFRESH_SKEW_MS = 30_000;

const refreshTransport = createGrpcWebTransport({ baseUrl: '/grpc' });
const refreshClient = createClient(IdentityApi, refreshTransport);

export function loadAuth(): AuthState | null {
  try {
    const raw = localStorage.getItem(AUTH_KEY);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    if (!isAuthState(parsed)) {
      clearStoredAuth();
      return null;
    }
    return parsed;
  } catch {
    clearStoredAuth();
    return null;
  }
}

export function saveAuth(auth: AuthState | null) {
  if (auth) {
    localStorage.setItem(AUTH_KEY, JSON.stringify(auth));
  } else {
    localStorage.removeItem(AUTH_KEY);
  }
  window.dispatchEvent(new CustomEvent<AuthState | null>(AUTH_CHANGED_EVENT, { detail: auth }));
}

let refreshInFlight: Promise<string | null> | null = null;

async function refreshAccessToken(auth: AuthState): Promise<string | null> {
  try {
    const resp = await refreshClient.createToken(
      new CreateTokenRequest({ refreshToken: auth.refreshToken }),
    );
    const accessToken = resp.accessToken?.value.trim();
    const accessTokenExpiration = timestampToMilliseconds(resp.accessToken?.expirationDate);
    if (!accessToken || accessTokenExpiration === null) {
      throw new Error('Identity service returned an incomplete access token');
    }

    const newAuth: AuthState = {
      ...auth,
      accessToken,
      accessTokenExpiration,
    };
    saveAuth(newAuth);
    return newAuth.accessToken;
  } catch {
    saveAuth(null);
    return null;
  }
}

// Возвращает валидный access token, при необходимости обновляя его по refresh token
export async function ensureValidAccessToken(): Promise<string | null> {
  const auth = loadAuth();
  if (!auth) return null;
  if (Date.now() < auth.accessTokenExpiration - REFRESH_SKEW_MS) {
    return auth.accessToken;
  }
  if (Date.now() >= auth.refreshTokenExpiration) {
    saveAuth(null);
    return null;
  }
  if (!refreshInFlight) {
    refreshInFlight = refreshAccessToken(auth).finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

export const authInterceptor: Interceptor = (next) => async (req) => {
  const token = await ensureValidAccessToken();
  if (token) {
    req.header.set('x-auth-token', token);
  }
  return next(req);
};

export function timestampToMilliseconds(
  timestamp: { seconds: bigint | number | string } | null | undefined,
): number | null {
  if (!timestamp) return null;

  const seconds = Number(timestamp.seconds);
  const milliseconds = seconds * 1000;
  return Number.isSafeInteger(milliseconds) && milliseconds > 0 ? milliseconds : null;
}

function isAuthState(value: unknown): value is AuthState {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;

  const candidate = value as Record<string, unknown>;
  return isNonEmptyString(candidate.accessToken)
    && isNonEmptyString(candidate.refreshToken)
    && isPositiveFiniteNumber(candidate.accessTokenExpiration)
    && isPositiveFiniteNumber(candidate.refreshTokenExpiration);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isPositiveFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

function clearStoredAuth() {
  try {
    localStorage.removeItem(AUTH_KEY);
  } catch {
    // Storage may be disabled by the browser; the in-memory auth state is still invalid.
  }
}
