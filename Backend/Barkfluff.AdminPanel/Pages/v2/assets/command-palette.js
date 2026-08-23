/* =============================================================
   BarkFluff Admin — Cmd+K command palette
   Быстрые действия (переходы по разделам) + поиск по
   username/userId/fileId/chatId. Работает на всех страницах,
   подключающих sidebar.js.
   ============================================================= */

(function () {
  const STYLE = `
    #cmdkScrim { align-items: flex-start; padding-top: 12vh; }
    .cmdk-dialog { max-width: 640px; }
    .cmdk-input-row {
      display: flex; align-items: center; gap: 12px;
      padding: 16px 20px;
      border-bottom: 1px solid var(--md-outline-variant);
    }
    .cmdk-input-row .msr { color: var(--md-on-surface-variant); font-size: 22px; }
    .cmdk-input-row input {
      flex: 1; border: none; outline: none; background: transparent;
      font-size: 16px; color: var(--md-on-surface); font-family: inherit;
    }
    .cmdk-input-row input::placeholder { color: var(--md-on-surface-variant); }
    .cmdk-hint {
      font-size: 11px; color: var(--md-on-surface-variant);
      border: 1px solid var(--md-outline-variant); border-radius: 4px;
      padding: 2px 6px; flex-shrink: 0;
    }
    .cmdk-list { max-height: 60vh; overflow-y: auto; padding: 8px; }
    .cmdk-item {
      display: flex; align-items: center; gap: 14px;
      padding: 10px 12px; border-radius: var(--md-shape-md);
      cursor: pointer;
    }
    .cmdk-item.selected, .cmdk-item:hover { background: var(--md-surface-container); }
    .cmdk-item .msr, .cmdk-item .bf-icon { color: var(--md-on-surface-variant); font-size: 20px; flex-shrink: 0; }
    .cmdk-item .cmdk-label { flex: 1; color: var(--md-on-surface); font-size: 14px; min-width: 0; }
    .cmdk-avatar {
      width: 36px; height: 36px; border-radius: 50%; flex-shrink: 0;
      background: var(--md-tertiary-container); color: var(--md-on-tertiary-container);
      display: inline-flex; align-items: center; justify-content: center;
      font-weight: 600; font-size: 13px; overflow: hidden;
    }
    .cmdk-avatar img { width: 100%; height: 100%; object-fit: cover; }
    .cmdk-name { font-weight: 500; color: var(--md-on-surface); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cmdk-sub { color: var(--md-on-surface-variant); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cmdk-section-title {
      font-size: 11px; font-weight: 600; letter-spacing: .5px; text-transform: uppercase;
      color: var(--md-on-surface-variant); padding: 12px 12px 4px;
    }
    .cmdk-result-card {
      display: flex; align-items: flex-start; gap: 14px;
      padding: 12px; border-radius: var(--md-shape-md);
      border: 1px solid var(--md-outline-variant);
      margin: 0 4px 8px;
    }
    .cmdk-members { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
    .cmdk-member { display: flex; align-items: center; gap: 8px; }
    .cmdk-empty { padding: 32px; text-align: center; color: var(--md-on-surface-variant); font-size: 14px; }
  `;

  function injectStyle() {
    const s = document.createElement('style');
    s.textContent = STYLE;
    document.head.appendChild(s);
  }

  function escapeHtml(t) {
    const d = document.createElement('div');
    d.textContent = t == null ? '' : String(t);
    return d.innerHTML;
  }

  function getInitials(f, l) {
    return ((f?.[0] || '') + (l?.[0] || '')).toUpperCase() || '?';
  }

  function avatarHtml(picture, f, l) {
    return picture
      ? `<img src="${escapeHtml(picture)}" alt="">`
      : escapeHtml(getInitials(f, l));
  }

  let scrim, input, actionsBox, resultsBox, listEl;
  let items = [];      // текущий видимый список (для клавиатурной навигации)
  let selectedIndex = -1;
  let debounceTimer = null;
  let requestToken = 0;

  function buildDom() {
    scrim = document.createElement('div');
    scrim.id = 'cmdkScrim';
    scrim.className = 'md-scrim';
    scrim.innerHTML = `
      <div class="md-dialog cmdk-dialog" style="padding:0;">
        <div class="cmdk-input-row">
          <span class="msr">search</span>
          <input id="cmdkInput" type="text" placeholder="Юзернейм, ID пользователя, ID файла, ID чата..." autocomplete="off">
          <span class="cmdk-hint">Esc</span>
        </div>
        <div id="cmdkActions" class="cmdk-list"></div>
        <div id="cmdkResults" class="cmdk-list" style="display:none;"></div>
      </div>
    `;
    document.body.appendChild(scrim);

    input = scrim.querySelector('#cmdkInput');
    actionsBox = scrim.querySelector('#cmdkActions');
    resultsBox = scrim.querySelector('#cmdkResults');

    scrim.addEventListener('click', e => { if (e.target === scrim) closePalette(); });
    input.addEventListener('input', onInput);
    input.addEventListener('keydown', onKeydown);
  }

  function openPalette() {
    if (document.querySelector('.md-scrim.open')) return;
    scrim.classList.add('open');
    input.value = '';
    renderActions('');
    resultsBox.style.display = 'none';
    actionsBox.style.display = '';
    setTimeout(() => input.focus(), 0);
  }

  function closePalette() {
    scrim.classList.remove('open');
  }

  function togglePalette() {
    scrim.classList.contains('open') ? closePalette() : openPalette();
  }

  // ---- Быстрые действия ----

  function renderActions(filter) {
    const can = window.BF && window.BF.can ? window.BF.can : function () { return false; };
    const navItems = (window.__mdNavItems || []).filter(it => !it.permission || can(it.permission));
    const q = filter.trim().toLowerCase();
    const filtered = q
      ? navItems.filter(it => it.label.toLowerCase().includes(q))
      : navItems;

    items = filtered.map(it => ({
      type: 'action',
      onSelect: () => { window.location.href = it.href; }
    }));
    selectedIndex = items.length ? 0 : -1;

    actionsBox.innerHTML = filtered.length
      ? `<div class="cmdk-section-title">Быстрые действия</div>` +
        filtered.map((it, i) => `
          <div class="cmdk-item${i === 0 ? ' selected' : ''}" data-index="${i}">
            ${window.bfIcon ? window.bfIcon(it.icon, 'size-20') : ''}
            <span class="cmdk-label">${escapeHtml(it.label)}</span>
          </div>
        `).join('')
      : `<div class="cmdk-empty">Ничего не найдено</div>`;

    actionsBox.querySelectorAll('.cmdk-item').forEach(el => {
      el.addEventListener('click', () => items[+el.dataset.index]?.onSelect());
    });
  }

  // ---- Поиск ----

  function classifyQuery(q) {
    if (/^\d+$/.test(q)) return 'numeric';
    if (/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(q)) return 'guid';
    return 'text';
  }

  function onInput(e) {
    const query = e.target.value.trim();
    clearTimeout(debounceTimer);

    if (!query) {
      resultsBox.style.display = 'none';
      actionsBox.style.display = '';
      renderActions('');
      return;
    }

    debounceTimer = setTimeout(() => runSearch(query), 250);
  }

  async function runSearch(query) {
    const token = ++requestToken;
    actionsBox.style.display = 'none';
    resultsBox.style.display = '';
    resultsBox.innerHTML = `<div class="cmdk-empty">Поиск...</div>`;

    const kind = classifyQuery(query);
    const blocks = [];

    try {
      if (kind === 'guid') {
        const [fileRes, chatRes] = await Promise.allSettled([
          BF.api(`/api/files/${encodeURIComponent(query)}`),
          BF.api(`/api/chats/${encodeURIComponent(query)}`)
        ]);
        if (fileRes.status === 'fulfilled' && fileRes.value.ok) {
          blocks.push({ kind: 'file', data: await fileRes.value.json() });
        }
        if (chatRes.status === 'fulfilled' && chatRes.value.ok) {
          blocks.push({ kind: 'chat', data: await chatRes.value.json() });
        }
      } else {
        const params = new URLSearchParams({ query, offset: 0, size: 8 });
        const res = await BF.api(`/api/users?${params}`);
        if (res.ok) {
          const data = await res.json();
          data.users.forEach(u => blocks.push({ kind: 'user', data: u }));
        }
      }
    } catch (e) { /* сеть недоступна — покажем "не найдено" */ }

    if (token !== requestToken) return; // устаревший ответ
    renderResults(blocks);
  }

  function renderResults(blocks) {
    if (!blocks.length) {
      resultsBox.innerHTML = `<div class="cmdk-empty">Ничего не найдено</div>`;
      items = [];
      selectedIndex = -1;
      return;
    }

    items = blocks.map(() => ({ type: 'result' }));
    selectedIndex = 0;

    resultsBox.innerHTML = blocks.map(renderResultCard).join('');
    const first = resultsBox.querySelector('.cmdk-result-card');
    if (first) first.classList.add('selected');
  }

  function renderResultCard(block) {
    if (block.kind === 'user') return userCard(block.data);
    if (block.kind === 'file') return fileCard(block.data);
    if (block.kind === 'chat') return chatCard(block.data);
    return '';
  }

  function userCard(u) {
    return `
      <div class="cmdk-result-card">
        <div class="cmdk-avatar">${avatarHtml(u.profilePicturePreview || u.profilePicture, u.firstName, u.lastName)}</div>
        <div style="min-width:0;flex:1;">
          <div class="cmdk-name">${escapeHtml(u.firstName)} ${escapeHtml(u.lastName)}</div>
          <div class="cmdk-sub">@${escapeHtml(u.username)} · ID: ${u.id}</div>
        </div>
      </div>
    `;
  }

  function fileCard(f) {
    const isImage = (f.type || '').toLowerCase().includes('image') && f.previewUrl;
    return `
      <div class="cmdk-result-card">
        <div class="cmdk-avatar" style="border-radius:${isImage ? 'var(--md-shape-sm)' : '50%'};">
          ${isImage ? `<img src="${escapeHtml(f.previewUrl)}" alt="">` : `<span class="msr">description</span>`}
        </div>
        <div style="min-width:0;flex:1;">
          <div class="cmdk-name">${escapeHtml(f.fileName || f.fileId)}</div>
          <div class="cmdk-sub">Файл · ${formatSize(f.fileSize)}${f.uploaders && f.uploaders.length ? ' · загрузил ' + f.uploaders.map(u => '@' + escapeHtml(u.username)).join(', ') : ''}</div>
        </div>
      </div>
    `;
  }

  function chatCard(c) {
    if (c.isGroup) {
      return `
        <div class="cmdk-result-card" style="display:block;">
          <div style="display:flex;align-items:center;gap:14px;">
            <div class="cmdk-avatar">${avatarHtml(c.picture, c.title, '')}</div>
            <div style="min-width:0;flex:1;">
              <div class="cmdk-name">${escapeHtml(c.title || 'Группа')}</div>
              <div class="cmdk-sub">Групповой чат · ${c.members.length} участников</div>
            </div>
          </div>
          <div class="cmdk-members">
            ${c.members.map(m => `
              <div class="cmdk-member">
                <div class="cmdk-avatar" style="width:24px;height:24px;font-size:11px;">${avatarHtml(m.profilePicturePreview, m.firstName, m.lastName)}</div>
                <span class="cmdk-sub">@${escapeHtml(m.username)}</span>
              </div>
            `).join('')}
          </div>
        </div>
      `;
    }

    const [a, b] = c.members || [];
    return `
      <div class="cmdk-result-card" style="display:block;">
        <div class="cmdk-sub" style="margin-bottom:8px;">Личный чат</div>
        <div class="cmdk-members">
          ${[a, b].filter(Boolean).map(m => `
            <div class="cmdk-member">
              <div class="cmdk-avatar" style="width:28px;height:28px;font-size:12px;">${avatarHtml(m.profilePicturePreview, m.firstName, m.lastName)}</div>
              <span class="cmdk-sub">@${escapeHtml(m.username)}</span>
            </div>
          `).join('')}
        </div>
      </div>
    `;
  }

  function formatSize(bytes) {
    if (!bytes) return '0 Б';
    const units = ['Б', 'КБ', 'МБ', 'ГБ'];
    let i = 0, n = bytes;
    while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
    return `${n.toFixed(n < 10 && i > 0 ? 1 : 0)} ${units[i]}`;
  }

  // ---- Клавиатурная навигация ----

  function visibleItemEls() {
    const box = resultsBox.style.display === 'none' ? actionsBox : resultsBox;
    return Array.from(box.querySelectorAll('.cmdk-item, .cmdk-result-card'));
  }

  function moveSelection(delta) {
    const els = visibleItemEls();
    if (!els.length) return;
    els.forEach(el => el.classList.remove('selected'));
    selectedIndex = (selectedIndex + delta + els.length) % els.length;
    els[selectedIndex].classList.add('selected');
    els[selectedIndex].scrollIntoView({ block: 'nearest' });
  }

  function onKeydown(e) {
    if (e.key === 'Escape') { closePalette(); return; }
    if (e.key === 'ArrowDown') { e.preventDefault(); moveSelection(1); return; }
    if (e.key === 'ArrowUp') { e.preventDefault(); moveSelection(-1); return; }
    if (e.key === 'Enter') {
      e.preventDefault();
      if (resultsBox.style.display === 'none' && items[selectedIndex]?.onSelect) {
        items[selectedIndex].onSelect();
      }
    }
  }

  // ---- Глобальный шорткат ----

  function onGlobalKeydown(e) {
    const isK = e.key === 'k' || e.key === 'K';
    if ((e.metaKey || e.ctrlKey) && isK) {
      if (!scrim.classList.contains('open') && document.querySelector('.md-scrim.open')) {
        return; // уже открыта другая модалка — не стекаем
      }
      e.preventDefault();
      togglePalette();
    }
  }

  function boot() {
    injectStyle();
    buildDom();
    document.addEventListener('keydown', onGlobalKeydown);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
