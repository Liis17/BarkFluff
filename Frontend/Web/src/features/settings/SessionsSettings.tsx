import { useEffect, useState } from 'react';
import { Button } from '../../components/Button';
import { IconButton } from '../../components/IconButton';
import { getActiveSessions, removeActiveSession } from '../../api/services/identity';
import { renameDevice } from '../../api/services/users';
import { getDeviceId } from '../../api/device';
import { tsToDate } from '../../utils/format';
import type { GetActiveSessionsResponse_Session } from '../../gen/identity_api_pb';

export function SessionsSettings() {
  const [sessions, setSessions] = useState<GetActiveSessionsResponse_Session[]>([]);
  const [loading, setLoading] = useState(true);
  const myDevice = getDeviceId();

  function reload() {
    setLoading(true);
    getActiveSessions()
      .then(setSessions)
      .catch(() => {})
      .finally(() => setLoading(false));
  }
  useEffect(reload, []);

  async function onTerminate(deviceId: string) {
    if (!window.confirm('Завершить эту сессию?')) return;
    await removeActiveSession(deviceId);
    reload();
  }

  async function onRename(s: GetActiveSessionsResponse_Session) {
    const name = window.prompt('Новое имя устройства', s.customName || s.originalName);
    if (name == null) return;
    await renameDevice(s.deviceId, name);
    reload();
  }

  return (
    <div className="bf-setcard">
      <h2 className="bf-setcard__title">Активные сессии</h2>
      {loading && <p className="bf-set-hint">Загрузка…</p>}
      {!loading && sessions.length === 0 && <p className="bf-set-hint">Сессий нет</p>}
      {sessions.map((s) => {
        const isCurrent = s.deviceId === myDevice;
        const created = tsToDate(s.createdAt);
        return (
          <div className="bf-setrow" key={s.deviceId}>
            <div className="bf-setrow__info">
              <span className="bf-setrow__label">
                {s.customName || s.originalName || 'Устройство'}
                {isCurrent && ' · текущее'}
              </span>
              <span className="bf-setrow__sub">
                {[s.appName, s.operationSystem, s.location].filter(Boolean).join(' · ')}
                {created && ` · с ${created.toLocaleDateString('ru-RU')}`}
              </span>
            </div>
            <div style={{ display: 'flex', gap: 4 }}>
              <IconButton icon="edit" onClick={() => void onRename(s)} aria-label="Переименовать" />
              {!isCurrent && (
                <IconButton icon="logout" onClick={() => void onTerminate(s.deviceId)} aria-label="Завершить" />
              )}
            </div>
          </div>
        );
      })}
      <div className="bf-set-actions">
        <Button variant="text" icon="refresh" onClick={reload}>
          Обновить
        </Button>
      </div>
    </div>
  );
}
