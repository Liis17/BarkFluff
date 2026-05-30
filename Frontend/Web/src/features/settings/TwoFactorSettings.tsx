import { useEffect, useState } from 'react';
import { Button } from '../../components/Button';
import { Switch } from '../../components/Switch';
import { TextField } from '../../components/TextField';
import {
  confirmOtpVerification,
  disableAuthenticator,
  enableAuthenticator,
  listOtpVerification,
} from '../../api/services/identity';
import { ErrorCodes, extractErrorCode } from '../../api/errorCodes';

function qrSrc(otpQr: string): string {
  return otpQr.startsWith('data:') ? otpQr : `data:image/png;base64,${otpQr}`;
}

export function TwoFactorSettings() {
  const [authEnabled, setAuthEnabled] = useState(false);
  const [emailEnabled, setEmailEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [setup, setSetup] = useState<{ qr: string; secret: string } | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function reload() {
    setLoading(true);
    listOtpVerification()
      .then((r) => {
        setAuthEnabled(r.authenticatorEnabled);
        setEmailEnabled(r.emailEnabled);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }
  useEffect(reload, []);

  async function onToggleAuth(next: boolean) {
    setError(null);
    if (next) {
      // Запускаем настройку: получаем QR + секрет.
      setBusy(true);
      try {
        const r = await enableAuthenticator();
        setSetup({ qr: r.otpQr, secret: r.otpCode });
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Не удалось начать настройку');
      } finally {
        setBusy(false);
      }
    } else {
      const c = window.prompt('Введите код из приложения для отключения 2FA');
      if (!c) return;
      setBusy(true);
      try {
        await disableAuthenticator(c.trim());
        reload();
      } catch (err) {
        setError(extractErrorCode(err) === ErrorCodes.INVALID_OTP ? 'Неверный код' : 'Не удалось отключить');
      } finally {
        setBusy(false);
      }
    }
  }

  async function onConfirm() {
    setBusy(true);
    setError(null);
    try {
      await confirmOtpVerification(code.trim());
      setSetup(null);
      setCode('');
      reload();
    } catch (err) {
      setError(extractErrorCode(err) === ErrorCodes.INVALID_OTP ? 'Неверный код' : 'Не удалось подтвердить');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="bf-setcard">
      <h2 className="bf-setcard__title">Двухфакторная аутентификация</h2>
      {loading && <p className="bf-set-hint">Загрузка…</p>}

      {!loading && !setup && (
        <>
          <div className="bf-setrow">
            <div className="bf-setrow__info">
              <span className="bf-setrow__label">Приложение-аутентификатор</span>
              <span className="bf-setrow__sub">Google Authenticator, Authy и др.</span>
            </div>
            <Switch checked={authEnabled} onChange={onToggleAuth} disabled={busy} aria-label="2FA приложение" />
          </div>
          <div className="bf-setrow">
            <div className="bf-setrow__info">
              <span className="bf-setrow__label">Код по email</span>
              <span className="bf-setrow__sub">{emailEnabled ? 'Включено' : 'Выключено'}</span>
            </div>
          </div>
        </>
      )}

      {setup && (
        <div className="bf-set-fields">
          <p className="bf-set-hint">Отсканируйте QR в приложении-аутентификаторе или введите ключ вручную:</p>
          <img className="bf-2fa__qr" src={qrSrc(setup.qr)} alt="QR код 2FA" />
          <code className="bf-2fa__secret">{setup.secret}</code>
          <TextField
            label="Код из приложения"
            inputMode="numeric"
            maxLength={6}
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
            autoFocus
          />
          <div className="bf-set-actions">
            <Button variant="text" onClick={() => { setSetup(null); setCode(''); setError(null); }}>
              Отмена
            </Button>
            <Button onClick={() => void onConfirm()} loading={busy} disabled={code.length < 6}>
              Подтвердить
            </Button>
          </div>
        </div>
      )}

      {error && <p className="bf-set-error">{error}</p>}
    </div>
  );
}
