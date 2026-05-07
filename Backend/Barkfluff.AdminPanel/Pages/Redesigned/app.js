/* =====================================================
   BarkFluff Admin — App shell, router, kbar, tweaks, helpers
   ===================================================== */
(function () {
  "use strict";

  /* ---------- DOM helpers ---------- */
  const $  = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
  const el = (tag, attrs = {}, ...children) => {
    const node = document.createElement(tag);
    for (const k in attrs) {
      if (k === "class") node.className = attrs[k];
      else if (k === "html") node.innerHTML = attrs[k];
      else if (k === "style") node.setAttribute("style", attrs[k]);
      else if (k.startsWith("on") && typeof attrs[k] === "function") node.addEventListener(k.slice(2), attrs[k]);
      else if (attrs[k] !== false && attrs[k] != null) node.setAttribute(k, attrs[k]);
    }
    for (const c of children.flat()) {
      if (c == null || c === false) continue;
      node.appendChild(c.nodeType ? c : document.createTextNode(c));
    }
    return node;
  };
  const fmt = {
    n: (v) => v.toLocaleString("ru-RU"),
    bytes: (b) => {
      if (b < 1024) return b + " B";
      if (b < 1024 * 1024) return (b / 1024).toFixed(1) + " KB";
      if (b < 1024 * 1024 * 1024) return (b / 1024 / 1024).toFixed(1) + " MB";
      if (b < 1024 ** 4) return (b / 1024 ** 3).toFixed(2) + " GB";
      return (b / 1024 ** 4).toFixed(2) + " TB";
    },
    time: (d) => d.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit", second: "2-digit" }),
    ms: (ms) => ms < 1000 ? ms + "ms" : (ms / 1000).toFixed(2) + "s",
    rel: (sec) => {
      if (sec < 60) return sec + "с назад";
      if (sec < 3600) return Math.floor(sec / 60) + "м назад";
      if (sec < 86400) return Math.floor(sec / 3600) + "ч назад";
      return Math.floor(sec / 86400) + "д назад";
    },
  };

  /* ---------- icons (small library) ---------- */
  const ICONS = {
    search: '<circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/>',
    chevron: '<path d="M9 6l6 6-6 6"/>',
    chevronDown: '<path d="M6 9l6 6 6-6"/>',
    chevronUp: '<path d="M6 15l6-6 6 6"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    minus: '<path d="M5 12h14"/>',
    check: '<path d="M5 13l4 4 10-10"/>',
    x: '<path d="M6 6l12 12M18 6L6 18"/>',
    refresh: '<path d="M3 12a9 9 0 0 1 15-6.7L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-15 6.7L3 16"/><path d="M3 21v-5h5"/>',
    download: '<path d="M12 3v13M6 11l6 6 6-6"/><path d="M5 21h14"/>',
    upload: '<path d="M12 21V8M6 13l6-6 6 6"/><path d="M5 3h14"/>',
    eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    eyeOff: '<path d="M3 3l18 18"/><path d="M10.6 10.6a2 2 0 0 0 2.8 2.8"/><path d="M9.5 5.2A11 11 0 0 1 12 5c6.5 0 10 7 10 7-.7 1.4-1.7 2.6-2.9 3.6"/><path d="M6.6 6.6C4.2 8.2 2 12 2 12s3.5 7 10 7c1.6 0 3-.3 4.3-.9"/>',
    copy: '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
    trash: '<path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>',
    play: '<polygon points="5 3 19 12 5 21 5 3"/>',
    pause: '<rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/>',
    power: '<path d="M18.4 6.6a9 9 0 1 1-12.8 0"/><path d="M12 2v10"/>',
    upload_alt: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12"/>',
    folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    file: '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6"/>',
    image: '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="M21 15l-5-5L5 21"/>',
    filter: '<path d="M22 3H2l8 9.5V19l4 2v-8.5z"/>',
    sort: '<path d="M3 6h18M7 12h10M11 18h2"/>',
    arrowUp: '<path d="M12 19V5M5 12l7-7 7 7"/>',
    arrowDown: '<path d="M12 5v14M5 12l7 7 7-7"/>',
    arrowRight: '<path d="M5 12h14M13 5l7 7-7 7"/>',
    arrowLeft: '<path d="M19 12H5M11 19l-7-7 7-7"/>',
    bell: '<path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/>',
    info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>',
    warn: '<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/>',
    error: '<circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/>',
    cpu: '<rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M9 1v3M15 1v3M9 20v3M15 20v3M20 9h3M20 14h3M1 9h3M1 14h3"/>',
    db: '<ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M3 5v14c0 1.7 4 3 9 3s9-1.3 9-3V5"/><path d="M3 12c0 1.7 4 3 9 3s9-1.3 9-3"/>',
    lock: '<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
    user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7"/>',
    mail: '<rect x="2" y="4" width="20" height="16" rx="2"/><path d="M22 6l-10 7L2 6"/>',
    settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
    play_circle: '<circle cx="12" cy="12" r="10"/><polygon points="10 8 16 12 10 16 10 8"/>',
    sparkles: '<path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>',
    activity: '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>',
    server: '<rect x="3" y="4" width="18" height="7" rx="1.5"/><rect x="3" y="13" width="18" height="7" rx="1.5"/><circle cx="7" cy="7.5" r="0.8" fill="currentColor"/><circle cx="7" cy="16.5" r="0.8" fill="currentColor"/>',
    star: '<path d="M12 2l2.6 5.3 5.9.9-4.3 4.2 1 5.9-5.2-2.7-5.2 2.7 1-5.9L3.5 8.2l5.9-.9z"/>',
    sticker: '<path d="M3 14l8 8h3a8 8 0 0 0 8-8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2z"/><path d="M21 14h-4a3 3 0 0 0-3 3v4"/>',
    bucket: '<rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 11h.01M10 11h.01"/>',
    code: '<polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/>',
    hash: '<path d="M4 9h16M4 15h16M10 3L8 21M16 3l-2 18"/>',
    clock: '<circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>',
    zap: '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>',
    git: '<circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><circle cx="18" cy="12" r="2.5"/><path d="M6 8.5v7M8.5 12H15"/>',
    loop: '<path d="M17 1l4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3"/>',
    layers: '<polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/>',
    grid: '<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>',
    list: '<path d="M4 5h16M4 10h12M4 15h16M4 20h10"/>',
    paw: '<circle cx="6" cy="9" r="2"/><circle cx="10" cy="5" r="2"/><circle cx="14" cy="5" r="2"/><circle cx="18" cy="9" r="2"/><path d="M8 14c0-2 1-4 4-4s4 2 4 4-1 6-4 6-4-4-4-6z"/>',
    waves: '<path d="M2 12c2 0 2-3 4-3s2 3 4 3 2-3 4-3 2 3 4 3 2-3 4-3"/><path d="M2 18c2 0 2-3 4-3s2 3 4 3 2-3 4-3 2 3 4 3 2-3 4-3"/>',
    file_audio: '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6"/><path d="M9 18v-4l3-1v4"/>',
    video: '<rect x="2" y="6" width="14" height="12" rx="2"/><polygon points="22 8 16 12 22 16 22 8"/>',
    moreH: '<circle cx="5" cy="12" r="1.5" fill="currentColor"/><circle cx="12" cy="12" r="1.5" fill="currentColor"/><circle cx="19" cy="12" r="1.5" fill="currentColor"/>',
    moreV: '<circle cx="12" cy="5" r="1.5" fill="currentColor"/><circle cx="12" cy="12" r="1.5" fill="currentColor"/><circle cx="12" cy="19" r="1.5" fill="currentColor"/>',
    external: '<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><path d="M15 3h6v6M10 14L21 3"/>',
    edit: '<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.1 2.1 0 1 1 3 3L12 15l-4 1 1-4z"/>',
    box: '<path d="M21 8 12 3 3 8v8l9 5 9-5z"/><path d="M3 8l9 5 9-5"/><path d="M12 13v9"/>',
    gauge: '<path d="M12 2a10 10 0 1 0 10 10"/><path d="M12 12l5-5"/>',
    moon: '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.4 1.4M17.6 17.6L19 19M5 19l1.4-1.4M17.6 6.4L19 5"/>',
    ban: '<circle cx="12" cy="12" r="10"/><path d="M5 5l14 14"/>',
  };
  const icon = (name, size = 14, opts = {}) => {
    const s = ICONS[name] || "";
    const stroke = opts.stroke ?? 2;
    const fill = opts.fill ?? "none";
    return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="${fill}" stroke="currentColor" stroke-width="${stroke}" stroke-linecap="round" stroke-linejoin="round">${s}</svg>`;
  };

  /* ---------- seeded rng ---------- */
  function seedRng(seed) {
    let s = seed >>> 0;
    return () => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s / 0xffffffff;
    };
  }

  /* ---------- toast ---------- */
  const toastStack = () => $("#toastStack");
  function toast(msg, opts = {}) {
    const kind = opts.kind || "info"; // info | ok | warn | err
    const sub  = opts.sub;
    const node = el("div", { class: `toast toast-${kind}` },
      el("div", { class: "toast-dot" }),
      el("div", { class: "toast-body" },
        el("div", { class: "toast-msg" }, msg),
        sub ? el("div", { class: "toast-sub" }, sub) : null
      ),
      el("button", { class: "toast-close", onclick: () => node.remove() }, "✕")
    );
    toastStack().appendChild(node);
    requestAnimationFrame(() => node.classList.add("in"));
    setTimeout(() => {
      node.classList.add("out");
      setTimeout(() => node.remove(), 260);
    }, opts.duration || 3200);
  }

  /* ---------- screens registry & router ---------- */
  const screens = {};
  function registerScreen(id, mod) { screens[id] = mod; }

  const screenLabels = {
    "login":      "Login",
    "dashboard":  "Dashboard",
    "services":   "Services",
    "logs":       "Logs",
    "badges":     "Badges",
    "stickers":   "Stickers",
    "users":      "Users",
    "s3-storage": "S3 Storage",
    "s3-browser": "S3 Browser",
  };
  const screenCrumbs = {
    "login":      ["Auth", "Sign in"],
    "dashboard":  ["Observability", "Dashboard"],
    "services":   ["Observability", "Services"],
    "logs":       ["Observability", "Logs"],
    "badges":     ["Content", "Бейджи"],
    "stickers":   ["Content", "Стикеры"],
    "users":      ["Content", "Юзеры"],
    "s3-storage": ["Storage", "S3 buckets"],
    "s3-browser": ["Storage", "S3 browser"],
  };

  let currentScreen = null;
  let currentParam  = null;

  function go(routeRaw) {
    const route = routeRaw || "dashboard";
    const [id, param] = route.split("/");
    currentScreen = id;
    currentParam  = param || null;

    const isLogin = id === "login";
    document.body.classList.toggle("is-login", isLogin);
    $("#app").dataset.screen = id;

    // active sidebar
    $$("#sidebarNav .nav-item, #sidebarNav .nav-sub-item").forEach(a => {
      const aId = a.dataset.screen;
      const aBucket = a.dataset.bucket;
      let active = false;
      if (id === "s3-browser" && a.classList.contains("nav-sub-item")) {
        active = aBucket === param;
      } else if (a.classList.contains("nav-sub-item")) {
        active = false;
      } else {
        active = aId === id;
      }
      a.classList.toggle("active", active);
    });

    // crumb
    const cr = screenCrumbs[id] || ["—"];
    $("#crumb").innerHTML = '<span class="crumb-item muted">' + cr[0] + '</span>'
      + '<span class="crumb-sep">/</span>'
      + '<span class="crumb-item current" id="crumbCurrent">' + cr[1] + (param ? ' <span class="mono" style="color:var(--t-3);font-size:11px;margin-left:4px;">/ ' + param + '</span>' : '') + '</span>';

    // show screen
    $$(".screen").forEach(s => {
      const isThis = s.id === "screen-" + id;
      s.classList.toggle("active", isThis);
    });

    // notify screen
    const mod = screens[id];
    if (mod && mod.show) mod.show(param);

    if (location.hash !== "#" + route) {
      history.replaceState(null, "", "#" + route);
    }
  }

  /* ---------- kbar ---------- */
  const KBAR_ITEMS = [
    { kind: "go", label: "Перейти: Дашборд",       hint: "G D",      route: "dashboard" },
    { kind: "go", label: "Перейти: Сервисы",       hint: "G S",      route: "services" },
    { kind: "go", label: "Перейти: Логи",          hint: "G L",      route: "logs" },
    { kind: "go", label: "Перейти: S3 Хранилище",  hint: "G B",      route: "s3-storage" },
    { kind: "go", label: "Перейти: Бейджи",        hint: "",         route: "badges" },
    { kind: "go", label: "Перейти: Стикеры",       hint: "",         route: "stickers" },
    { kind: "go", label: "Перейти: Юзеры",         hint: "G U",      route: "users" },
    { kind: "bucket", label: "Бакет: profile-pictures",  route: "s3-browser/profile-pictures" },
    { kind: "bucket", label: "Бакет: message-images",    route: "s3-browser/message-images" },
    { kind: "bucket", label: "Бакет: message-videos",    route: "s3-browser/message-videos" },
    { kind: "bucket", label: "Бакет: message-audio",     route: "s3-browser/message-audio" },
    { kind: "bucket", label: "Бакет: message-documents", route: "s3-browser/message-documents" },
    { kind: "bucket", label: "Бакет: chat-pictures",     route: "s3-browser/chat-pictures" },
    { kind: "bucket", label: "Бакет: badge-images",      route: "s3-browser/badge-images" },
    { kind: "act",  label: "Перезапустить все сервисы",    hint: "restart-all", action: async () => {
        try { await fetch("/api/docker/containers/restart-all", { method: "POST" }); toast("Запущен перезапуск всех сервисов", { kind: "ok" }); }
        catch (e) { toast("Ошибка запуска", { kind: "err" }); }
      } },
    { kind: "act",  label: "Обновить все сервисы (pull)",  hint: "update-all", action: async () => {
        try { await fetch("/api/docker/containers/update-all", { method: "POST" }); toast("Запущено обновление всех сервисов", { kind: "ok" }); }
        catch (e) { toast("Ошибка запуска", { kind: "err" }); }
      } },
    { kind: "act",  label: "Выйти из админки",             hint: "logout",    action: async () => {
        try { await fetch("/api/auth/logout", { method: "POST" }); } catch (_) {}
        window.location.href = "/";
      } },
    { kind: "act",  label: "Вернуться на старую версию",   hint: "legacy",    action: () => {
        document.cookie = "ui_version=; path=/; max-age=0";
        window.location.href = "/";
      } },
  ];

  function openKbar() {
    $("#kbarOverlay").classList.add("open");
    $("#kbarInput").value = "";
    renderKbarList("");
    setTimeout(() => $("#kbarInput").focus(), 0);
  }
  function closeKbar() { $("#kbarOverlay").classList.remove("open"); }
  function renderKbarList(q) {
    const list = $("#kbarList");
    list.innerHTML = "";
    const queries = q.toLowerCase().split(/\s+/).filter(Boolean);
    const filtered = !queries.length ? KBAR_ITEMS : KBAR_ITEMS.filter(it => queries.every(t => it.label.toLowerCase().includes(t)));
    if (!filtered.length) {
      list.appendChild(el("div", { class: "kbar-empty" }, "Ничего не найдено"));
      return;
    }
    let groups = { go: [], bucket: [], act: [], doc: [] };
    filtered.forEach(it => groups[it.kind].push(it));
    const sections = [
      ["Навигация", groups.go, "navigation"],
      ["Бакеты",    groups.bucket, "bucket"],
      ["Действия",  groups.act, "act"],
      ["Документация", groups.doc, "doc"],
    ];
    let firstSet = false;
    sections.forEach(([title, items]) => {
      if (!items.length) return;
      list.appendChild(el("div", { class: "kbar-section-head" }, title));
      items.forEach(it => {
        const ic = it.kind === "go" ? "arrowRight" : it.kind === "bucket" ? "bucket" : it.kind === "doc" ? "external" : "zap";
        const node = el("button", { class: "kbar-item" + (!firstSet ? " active" : ""), onclick: () => kbarSelect(it) },
          el("span", { class: "kbar-item-icon", html: icon(ic, 14) }),
          el("span", { class: "kbar-item-label" }, it.label),
          it.hint ? el("span", { class: "kbar-item-hint mono" }, it.hint) : null
        );
        list.appendChild(node);
        firstSet = true;
      });
    });
  }
  function kbarSelect(it) {
    closeKbar();
    if (it.route) go(it.route);
    if (it.action) it.action();
  }
  function kbarMove(dir) {
    const items = $$(".kbar-item", $("#kbarList"));
    if (!items.length) return;
    const idx = items.findIndex(i => i.classList.contains("active"));
    items.forEach(i => i.classList.remove("active"));
    const next = items[(idx + dir + items.length) % items.length];
    next.classList.add("active");
    next.scrollIntoView({ block: "nearest" });
  }
  function kbarConfirm() {
    const it = $(".kbar-item.active", $("#kbarList"));
    if (it) it.click();
  }

  /* ---------- tweaks ---------- */
  const tweakState = { accent: "indigo", density: "cozy", radius: "default", live: "on" };
  function applyTweaks() {
    const app = $("#app");
    app.dataset.accent = tweakState.accent;
    app.dataset.density = tweakState.density;
    app.dataset.radius = tweakState.radius;
    app.dataset.live = tweakState.live;
    $("#tweakAccentVal").textContent = tweakState.accent;
    $("#tweakDensityVal").textContent = tweakState.density;
    $("#tweakRadiusVal").textContent = tweakState.radius;
    $("#tweakLiveVal").textContent = tweakState.live;
    $$("#tweakAccent .tweak-sw").forEach(s => s.classList.toggle("active", s.dataset.val === tweakState.accent));
    $$("#tweakDensity button").forEach(b => b.classList.toggle("active", b.dataset.val === tweakState.density));
    $$("#tweakRadius button").forEach(b => b.classList.toggle("active", b.dataset.val === tweakState.radius));
    $$("#tweakLive button").forEach(b => b.classList.toggle("active", b.dataset.val === tweakState.live));
  }
  function setTweak(k, v) {
    tweakState[k] = v;
    applyTweaks();
    try {
      window.parent.postMessage({ type: "__edit_mode_set_keys", edits: { [k]: v } }, "*");
    } catch (_) {}
  }
  function openTweaks() { $("#tweaksPanel").classList.add("open"); }
  function closeTweaks() {
    $("#tweaksPanel").classList.remove("open");
    try { window.parent.postMessage({ type: "__edit_mode_dismissed" }, "*"); } catch (_) {}
  }

  /* ---------- sidebar collapse ---------- */
  function toggleSidebar() {
    const app = $("#app");
    const cur = app.dataset.sidebar;
    app.dataset.sidebar = cur === "expanded" ? "collapsed" : "expanded";
  }

  /* ---------- init ---------- */
  function init() {
    // load tweak defaults
    try {
      const raw = $("#tweaks-defaults").textContent.replace(/\/\*EDITMODE-(BEGIN|END)\*\//g, "").trim();
      Object.assign(tweakState, JSON.parse(raw));
    } catch (e) {}
    applyTweaks();

    // sidebar nav
    $$("#sidebarNav .nav-item, #sidebarNav .nav-sub-item").forEach(a => {
      a.addEventListener("click", (e) => {
        e.preventDefault();
        if (a.classList.contains("nav-item-parent")) {
          a.classList.toggle("expanded");
          const sub = a.nextElementSibling;
          if (sub && sub.classList.contains("nav-sub")) sub.classList.toggle("collapsed");
          return;
        }
        const id = a.dataset.screen;
        const bucket = a.dataset.bucket;
        const route = bucket ? `${id}/${bucket}` : id;
        go(route);
      });
    });

    // collapse
    $("#sidebarCollapseBtn").addEventListener("click", toggleSidebar);
    $("#topbarMenuBtn").addEventListener("click", toggleSidebar);

    // logout (real)
    const doLogout = async () => {
      try { await fetch("/api/auth/logout", { method: "POST" }); } catch (_) {}
      window.location.href = "/";
    };
    $("#topbarLogout").addEventListener("click", doLogout);
    $("#sidebarFoot").addEventListener("click", doLogout);

    // switch back to legacy UI
    const switchToLegacy = $("#switchToLegacyBtn");
    if (switchToLegacy) {
      switchToLegacy.addEventListener("click", () => {
        document.cookie = "ui_version=; path=/; max-age=0";
        window.location.href = "/";
      });
    }

    // kbar
    $("#kbarTrigger").addEventListener("click", openKbar);
    $("#kbarOverlay").addEventListener("click", (e) => { if (e.target.id === "kbarOverlay") closeKbar(); });
    $("#kbarInput").addEventListener("input", (e) => renderKbarList(e.target.value));

    // tweaks
    $("#tweaksToggleBtn").addEventListener("click", () => $("#tweaksPanel").classList.toggle("open"));
    $("#tweaksCloseBtn").addEventListener("click", closeTweaks);
    $$("#tweakAccent .tweak-sw").forEach(s => s.addEventListener("click", () => setTweak("accent", s.dataset.val)));
    $$("#tweakDensity button").forEach(b => b.addEventListener("click", () => setTweak("density", b.dataset.val)));
    $$("#tweakRadius button").forEach(b => b.addEventListener("click", () => setTweak("radius", b.dataset.val)));
    $$("#tweakLive button").forEach(b => b.addEventListener("click", () => setTweak("live", b.dataset.val)));

    // edit-mode protocol
    window.addEventListener("message", (e) => {
      if (!e.data || typeof e.data !== "object") return;
      if (e.data.type === "__activate_edit_mode") $("#tweaksPanel").classList.add("open");
      if (e.data.type === "__deactivate_edit_mode") $("#tweaksPanel").classList.remove("open");
    });
    try { window.parent.postMessage({ type: "__edit_mode_available" }, "*"); } catch (_) {}

    // global keys
    document.addEventListener("keydown", (e) => {
      const k = e.key.toLowerCase();
      const isOverlay = $("#kbarOverlay").classList.contains("open");
      const isTyping = ["INPUT","TEXTAREA"].includes(document.activeElement?.tagName);
      if ((e.metaKey || e.ctrlKey) && k === "k") { e.preventDefault(); isOverlay ? closeKbar() : openKbar(); return; }
      if ((e.metaKey || e.ctrlKey) && e.key === "\\") { e.preventDefault(); toggleSidebar(); return; }
      if (e.key === "Escape" && isOverlay) { closeKbar(); return; }
      if (isOverlay) {
        if (e.key === "ArrowDown") { e.preventDefault(); kbarMove(1); }
        if (e.key === "ArrowUp")   { e.preventDefault(); kbarMove(-1); }
        if (e.key === "Enter")     { e.preventDefault(); kbarConfirm(); }
        return;
      }
      if (!isTyping) {
        if (k === "g") { App._gPressed = true; setTimeout(() => App._gPressed = false, 700); return; }
        if (App._gPressed) {
          if (k === "d") go("dashboard");
          if (k === "s") go("services");
          if (k === "l") go("logs");
          if (k === "b") go("s3-storage");
          if (k === "u") go("users");
          App._gPressed = false;
        }
      }
    });

    // hash route
    window.addEventListener("hashchange", () => go(location.hash.slice(1)));

    // pulse animation tick
    setInterval(() => {
      const dot = $("#systemPulse .pulse-dot");
      if (dot) dot.animate(
        [{ transform: "scale(1)", opacity: 1 }, { transform: "scale(2.4)", opacity: 0 }],
        { duration: 1400, easing: "ease-out" }
      );
    }, 1700);
  }

  /* ---------- spark/bar utils used by screens ---------- */
  function sparkPath(values, w, h, opts = {}) {
    if (!values.length) return "";
    const min = Math.min(...values);
    const max = Math.max(...values);
    const range = max - min || 1;
    const pad = opts.pad ?? 2;
    const innerW = w - pad * 2;
    const innerH = h - pad * 2;
    const stepX = innerW / (values.length - 1 || 1);
    const pts = values.map((v, i) => [pad + i * stepX, pad + innerH - ((v - min) / range) * innerH]);
    let d = "M" + pts.map(p => p[0].toFixed(1) + "," + p[1].toFixed(1)).join(" L");
    if (opts.area) {
      d += ` L${(pad + innerW).toFixed(1)},${(pad + innerH).toFixed(1)} L${pad.toFixed(1)},${(pad + innerH).toFixed(1)} Z`;
    }
    return d;
  }

  /* ---------- auth helpers ---------- */
  async function checkAuth() {
    try {
      const res = await fetch("/api/auth/me");
      if (!res.ok) return null;
      return await res.json();
    } catch (_) { return null; }
  }

  function fillAuthInfo(data) {
    if (!data) return;
    const name = data.name || data.Name;
    const created = data.createdAt || data.CreatedAt || data.created_at;
    const footName = $("#footName");
    const footMeta = $("#footMeta");
    if (footName && name) footName.textContent = name;
    if (footMeta) {
      const parts = [];
      if (data.id || data.Id) parts.push(("tok_" + (data.id || data.Id)).slice(0, 8));
      if (created) {
        try {
          const d = new Date(created);
          if (!isNaN(d.getTime())) parts.push(d.toLocaleDateString("ru-RU", { day: "2-digit", month: "2-digit", year: "2-digit" }));
        } catch (_) {}
      }
      if (parts.length) footMeta.textContent = parts.join(" · ");
    }
  }

  /* ---------- HTTP helpers ---------- */
  async function fetchJson(url, opts = {}) {
    const res = await fetch(url, opts);
    if (res.status === 401) {
      window.location.href = "/";
      throw new Error("unauthorized");
    }
    if (!res.ok) {
      let detail = "";
      try { detail = await res.text(); } catch (_) {}
      const err = new Error(`HTTP ${res.status}${detail ? ": " + detail.slice(0, 200) : ""}`);
      err.status = res.status;
      throw err;
    }
    if (res.status === 204) return null;
    const ct = res.headers.get("content-type") || "";
    if (ct.includes("application/json")) return await res.json();
    return await res.text();
  }

  /* ---------- public API ---------- */
  window.App = {
    init, go, registerScreen,
    el, $, $$, fmt, icon, ICONS, toast,
    seedRng, sparkPath,
    setTweak, getTweak: (k) => tweakState[k],
    onTweakChange: (cb) => { window.addEventListener("__tweak", e => cb(e.detail)); },
    checkAuth, fillAuthInfo, fetchJson,
  };
})();
