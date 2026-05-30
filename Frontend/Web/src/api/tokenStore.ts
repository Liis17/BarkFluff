// Хранилище токенов. Порт wwwroot/js/app/tokens.js.
// sessionStorage для временного входа (не запоминать), localStorage иначе.

export interface AuthTokens {
  accessToken: string;
  accessTokenExpiration: number; // ms epoch
  refreshToken: string;
  refreshTokenExpiration: number; // ms epoch
}

const KEY = 'barkfluff_auth';
const MODE_KEY = 'barkfluff_temp';

function store(): Storage {
  return localStorage.getItem(MODE_KEY) === '1' ? sessionStorage : localStorage;
}

export const tokenStore = {
  setTempMode(isTemp: boolean): void {
    if (isTemp) localStorage.setItem(MODE_KEY, '1');
    else localStorage.removeItem(MODE_KEY);
  },

  save(data: AuthTokens): void {
    store().setItem(KEY, JSON.stringify(data));
  },

  get(): AuthTokens | null {
    const raw = store().getItem(KEY) ?? sessionStorage.getItem(KEY) ?? localStorage.getItem(KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as AuthTokens;
    } catch {
      return null;
    }
  },

  clear(): void {
    sessionStorage.removeItem(KEY);
    localStorage.removeItem(KEY);
    localStorage.removeItem(MODE_KEY);
  },

  getAccessToken(): string | null {
    return this.get()?.accessToken ?? null;
  },

  getRefreshToken(): string | null {
    return this.get()?.refreshToken ?? null;
  },

  // true, если access истёк или истечёт в ближайшие 30с.
  isAccessExpired(): boolean {
    const d = this.get();
    if (!d?.accessTokenExpiration) return true;
    return Date.now() >= d.accessTokenExpiration - 30_000;
  },

  // true, если refresh-токен ещё валиден (можно восстановить сессию).
  hasValidRefresh(): boolean {
    const d = this.get();
    if (!d?.refreshToken) return false;
    if (!d.refreshTokenExpiration) return true;
    return Date.now() < d.refreshTokenExpiration;
  },
};
