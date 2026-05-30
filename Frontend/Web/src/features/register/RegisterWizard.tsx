import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { TextField } from '../../components/TextField';
import { useAuth } from '../../state/AuthContext';
import { useDebounce } from '../../hooks/useDebounce';
import { tokenStore } from '../../api/tokenStore';
import { getValidToken, timestampToMs } from '../../api/refresh';
import { ErrorCodes, extractErrorCode } from '../../api/errorCodes';
import {
  confirmAccount,
  confirmOtpVerification,
  createAccount,
  enableAuthenticator,
  setPassword,
} from '../../api/services/identity';
import { changeBio, checkEmailExists, checkUsernameExists, setProfilePicture } from '../../api/services/users';
import { uploadFile } from '../../api/upload';
import { UploadFileType } from '../../gen/files_api_pb';
import './RegisterWizard.css';

const USERNAME_RE = /^[a-zA-Z0-9_]{3,32}$/;
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const TOTAL = 9;

type Avail = 'idle' | 'checking' | 'free' | 'taken' | 'invalid';

function passwordScore(p: string): number {
  let s = 0;
  if (p.length >= 8) s++;
  if (/[a-z]/.test(p) && /[A-Z]/.test(p)) s++;
  if (/\d/.test(p)) s++;
  if (/[^a-zA-Z0-9]/.test(p)) s++;
  return s; // 0..4
}

export function RegisterWizard() {
  const navigate = useNavigate();
  const { applySession } = useAuth();
  const avatarInput = useRef<HTMLInputElement>(null);
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Собранные данные
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [codeId, setCodeId] = useState('');
  const [code, setCode] = useState('');
  const [password, setPassword2] = useState('');
  const [bio, setBio] = useState('');
  const [avatarFile, setAvatarFile] = useState<File | null>(null);
  const [avatarPreview, setAvatarPreview] = useState('');
  const [twoFa, setTwoFa] = useState<{ qr: string; secret: string } | null>(null);
  const [otpCode, setOtpCode] = useState('');

  // Проверки доступности
  const [usernameAvail, setUsernameAvail] = useState<Avail>('idle');
  const [emailAvail, setEmailAvail] = useState<Avail>('idle');
  const dUsername = useDebounce(username);
  const dEmail = useDebounce(email);

  useEffect(() => {
    if (!dUsername) return setUsernameAvail('idle');
    if (!USERNAME_RE.test(dUsername)) return setUsernameAvail('invalid');
    setUsernameAvail('checking');
    let cancelled = false;
    checkUsernameExists(dUsername)
      .then((exists) => !cancelled && setUsernameAvail(exists ? 'taken' : 'free'))
      .catch(() => !cancelled && setUsernameAvail('idle'));
    return () => {
      cancelled = true;
    };
  }, [dUsername]);

  useEffect(() => {
    if (!dEmail) return setEmailAvail('idle');
    if (!EMAIL_RE.test(dEmail)) return setEmailAvail('invalid');
    setEmailAvail('checking');
    let cancelled = false;
    checkEmailExists(dEmail)
      .then((exists) => !cancelled && setEmailAvail(exists ? 'taken' : 'free'))
      .catch(() => !cancelled && setEmailAvail('idle'));
    return () => {
      cancelled = true;
    };
  }, [dEmail]);

  function errMessage(e: unknown): string {
    const c = extractErrorCode(e);
    if (c === ErrorCodes.INVALID_USERNAME_FORMAT) return 'Неверный формат username';
    if (c === ErrorCodes.INVALID_OTP) return 'Неверный код';
    return e instanceof Error ? e.message : 'Произошла ошибка';
  }

  // Можно ли перейти со step далее
  const canNext = (() => {
    switch (step) {
      case 0: return firstName.trim().length > 0 && lastName.trim().length > 0;
      case 1: return usernameAvail === 'free';
      case 2: return emailAvail === 'free';
      case 3: return code.trim().length >= 4;
      case 4: return password.length >= 8;
      default: return true;
    }
  })();

  async function next() {
    setError(null);
    setBusy(true);
    try {
      if (step === 2) {
        // Создаём аккаунт → получаем code_id
        const id = await createAccount(firstName.trim(), lastName.trim(), username.trim(), email.trim());
        setCodeId(id);
      } else if (step === 3) {
        // Подтверждаем код → сохраняем сессию (но НЕ помечаем authed, чтобы не вылететь из мастера)
        const rt = await confirmAccount(codeId, code.trim());
        tokenStore.setTempMode(false);
        tokenStore.save({
          accessToken: '',
          accessTokenExpiration: 0,
          refreshToken: rt?.value ?? '',
          refreshTokenExpiration: timestampToMs(rt?.expirationDate) || Date.now() + 30 * 86_400_000,
        });
        await getValidToken(); // получаем access по refresh
      } else if (step === 4) {
        await setPassword(password);
      } else if (step === 5) {
        if (avatarFile) {
          const fileId = await uploadFile(avatarFile, UploadFileType.USER_AVATAR);
          await setProfilePicture(fileId);
        }
      } else if (step === 6) {
        if (bio.trim()) await changeBio(bio.trim());
      } else if (step === 7) {
        // Подтверждение 2FA (если пользователь начал настройку)
        if (twoFa) await confirmOtpVerification(otpCode.trim());
      } else if (step === 8) {
        // Финал
        applySession();
        navigate('/chats', { replace: true });
        return;
      }
      setStep((s) => s + 1);
    } catch (e) {
      setError(errMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function startTwoFa() {
    setBusy(true);
    setError(null);
    try {
      const r = await enableAuthenticator();
      setTwoFa({ qr: r.otpQr, secret: r.otpCode });
    } catch (e) {
      setError(errMessage(e));
    } finally {
      setBusy(false);
    }
  }

  function pickAvatar(f: File) {
    setAvatarFile(f);
    setAvatarPreview(URL.createObjectURL(f));
  }

  const STEP_TITLES = [
    'Как вас зовут?',
    'Придумайте имя пользователя',
    'Укажите email',
    'Подтвердите email',
    'Создайте пароль',
    'Добавьте аватар',
    'Расскажите о себе',
    'Двухфакторная аутентификация',
    'Готово!',
  ];

  return (
    <div className="bf-reg">
      <div className="bf-reg__card">
        <div className="bf-reg__progress">
          {Array.from({ length: TOTAL }).map((_, i) => (
            <span key={i} className={`bf-reg__dot ${i <= step ? 'is-done' : ''}`} />
          ))}
        </div>
        <h1 className="bf-reg__title">{STEP_TITLES[step]}</h1>

        <div className="bf-reg__body">
          {step === 0 && (
            <>
              <TextField label="Имя" value={firstName} onChange={(e) => setFirstName(e.target.value)} autoFocus />
              <TextField label="Фамилия" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            </>
          )}
          {step === 1 && (
            <TextField
              label="Имя пользователя"
              leadingIcon="alternate_email"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoFocus
              error={
                usernameAvail === 'invalid'
                  ? '3–32 символа: буквы, цифры, _'
                  : usernameAvail === 'taken'
                    ? 'Уже занято'
                    : undefined
              }
            />
          )}
          {step === 1 && usernameAvail === 'free' && <span className="bf-reg__ok">Свободно ✓</span>}
          {step === 1 && usernameAvail === 'checking' && <span className="bf-set-hint">Проверка…</span>}

          {step === 2 && (
            <TextField
              label="Email"
              type="email"
              leadingIcon="mail"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoFocus
              error={
                emailAvail === 'invalid' ? 'Неверный формат email' : emailAvail === 'taken' ? 'Уже зарегистрирован' : undefined
              }
            />
          )}
          {step === 2 && emailAvail === 'free' && <span className="bf-reg__ok">Доступно ✓</span>}

          {step === 3 && (
            <>
              <p className="bf-set-hint">Мы отправили код на {email}</p>
              <TextField
                label="Код подтверждения"
                inputMode="numeric"
                value={code}
                onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                autoFocus
              />
              <Button
                variant="text"
                onClick={() => void createAccount(firstName, lastName, username, email).then(setCodeId)}
              >
                Отправить код повторно
              </Button>
            </>
          )}

          {step === 4 && (
            <>
              <TextField
                label="Пароль"
                type="password"
                value={password}
                onChange={(e) => setPassword2(e.target.value)}
                autoFocus
              />
              <div className="bf-reg__strength" data-score={passwordScore(password)}>
                <span /><span /><span /><span />
              </div>
              <span className="bf-set-hint">Минимум 8 символов</span>
            </>
          )}

          {step === 5 && (
            <div className="bf-reg__avatar">
              <Avatar name={`${firstName} ${lastName}`} src={avatarPreview || undefined} size={96} />
              <Button variant="tonal" icon="photo_camera" type="button" onClick={() => avatarInput.current?.click()}>
                Выбрать фото
              </Button>
              <input
                ref={avatarInput}
                type="file"
                accept="image/*"
                hidden
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) pickAvatar(f);
                  e.target.value = '';
                }}
              />
            </div>
          )}

          {step === 6 && (
            <TextField label="О себе (необязательно)" value={bio} onChange={(e) => setBio(e.target.value)} autoFocus />
          )}

          {step === 7 && (
            <>
              {!twoFa ? (
                <>
                  <p className="bf-set-hint">Добавьте дополнительную защиту аккаунта (необязательно).</p>
                  <Button variant="tonal" icon="security" onClick={() => void startTwoFa()} loading={busy}>
                    Настроить 2FA
                  </Button>
                </>
              ) : (
                <div className="bf-set-fields">
                  <img className="bf-2fa__qr" src={twoFa.qr.startsWith('data:') ? twoFa.qr : `data:image/png;base64,${twoFa.qr}`} alt="QR" />
                  <code className="bf-2fa__secret">{twoFa.secret}</code>
                  <TextField
                    label="Код из приложения"
                    inputMode="numeric"
                    maxLength={6}
                    value={otpCode}
                    onChange={(e) => setOtpCode(e.target.value.replace(/\D/g, ''))}
                  />
                </div>
              )}
            </>
          )}

          {step === 8 && (
            <p className="bf-set-hint">Аккаунт создан. Добро пожаловать в BarkFluff!</p>
          )}
        </div>

        {error && <p className="bf-set-error">{error}</p>}

        <div className="bf-reg__actions">
          {step > 0 && step < 8 && (
            <Button variant="text" onClick={() => { setStep((s) => s - 1); setError(null); }} disabled={busy}>
              Назад
            </Button>
          )}
          <Button onClick={() => void next()} loading={busy} disabled={!canNext}>
            {step === 8 ? 'Войти' : step === 5 || step === 6 || step === 7 ? 'Далее' : 'Продолжить'}
          </Button>
        </div>
      </div>
    </div>
  );
}
