interface HeaderProps {
  onMenuToggle: () => void;
  onLogout: () => void;
}

export function Header({ onMenuToggle, onLogout }: HeaderProps) {
  return (
    <header className="site-header">
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <button className="mobile-menu-btn" onClick={onMenuToggle} title="Меню">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <line x1="3" y1="6" x2="21" y2="6" />
            <line x1="3" y1="12" x2="21" y2="12" />
            <line x1="3" y1="18" x2="21" y2="18" />
          </svg>
        </button>
        <a href="/" className="brand-mark">
          <span className="glyph">
            <svg viewBox="0 0 24 24"><path d="M8.5 13.5c1.38 0 2.5-1.57 2.5-3.5s-1.12-3.5-2.5-3.5S6 8.07 6 10s1.12 3.5 2.5 3.5zm7 0c1.38 0 2.5-1.57 2.5-3.5s-1.12-3.5-2.5-3.5S13 8.07 13 10s1.12 3.5 2.5 3.5zM5 16.5c1.1 0 2-1.34 2-3s-.9-3-2-3-2 1.34-2 3 .9 3 2 3zm14 0c1.1 0 2-1.34 2-3s-.9-3-2-3-2 1.34-2 3 .9 3 2 3zm-7 4c3 0 6-2.5 6-5 0-2-2-3.5-4-3.5-1.2 0-1.5.5-2 .5s-.8-.5-2-.5c-2 0-4 1.5-4 3.5 0 2.5 3 5 6 5z" /></svg>
          </span>
          <span>barkfluff</span>
        </a>
        <span className="header-badge">Dev Portal</span>
      </div>
      <div className="header-links">
        <a href="https://barkfluff.com" target="_blank" rel="noreferrer">barkfluff.com</a>
        <button className="header-btn" onClick={onLogout}>Выйти</button>
      </div>
    </header>
  );
}
