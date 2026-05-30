import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '../../components/Button';
import { TextField } from '../../components/TextField';
import { useAuth } from '../../state/AuthContext';
import { ErrorCodes, extractErrorCode } from '../../api/errorCodes';
import './LoginPage.css';

function messageForError(err: unknown): string {
  const code = extractErrorCode(err);
  if (code === ErrorCodes.INVALID_CREDENTIALS) return 'Неверный логин или пароль';
  if (code === ErrorCodes.INVALID_OTP) return 'Неверный код подтверждения';
  if (err instanceof Error && err.message) return err.message;
  return 'Не удалось войти. Попробуйте ещё раз';
}

export function LoginPage() {
  const { login } = useAuth();
  const [loginValue, setLoginValue] = useState('');
  const [password, setPassword] = useState('');
  const [otpCode, setOtpCode] = useState('');
  const [remember, setRemember] = useState(true);
  const [showPassword, setShowPassword] = useState(false);
  const [needOtp, setNeedOtp] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const res = await login(loginValue.trim(), password, {
        otpCode: needOtp ? otpCode.trim() : undefined,
        remember,
      });
      if (res.needOtp) {
        setNeedOtp(true);
      }
      // при успехе AuthProvider переключит isAuthed → роутер уведёт на /chats
    } catch (err) {
      setError(messageForError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="bf-login">
      <div className="bf-login__bg" aria-hidden="true">
        <span className="bf-login__blob bf-login__blob--1" />
        <span className="bf-login__blob bf-login__blob--2" />
      </div>

      <form className="bf-login__card" onSubmit={submit}>
        <div className="bf-login__brand">
          <span className="material-symbols-rounded bf-login__logo">forum</span>
          <h1 className="bf-login__title">BarkFluff</h1>
          <p className="bf-login__subtitle">
            {needOtp ? 'Введите код двухфакторной аутентификации' : 'Вход в аккаунт'}
          </p>
        </div>

        {!needOtp ? (
          <>
            <TextField
              label="Имя пользователя или email"
              leadingIcon="person"
              autoComplete="username"
              value={loginValue}
              onChange={(e) => setLoginValue(e.target.value)}
              required
              autoFocus
            />
            <TextField
              label="Пароль"
              leadingIcon="lock"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              trailing={
                <button
                  type="button"
                  className="bf-login__eye material-symbols-rounded"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? 'Скрыть пароль' : 'Показать пароль'}
                >
                  {showPassword ? 'visibility_off' : 'visibility'}
                </button>
              }
            />
            <label className="bf-login__remember">
              <input
                type="checkbox"
                checked={remember}
                onChange={(e) => setRemember(e.target.checked)}
              />
              Запомнить меня
            </label>
          </>
        ) : (
          <TextField
            label="Код 2FA"
            leadingIcon="pin"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            value={otpCode}
            onChange={(e) => setOtpCode(e.target.value.replace(/\D/g, ''))}
            required
            autoFocus
          />
        )}

        {error && <p className="bf-login__error">{error}</p>}

        <Button type="submit" loading={busy} className="bf-login__submit">
          {needOtp ? 'Подтвердить' : 'Войти'}
        </Button>

        {needOtp && (
          <Button
            type="button"
            variant="text"
            onClick={() => {
              setNeedOtp(false);
              setOtpCode('');
              setError(null);
            }}
          >
            Назад
          </Button>
        )}

        {!needOtp && (
          <p className="bf-login__switch">
            Нет аккаунта? <Link to="/register">Создать</Link>
          </p>
        )}
      </form>
    </div>
  );
}
