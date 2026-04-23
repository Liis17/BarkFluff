import { useState, createContext, useContext, useCallback, useEffect } from 'react';
import { LoginPage } from './auth/LoginPage';
import { DocsPage } from './components/DocsPage';

interface AuthState {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiration: number;
  refreshTokenExpiration: number;
}

interface AuthContextValue {
  auth: AuthState | null;
  login: (login: string, password: string, otpCode?: string) => Promise<{ needOtp: boolean }>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const AUTH_KEY = 'barkfluff_dev_auth';

function loadAuth(): AuthState | null {
  try {
    const raw = localStorage.getItem(AUTH_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (parsed.accessTokenExpiration && Date.now() >= parsed.accessTokenExpiration) {
      if (parsed.refreshTokenExpiration && Date.now() >= parsed.refreshTokenExpiration) {
        localStorage.removeItem(AUTH_KEY);
        return null;
      }
    }
    return parsed;
  } catch {
    return null;
  }
}

function saveAuth(auth: AuthState | null) {
  if (auth) {
    localStorage.setItem(AUTH_KEY, JSON.stringify(auth));
  } else {
    localStorage.removeItem(AUTH_KEY);
  }
}

const OTP_NEEDED_CODE = 'C1576884-12D8-4722-A7EE-9F9789AD1265';

export function App() {
  const [auth, setAuth] = useState<AuthState | null>(loadAuth);

  useEffect(() => {
    saveAuth(auth);
  }, [auth]);

  const login = useCallback(async (loginValue: string, password: string, otpCode?: string) => {
    const metadata: Record<string, string> = {
      'x-device-id': btoa(getDeviceId()),
      'x-device-name': btoa(getBrowserName()),
      'x-os-name': btoa(getOsName()),
      'x-app-name': btoa('BarkFluff Developers'),
      'x-app-version': btoa('1.0.0'),
      'x-ip-address': btoa('0.0.0.0'),
    };

    const req: Record<string, unknown> = { password };
    if (loginValue.includes('@')) {
      req.email = loginValue;
    } else {
      req.username = loginValue;
    }
    if (otpCode) {
      req.otpCode = otpCode;
    }

    try {
      const resp = await fetch('/grpc/barkfluff.identity.IdentityApi/Auth', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Connect-Protocol-Version': '1',
          ...metadata,
        },
        body: JSON.stringify(req),
      });

      if (!resp.ok) {
        const errorCode = resp.headers.get('x-error-code');
        if (errorCode === OTP_NEEDED_CODE) {
          return { needOtp: true };
        }
        throw new Error(`Auth failed: ${resp.status}`);
      }

      const data = await resp.json();
      const newAuth: AuthState = {
        accessToken: data.accessToken?.value ?? '',
        refreshToken: data.refreshToken?.value ?? '',
        accessTokenExpiration: data.accessToken?.expirationDate
          ? new Date(data.accessToken.expirationDate).getTime()
          : Date.now() + 3600_000,
        refreshTokenExpiration: data.refreshToken?.expirationDate
          ? new Date(data.refreshToken.expirationDate).getTime()
          : Date.now() + 86400_000 * 9999,
      };
      setAuth(newAuth);
      return { needOtp: false };
    } catch (e) {
      throw e;
    }
  }, []);

  const logout = useCallback(() => {
    setAuth(null);
  }, []);

  return (
    <AuthContext.Provider value={{ auth, login, logout }}>
      {auth ? <DocsPage /> : <LoginPage />}
    </AuthContext.Provider>
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
  if (ua.includes('Linux')) return 'Linux';
  if (ua.includes('Android')) return 'Android';
  if (ua.includes('iOS') || ua.includes('iPhone')) return 'iOS';
  return 'Unknown';
}
