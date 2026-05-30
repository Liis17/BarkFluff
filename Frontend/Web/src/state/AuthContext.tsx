import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { AuthRequest } from '../gen/identity_api_pb';
import { rawIdentityClient } from '../api/baseTransport';
import { identityClient } from '../api/transport';
import { buildHeaders } from '../api/metadata';
import { tokenStore, type AuthTokens } from '../api/tokenStore';
import { timestampToMs, AUTH_LOST_EVENT } from '../api/refresh';
import { ErrorCodes, extractErrorCode } from '../api/errorCodes';
import { userIdFromToken } from '../api/jwt';

interface AuthContextValue {
  isAuthed: boolean;
  currentUserId: string | null;
  login: (login: string, password: string, opts?: { otpCode?: string; remember?: boolean }) => Promise<{ needOtp: boolean }>;
  logout: () => Promise<void>;
  // Применить уже сохранённую в tokenStore сессию (после завершения регистрации).
  applySession: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  // Авторизованы, если есть валидный refresh-токен (access обновится по нужде).
  const [isAuthed, setIsAuthed] = useState<boolean>(() => tokenStore.hasValidRefresh());
  const [currentUserId, setCurrentUserId] = useState<string | null>(() =>
    userIdFromToken(tokenStore.getAccessToken()),
  );

  // Глобальная потеря авторизации (refresh не удался) — чистим состояние.
  useEffect(() => {
    const onLost = () => {
      setIsAuthed(false);
      setCurrentUserId(null);
    };
    window.addEventListener(AUTH_LOST_EVENT, onLost);
    return () => window.removeEventListener(AUTH_LOST_EVENT, onLost);
  }, []);

  const login = useCallback(
    async (loginValue: string, password: string, opts?: { otpCode?: string; remember?: boolean }) => {
      // remember=false → временный режим (sessionStorage). Ставим ДО save.
      tokenStore.setTempMode(opts?.remember === false);

      const request = new AuthRequest({
        login: loginValue.includes('@')
          ? { case: 'email', value: loginValue }
          : { case: 'username', value: loginValue },
        password,
        otpCode: opts?.otpCode ?? '',
      });

      try {
        const resp = await rawIdentityClient.auth(request, { headers: buildHeaders() });
        const access = resp.accessToken;
        const refresh = resp.refreshToken;
        const tokens: AuthTokens = {
          accessToken: access?.value ?? '',
          accessTokenExpiration: timestampToMs(access?.expirationDate) || Date.now() + 3_600_000,
          refreshToken: refresh?.value ?? '',
          refreshTokenExpiration: timestampToMs(refresh?.expirationDate) || Date.now() + 30 * 86_400_000,
        };
        tokenStore.save(tokens);
        setCurrentUserId(userIdFromToken(tokens.accessToken));
        setIsAuthed(true);
        return { needOtp: false };
      } catch (err) {
        if (extractErrorCode(err) === ErrorCodes.OTP_REQUIRED) {
          return { needOtp: true };
        }
        throw err;
      }
    },
    [],
  );

  const applySession = useCallback(() => {
    setCurrentUserId(userIdFromToken(tokenStore.getAccessToken()));
    setIsAuthed(tokenStore.hasValidRefresh());
  }, []);

  const logout = useCallback(async () => {
    try {
      await identityClient.logout({});
    } catch {
      // best-effort — всё равно чистим локально
    }
    tokenStore.clear();
    setIsAuthed(false);
    setCurrentUserId(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ isAuthed, currentUserId, login, logout, applySession }),
    [isAuthed, currentUserId, login, logout, applySession],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
