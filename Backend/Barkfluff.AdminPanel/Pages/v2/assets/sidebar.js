/* =============================================================
   BarkFluff Admin — shared navigation drawer
   Renders into <div id="md-nav-root" data-active="..."></div>
   ============================================================= */

(function () {
  const NAV_ITEMS = [
    { id: 'dashboard',     href: '/',              label: 'Дашборд',    icon: 'space_dashboard' },
    { id: 'services',      href: '/services',      label: 'Сервисы',    icon: 'dns' },
    { id: 'logs',          href: '/logs',          label: 'Логи',       icon: 'terminal' },
    { id: 'badges',        href: '/badges',        label: 'Бейджи',     icon: 'workspace_premium' },
    { id: 'stickers',      href: '/stickers',      label: 'Стикеры',    icon: 'mood' },
    { id: 'users',         href: '/users',         label: 'Юзеры',      icon: 'group' },
    { id: 'bots',          href: '/bots',          label: 'Боты',       icon: 'smart_toy' },
    { id: 'federation',    href: '/federation',    label: 'Федерация',  icon: 'hub' },
    { id: 'notifications', href: '/notifications', label: 'Уведомления',icon: 'notifications' },
    { id: 'mail',          href: '/mail',          label: 'Почта',      icon: 'mail' },
    { id: 'configuration', href: '/configuration', label: 'Конфигурация', icon: 'tune' },
    { id: 's3',            href: '/s3-storage',    label: 'Хранилище S3', icon: 'cloud', expandable: true }
  ];

  function el(html) {
    const t = document.createElement('template');
    t.innerHTML = html.trim();
    return t.content.firstElementChild;
  }

  function buildItem(item, activeId) {
    const isActive = item.id === activeId;
    const cls = `md-nav-item${isActive ? ' active' : ''}`;
    if (item.expandable) {
      return `
        <div class="md-nav-row">
          <a class="${cls}" href="${item.href}">
            <span class="msr">${item.icon}</span>
            <span>${item.label}</span>
          </a>
          <button class="md-nav-expander" id="s3-chevron-btn" onclick="window.__toggleS3Menu && window.__toggleS3Menu()" aria-label="Развернуть">
            <span class="msr" id="s3-chevron-icon">expand_more</span>
          </button>
        </div>
        <div id="s3-submenu" class="md-nav-submenu" style="display:none;"></div>
      `;
    }
    return `
      <a class="${cls}" href="${item.href}">
        <span class="msr">${item.icon}</span>
        <span>${item.label}</span>
      </a>
    `;
  }

  function render(rootEl) {
    const active = rootEl.getAttribute('data-active') || '';
    const userName = rootEl.getAttribute('data-user-name') || 'Admin';
    const userMeta = rootEl.getAttribute('data-user-meta') || '';
    const initial = (userName || 'A').trim().charAt(0).toUpperCase();

    rootEl.innerHTML = `
      <aside class="md-nav-drawer">
        <div class="md-nav-header">
          <span class="md-nav-logo"><span class="msr">forum</span></span>
          <div class="md-nav-brand">
            BarkFluff
            <small>Admin Console</small>
          </div>
        </div>

        <nav style="display:flex;flex-direction:column;gap:2px;">
          ${NAV_ITEMS.map(it => buildItem(it, active)).join('')}
        </nav>

        <div class="md-nav-footer">
          <span class="avatar" id="md-nav-avatar">${initial}</span>
          <div class="info">
            <div class="name" id="md-nav-username">${userName}</div>
            <div class="meta" id="md-nav-usermeta">${userMeta}</div>
          </div>
        </div>
      </aside>
    `;
  }

  // -------- S3 submenu --------
  let s3Loaded = false;

  async function loadS3Submenu() {
    const submenu = document.getElementById('s3-submenu');
    if (!submenu) return;
    submenu.innerHTML = '<div style="padding:6px 16px 6px 56px;font-size:12px;color:var(--md-on-surface-variant);">Загрузка...</div>';
    try {
      const res = await fetch('/api/s3/buckets');
      if (!res.ok) throw new Error('failed');
      const buckets = await res.json();
      submenu.innerHTML = buckets.map(b => `
        <a class="md-nav-subitem" href="/s3-browser?bucket=${encodeURIComponent(b.id)}">
          <span class="msr size-18">folder</span>
          <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${b.displayName}</span>
        </a>
      `).join('');
      s3Loaded = true;
    } catch (e) {
      submenu.innerHTML = '<div style="padding:6px 16px 6px 56px;font-size:12px;color:var(--md-on-surface-variant);">Не удалось загрузить</div>';
    }
  }

  window.__toggleS3Menu = function () {
    const submenu = document.getElementById('s3-submenu');
    const chevronBtn = document.getElementById('s3-chevron-btn');
    if (!submenu || !chevronBtn) return;
    const isOpen = submenu.style.display !== 'none';
    if (isOpen) {
      submenu.style.display = 'none';
      chevronBtn.classList.remove('open');
      try { localStorage.setItem('s3-menu-open', 'false'); } catch (e) {}
    } else {
      submenu.style.display = 'block';
      chevronBtn.classList.add('open');
      try { localStorage.setItem('s3-menu-open', 'true'); } catch (e) {}
      if (!s3Loaded) loadS3Submenu();
    }
  };

  function initS3Persistence() {
    try {
      if (localStorage.getItem('s3-menu-open') === 'true') {
        window.__toggleS3Menu();
      }
    } catch (e) {}
  }

  // -------- Public sync helpers (called from page after auth check) --------

  window.mdNavSetUser = function (name, meta) {
    const a = document.getElementById('md-nav-avatar');
    const n = document.getElementById('md-nav-username');
    const m = document.getElementById('md-nav-usermeta');
    if (n && name) n.textContent = name;
    if (a && name) a.textContent = name.trim().charAt(0).toUpperCase();
    if (m && meta) m.textContent = meta;
  };

  // -------- Mobile off-canvas drawer --------
  function initMobileNav() {
    const shell = document.querySelector('.md-app-shell');
    const appBar = document.querySelector('.md-app-bar');
    if (!shell || !appBar) return;

    let toggle = appBar.querySelector('.md-nav-toggle');
    if (!toggle) {
      toggle = el('<button class="md-nav-toggle md-icon-btn" aria-label="Меню"><span class="msr">menu</span></button>');
      appBar.insertBefore(toggle, appBar.firstChild);
    }

    let scrim = document.querySelector('.md-nav-scrim');
    if (!scrim) {
      scrim = el('<div class="md-nav-scrim"></div>');
      shell.appendChild(scrim);
    }

    toggle.addEventListener('click', function () {
      shell.classList.toggle('nav-open');
    });
    scrim.addEventListener('click', function () {
      shell.classList.remove('nav-open');
    });
    document.addEventListener('click', function (e) {
      if (e.target.closest('.md-nav-item, .md-nav-subitem')) {
        shell.classList.remove('nav-open');
      }
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') shell.classList.remove('nav-open');
    });
  }

  // -------- Boot --------
  function boot() {
    const root = document.getElementById('md-nav-root');
    if (!root) return;
    render(root);
    initS3Persistence();
    initMobileNav();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
