import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';

type Server = {
  id: number;
  name: string;
  serverPublicName: string;
  description: string;
  location: string;
  beaconHost: string;
  beaconPort: number;
  webEndpoint: string;
  filesMediaEndpoint: string;
  lastSeenAt: string;
  isManual: boolean;
  color: string;
};

const EMPTY_FORM = {
  name: '',
  serverPublicName: '',
  description: '',
  location: '',
  color: '#8c351c',
  beaconHost: '',
  beaconPort: '',
  webEndpoint: '',
  filesMediaEndpoint: '',
};

async function request(path: string, init?: RequestInit) {
  return fetch(`/admin/api/${path}`, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  });
}

function BgDeco() {
  return (
    <div className="bg-deco" aria-hidden="true"><span></span><span></span><span></span></div>
  );
}

function Login({ onLogin }: { onLogin: (username: string) => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setIsSubmitting(true);

    try {
      const response = await request('login', {
        method: 'POST',
        body: JSON.stringify({ username, password }),
      });

      if (!response.ok) {
        setError('Неверный логин или пароль.');
        return;
      }

      onLogin(username);
    } catch {
      setError('Не удалось подключиться к Navigator.');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <main className="login-layout">
      <BgDeco />
      <section className="login-card" aria-labelledby="login-title">
        <div className="login-icon-wrap" aria-hidden="true"><span className="msr">lock</span></div>
        <p className="eyebrow">BarkFluff</p>
        <h1 id="login-title">Navigator</h1>
        <p className="muted">Войдите, чтобы просмотреть активные серверы сети.</p>
        <form onSubmit={submit}>
          <div className="md-field-outlined">
            <input
              type="text"
              placeholder="Логин"
              autoComplete="username"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              required
            />
          </div>
          <div className="md-field-outlined">
            <input
              type="password"
              placeholder="Пароль"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </div>
          {error && <p className="form-error" role="alert">{error}</p>}
          <button type="submit" className="md-btn md-btn-filled md-btn-lg md-btn-square-press login-btn" disabled={isSubmitting}>
            {isSubmitting ? 'Вход…' : 'Войти'}
          </button>
        </form>
      </section>
    </main>
  );
}

function AddServerDialog({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ ...EMPTY_FORM });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  function set<K extends keyof typeof form>(key: K, value: string) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setIsSubmitting(true);

    try {
      const response = await request('servers', {
        method: 'POST',
        body: JSON.stringify({
          ...form,
          beaconPort: form.beaconPort ? Number(form.beaconPort) : null,
        }),
      });

      if (response.status === 401) {
        onClose();
        onSaved();
        return;
      }
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        setError(body?.error ?? 'Не удалось добавить сервер.');
        return;
      }

      onSaved();
    } catch {
      setError('Не удалось подключиться к Navigator.');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="md-scrim open" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <div className="md-dialog md-dialog-lg" role="dialog" aria-modal="true" aria-labelledby="add-server-title">
        <div className="md-dialog-header">
          <h3 id="add-server-title">Добавить сервер вручную</h3>
          <button type="button" className="md-icon-btn" onClick={onClose} aria-label="Закрыть">
            <span className="msr">close</span>
          </button>
        </div>
        <form onSubmit={submit}>
          <div className="md-dialog-body">
            <p className="dialog-hint">Сервер будет закреплён в каталоге навсегда — независимо от его доступности и активности регистрации.</p>
            <div className="field-grid">
              <label>
                <span>Публичное имя *</span>
                <input className="md-input-outlined" value={form.serverPublicName} onChange={(e) => set('serverPublicName', e.target.value)} maxLength={64} required />
              </label>
              <label>
                <span>Внутреннее имя *</span>
                <input className="md-input-outlined" value={form.name} onChange={(e) => set('name', e.target.value)} maxLength={64} required />
              </label>
              <label className="span-2">
                <span>Описание *</span>
                <textarea className="md-input-outlined dialog-textarea" value={form.description} onChange={(e) => set('description', e.target.value)} maxLength={512} required />
              </label>
              <label>
                <span>Локация</span>
                <input className="md-input-outlined" value={form.location} onChange={(e) => set('location', e.target.value)} maxLength={128} />
              </label>
              <label>
                <span>Цвет (HEX)</span>
                <span className="color-row">
                  <input type="color" className="color-picker" value={/^#[0-9A-Fa-f]{6}$/.test(form.color) ? form.color : '#8c351c'} onChange={(e) => set('color', e.target.value)} aria-label="Выбор цвета" />
                  <input className="md-input-outlined" value={form.color} onChange={(e) => set('color', e.target.value)} placeholder="#8c351c" />
                </span>
              </label>
              <label>
                <span>Beacon — адрес</span>
                <input className="md-input-outlined" value={form.beaconHost} onChange={(e) => set('beaconHost', e.target.value)} placeholder="beacon.example.org" />
              </label>
              <label>
                <span>Beacon — порт</span>
                <input className="md-input-outlined" type="number" min={1} max={65535} value={form.beaconPort} onChange={(e) => set('beaconPort', e.target.value)} disabled={!form.beaconHost} />
              </label>
              <label>
                <span>Веб-клиент (URL)</span>
                <input className="md-input-outlined" value={form.webEndpoint} onChange={(e) => set('webEndpoint', e.target.value)} placeholder="https://web.example.org" />
              </label>
              <label>
                <span>Файловый адрес (URL)</span>
                <input className="md-input-outlined" value={form.filesMediaEndpoint} onChange={(e) => set('filesMediaEndpoint', e.target.value)} placeholder="https://files.example.org" />
              </label>
            </div>
            {error && <p className="form-error" role="alert">{error}</p>}
          </div>
          <div className="md-dialog-actions">
            <button type="button" className="md-btn md-btn-text" onClick={onClose}>Отмена</button>
            <button type="submit" className="md-btn md-btn-filled" disabled={isSubmitting}>
              <span className="msr">push_pin</span>{isSubmitting ? 'Добавление…' : 'Закрепить сервер'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function Dashboard({ username, onLogout }: { username: string; onLogout: () => void }) {
  const [servers, setServers] = useState<Server[]>([]);
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isAddOpen, setIsAddOpen] = useState(false);

  async function loadServers() {
    setIsLoading(true);
    setError('');
    try {
      const response = await request('servers');
      if (response.status === 401) {
        onLogout();
        return;
      }
      if (!response.ok) throw new Error();
      setServers(await response.json());
    } catch {
      setError('Не удалось загрузить список серверов.');
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => { void loadServers(); }, []);

  async function logout() {
    await request('logout', { method: 'POST' });
    onLogout();
  }

  async function deleteServer(server: Server) {
    if (!window.confirm(`Удалить сервер «${server.serverPublicName || server.name}» из каталога?`))
      return;

    const response = await request(`servers/${server.id}`, { method: 'DELETE' });
    if (response.status === 401) {
      onLogout();
      return;
    }
    await loadServers();
  }

  const manualCount = servers.filter((server) => server.isManual).length;

  return (
    <main className="dashboard">
      <BgDeco />
      <header className="topbar">
        <div>
          <p className="eyebrow">BarkFluff</p>
          <h1>Navigator</h1>
        </div>
        <div className="account">
          <span className="account-chip"><span className="msr">person</span>{username}</span>
          <button className="md-btn md-btn-outlined" onClick={() => void logout()}>
            <span className="msr">logout</span>Выйти
          </button>
        </div>
      </header>
      <section className="panel" aria-labelledby="servers-title">
        <div className="panel-heading">
          <div>
            <h2 id="servers-title">Серверы сети</h2>
            <p className="muted">
              {servers.length} в реестре{manualCount > 0 ? ` · ${manualCount} закреплён` : ''}
            </p>
          </div>
          <div className="panel-actions">
            <button className="md-btn md-btn-tonal" onClick={() => void loadServers()} disabled={isLoading}>
              <span className="msr">refresh</span>Обновить
            </button>
            <button className="md-btn md-btn-filled" onClick={() => setIsAddOpen(true)}>
              <span className="msr">add</span>Добавить сервер
            </button>
          </div>
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        {isLoading ? (
          <div className="loading-row"><div className="md-loader-expressive" aria-label="Загрузка"></div></div>
        ) : servers.length === 0 ? (
          <div className="md-empty">
            <span className="msr">dns</span>
            <h3>Серверов пока нет</h3>
            <p>Активные серверы появятся автоматически после регистрации. Свои можно закрепить вручную.</p>
          </div>
        ) : (
          <div className="server-grid">
            {servers.map((server) => (
              <article className="server-card" key={server.id}>
                <span className="color-dot" style={{ backgroundColor: server.color || 'var(--md-primary)' }} />
                <div className="server-card-head">
                  <h3>{server.serverPublicName || server.name}</h3>
                  {server.isManual && (
                    <span className="md-chip md-chip-elevated"><span className="msr">push_pin</span>Закреплён</span>
                  )}
                </div>
                <p>{server.description}</p>
                <dl>
                  <div><dt>Beacon</dt><dd>{server.beaconHost ? `${server.beaconHost}:${server.beaconPort}` : 'Не указан'}</dd></div>
                  <div><dt>Веб-клиент</dt><dd>{server.webEndpoint || 'Не поддерживается'}</dd></div>
                  <div><dt>Файловый адрес</dt><dd>{server.filesMediaEndpoint || 'Основной адрес Files'}</dd></div>
                  <div><dt>Локация</dt><dd>{server.location || 'Не указана'}</dd></div>
                  <div><dt>Последняя регистрация</dt><dd>{new Date(server.lastSeenAt).toLocaleString('ru-RU')}</dd></div>
                </dl>
                {server.isManual && (
                  <button className="md-btn md-btn-text delete-btn" onClick={() => void deleteServer(server)}>
                    <span className="msr">delete</span>Удалить
                  </button>
                )}
              </article>
            ))}
          </div>
        )}
      </section>
      {isAddOpen && (
        <AddServerDialog
          onClose={() => setIsAddOpen(false)}
          onSaved={() => { setIsAddOpen(false); void loadServers(); }}
        />
      )}
    </main>
  );
}

export default function App() {
  const [username, setUsername] = useState<string | null>(null);
  const [isCheckingSession, setIsCheckingSession] = useState(true);

  useEffect(() => {
    void request('session')
      .then(async (response) => response.ok ? response.json() : null)
      .then((session) => setUsername(session?.username ?? null))
      .finally(() => setIsCheckingSession(false));
  }, []);

  if (isCheckingSession) {
    return (
      <main className="login-layout">
        <div className="loading-row"><div className="md-loader-expressive" aria-label="Загрузка"></div></div>
      </main>
    );
  }

  return username ? <Dashboard username={username} onLogout={() => setUsername(null)} /> : <Login onLogin={setUsername} />;
}
