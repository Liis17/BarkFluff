import { NavLink } from 'react-router-dom';
import { useThemeStore, type Theme } from '../state/themeStore';
import { useAuth } from '../state/AuthContext';
import './NavRail.css';

const THEME_ORDER: Theme[] = ['light', 'dark', 'midnight'];
const THEME_ICON: Record<Theme, string> = {
  light: 'light_mode',
  dark: 'dark_mode',
  midnight: 'bedtime',
};

const ITEMS = [
  { to: '/chats', icon: 'forum', label: 'Чаты' },
  { to: '/settings', icon: 'settings', label: 'Настройки' },
];

export function NavRail() {
  const { theme, setTheme } = useThemeStore();
  const { logout } = useAuth();

  const cycleTheme = () => {
    const next = THEME_ORDER[(THEME_ORDER.indexOf(theme) + 1) % THEME_ORDER.length];
    setTheme(next);
  };

  return (
    <nav className="bf-rail">
      <div className="bf-rail__brand">
        <span className="material-symbols-rounded bf-rail__logo">forum</span>
      </div>

      <div className="bf-rail__items">
        {ITEMS.map((it) => (
          <NavLink
            key={it.to}
            to={it.to}
            className={({ isActive }) => `bf-rail__item ${isActive ? 'is-active' : ''}`}
          >
            {({ isActive }) => (
              <>
                <span className="bf-rail__pill" aria-hidden="true">
                  <span
                    className="material-symbols-rounded"
                    style={isActive ? { fontVariationSettings: "'FILL' 1" } : undefined}
                  >
                    {it.icon}
                  </span>
                </span>
                <span className="bf-rail__label">{it.label}</span>
              </>
            )}
          </NavLink>
        ))}
      </div>

      <div className="bf-rail__bottom">
        <button className="bf-rail__action" onClick={cycleTheme} title="Сменить тему">
          <span className="material-symbols-rounded">{THEME_ICON[theme]}</span>
        </button>
        <button className="bf-rail__action" onClick={() => logout()} title="Выйти">
          <span className="material-symbols-rounded">logout</span>
        </button>
      </div>
    </nav>
  );
}
