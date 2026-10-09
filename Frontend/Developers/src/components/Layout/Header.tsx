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
            <img src="/favicon.ico" width="28" height="28" alt="" />
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
