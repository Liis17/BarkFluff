import { useState, createContext, useContext, useCallback, useEffect } from 'react';
import { createClient, ConnectError } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { IdentityApi } from './gen/identity_api_connect';
import { AuthRequest } from './gen/identity_api_pb';
import { LoginPage } from './auth/LoginPage';
import { DocsPage } from './components/DocsPage';
import { type AuthState, AUTH_CHANGED_EVENT, loadAuth, saveAuth, timestampToMilliseconds } from './auth/tokenManager';
import { ErrorBoundary } from './components/ErrorBoundary';

interface AuthContextValue {
  auth: AuthState | null;
  login: (login: string, password: string, otpCode?: string) => Promise<{ needOtp: boolean }>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const OTP_NEEDED_CODE = 'C1576884-12D8-4722-A7EE-9F9789AD1265';

const identityTransport = createGrpcWebTransport({ baseUrl: '/grpc' });
const identityClient = createClient(IdentityApi, identityTransport);

export function App() {
  const [auth, setAuth] = useState<AuthState | null>(loadAuth);

  // Держит состояние в синхроне с фоновым обновлением/сбросом токена (tokenManager.ensureValidAccessToken)
  useEffect(() => {
    const onAuthChanged = (e: Event) => {
      setAuth((e as CustomEvent<AuthState | null>).detail);
    };
    window.addEventListener(AUTH_CHANGED_EVENT, onAuthChanged);
    return () => window.removeEventListener(AUTH_CHANGED_EVENT, onAuthChanged);
  }, []);

  const login = useCallback(async (loginValue: string, password: string, otpCode?: string) => {
    const metadata = {
      'x-device-id': btoa(getDeviceId()),
      'x-device-name': btoa(getBrowserName()),
      'x-os-name': btoa(getOsName()),
      'x-app-name': btoa('BarkFluff Developers'),
      'x-app-version': btoa('1.0.0'),
      'x-ip-address': btoa('0.0.0.0'),
    };

    const request = new AuthRequest({
      login: loginValue.includes('@')
        ? { case: 'email', value: loginValue }
        : { case: 'username', value: loginValue },
      password,
      otpCode: otpCode ?? '',
    });

    try {
      const resp = await identityClient.auth(request, { headers: new Headers(metadata) });
      const accessToken = resp.accessToken?.value.trim();
      const refreshToken = resp.refreshToken?.value.trim();
      const accessTokenExpiration = timestampToMilliseconds(resp.accessToken?.expirationDate);
      const refreshTokenExpiration = timestampToMilliseconds(resp.refreshToken?.expirationDate);
      if (!accessToken || !refreshToken || accessTokenExpiration === null || refreshTokenExpiration === null) {
        throw new Error('Identity service returned incomplete authentication data');
      }

      const newAuth: AuthState = {
        accessToken,
        refreshToken,
        accessTokenExpiration,
        refreshTokenExpiration,
      };
      saveAuth(newAuth);
      setAuth(newAuth);
      return { needOtp: false };
    } catch (err) {
      if (err instanceof ConnectError) {
        const errorCode = err.metadata.get('x-error-code');
        if (errorCode === OTP_NEEDED_CODE) return { needOtp: true };
      }
      throw err;
    }
  }, []);

  const logout = useCallback(() => {
    saveAuth(null);
    setAuth(null);
  }, []);

  return (
    <ErrorBoundary>
      <AuthContext.Provider value={{ auth, login, logout }}>
        {auth ? <DocsPage /> : <LoginPage />}
      </AuthContext.Provider>
    </ErrorBoundary>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthContext');
  return ctx;
}

export function getDeviceId(): string {
  let id = localStorage.getItem('barkfluff_device_id');
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem('barkfluff_device_id', id);
  }
  return id;
}

function getBrowserName(): string {
  const ua = navigator.userAgent;
  if (ua.includes('Firefox')) return 'Firefox';
  if (ua.includes('Edg')) return 'Edge';
  if (ua.includes('Chrome')) return 'Chrome';
  if (ua.includes('Safari')) return 'Safari';
  if (ua.includes('Opera') || ua.includes('OPR')) return 'Opera';
  return 'Unknown';
}

function getOsName(): string {
  const ua = navigator.userAgent;
  if (ua.includes('Win')) return 'Windows';
  if (ua.includes('Mac')) return 'macOS';
  if (ua.includes('Android')) return 'Android';
  if (ua.includes('iOS') || ua.includes('iPhone')) return 'iOS';
  if (ua.includes('Linux')) return 'Linux';
  return 'Unknown';
}
