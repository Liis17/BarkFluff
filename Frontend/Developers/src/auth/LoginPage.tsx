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
            <img src="/favicon.ico" width="36" height="36" alt="" />
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
