import { Navigate, NavLink, Route, Routes } from 'react-router-dom';
import { ProfileSettings } from './ProfileSettings';
import { SessionsSettings } from './SessionsSettings';
import { TwoFactorSettings } from './TwoFactorSettings';
import { StorageSettings } from './StorageSettings';
import './Settings.css';

const TABS = [
  { to: 'profile', icon: 'person', label: 'Профиль' },
  { to: 'sessions', icon: 'devices', label: 'Сессии' },
  { to: 'security', icon: 'security', label: 'Безопасность' },
  { to: 'storage', icon: 'cloud', label: 'Хранилище' },
];

export function SettingsPage() {
  return (
    <div className="bf-settings">
      <nav className="bf-settings__nav">
        <h1 className="bf-settings__title">Настройки</h1>
        {TABS.map((t) => (
          <NavLink
            key={t.to}
            to={t.to}
            className={({ isActive }) => `bf-settings__tab ${isActive ? 'is-active' : ''}`}
          >
            <span className="material-symbols-rounded">{t.icon}</span>
            {t.label}
          </NavLink>
        ))}
      </nav>
      <div className="bf-settings__content">
        <Routes>
          <Route path="profile" element={<ProfileSettings />} />
          <Route path="sessions" element={<SessionsSettings />} />
          <Route path="security" element={<TwoFactorSettings />} />
          <Route path="storage" element={<StorageSettings />} />
          <Route path="*" element={<Navigate to="profile" replace />} />
        </Routes>
      </div>
    </div>
  );
}
