import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';

type Server = {
  name: string;
  serverPublicName: string;
  description: string;
  location: string;
  beaconHost: string;
  beaconPort: number;
  webEndpoint: string;
  filesMediaEndpoint: string;
  lastSeenAt: string;
  color: string;
};

async function request(path: string, init?: RequestInit) {
  return fetch(`/admin/api/${path}`, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  });
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
      <section className="login-card" aria-labelledby="login-title">
        <p className="eyebrow">BarkFluff</p>
        <h1 id="login-title">Navigator</h1>
        <p className="muted">Войдите, чтобы просмотреть активные серверы сети.</p>
        <form onSubmit={submit}>
          <label>
            Логин
            <input autoComplete="username" value={username} onChange={(event) => setUsername(event.target.value)} required />
          </label>
          <label>
            Пароль
            <input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
          </label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <button type="submit" disabled={isSubmitting}>{isSubmitting ? 'Вход…' : 'Войти'}</button>
        </form>
      </section>
    </main>
  );
}

function Dashboard({ username, onLogout }: { username: string; onLogout: () => void }) {
  const [servers, setServers] = useState<Server[]>([]);
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(true);

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

  return (
    <main className="dashboard">
      <header className="topbar">
        <div><p className="eyebrow">BarkFluff</p><h1>Navigator</h1></div>
        <div className="account"><span>{username}</span><button className="secondary" onClick={logout}>Выйти</button></div>
      </header>
      <section className="panel" aria-labelledby="servers-title">
        <div className="panel-heading">
          <div><h2 id="servers-title">Активные серверы</h2><p>{servers.length} в реестре за последние 10 минут</p></div>
          <button className="secondary" onClick={() => void loadServers()} disabled={isLoading}>Обновить</button>
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        {isLoading ? <p className="muted">Загрузка…</p> : servers.length === 0 ? <p className="muted">Активных серверов пока нет.</p> : (
          <div className="server-grid">
            {servers.map((server) => (
              <article className="server-card" key={`${server.name}-${server.beaconHost}`}>
                <span className="color-dot" style={{ backgroundColor: server.color || '#6954d5' }} />
                <h3>{server.serverPublicName || server.name}</h3>
                <p>{server.description}</p>
                <dl>
                  <div><dt>Beacon</dt><dd>{server.beaconHost}:{server.beaconPort}</dd></div>
                  <div><dt>Веб-клиент</dt><dd>{server.webEndpoint || 'Не поддерживается'}</dd></div>
                  <div><dt>Файловый адрес</dt><dd>{server.filesMediaEndpoint || 'Основной адрес Files'}</dd></div>
                  <div><dt>Локация</dt><dd>{server.location || 'Не указана'}</dd></div>
                  <div><dt>Последняя регистрация</dt><dd>{new Date(server.lastSeenAt).toLocaleString('ru-RU')}</dd></div>
                </dl>
              </article>
            ))}
          </div>
        )}
      </section>
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

  if (isCheckingSession) return <main className="login-layout"><p className="muted">Загрузка…</p></main>;
  return username ? <Dashboard username={username} onLogout={() => setUsername(null)} /> : <Login onLogin={setUsername} />;
}
