import { useState } from 'react';
import { useAuth } from '../App';

export function LoginPage() {
  const { login } = useAuth();
  const [loginValue, setLoginValue] = useState('');
  const [password, setPassword] = useState('');
  const [otpCode, setOtpCode] = useState('');
  const [needOtp, setNeedOtp] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const result = await login(loginValue, password, needOtp ? otpCode : undefined);
      if (result.needOtp) {
        setNeedOtp(true);
      }
    } catch {
      setError('Неверный логин или пароль');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="bg-mesh" />
      <div className="bg-grain" />
      <div className="grid-overlay" />

      <div className="login-card">
        <div className="login-logo">
          <span className="glyph">
            <svg viewBox="0 0 24 24"><path d="M8.5 13.5c1.38 0 2.5-1.57 2.5-3.5s-1.12-3.5-2.5-3.5S6 8.07 6 10s1.12 3.5 2.5 3.5zm7 0c1.38 0 2.5-1.57 2.5-3.5s-1.12-3.5-2.5-3.5S13 8.07 13 10s1.12 3.5 2.5 3.5zM5 16.5c1.1 0 2-1.34 2-3s-.9-3-2-3-2 1.34-2 3 .9 3 2 3zm14 0c1.1 0 2-1.34 2-3s-.9-3-2-3-2 1.34-2 3 .9 3 2 3zm-7 4c3 0 6-2.5 6-5 0-2-2-3.5-4-3.5-1.2 0-1.5.5-2 .5s-.8-.5-2-.5c-2 0-4 1.5-4 3.5 0 2.5 3 5 6 5z"/></svg>
          </span>
          <span className="brand-text">barkfluff</span>
          <span className="header-badge">Dev Portal</span>
        </div>

        <form onSubmit={handleSubmit}>
          {!needOtp ? (
            <>
              <div className="login-field">
                <label>Логин или email</label>
                <input
                  type="text"
                  value={loginValue}
                  onChange={e => setLoginValue(e.target.value)}
                  placeholder="username или email@example.com"
                  required
                  autoFocus
                />
              </div>
              <div className="login-field">
                <label>Пароль</label>
                <input
                  type="password"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  placeholder="••••••••"
                  required
                />
              </div>
            </>
          ) : (
            <div className="login-field">
              <label>Код 2FA</label>
              <input
                type="text"
                value={otpCode}
                onChange={e => setOtpCode(e.target.value)}
                placeholder="000000"
                maxLength={6}
                required
                autoFocus
              />
            </div>
          )}

          {error && <div className="login-error">{error}</div>}

          <button type="submit" className="login-btn" disabled={loading}>
            {loading ? 'Входим...' : needOtp ? 'Подтвердить' : 'Войти'}
          </button>
        </form>
      </div>
    </div>
  );
}
