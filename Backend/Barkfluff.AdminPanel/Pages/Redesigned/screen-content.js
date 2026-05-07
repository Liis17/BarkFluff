/* =====================================================
   S3 Browser + Badges + Stickers + Users (real backend)
   ===================================================== */
(function () {
  "use strict";
  const { el, $, $$, icon, fmt, toast, fetchJson } = App;

  function escapeHtml(s) {
    return String(s == null ? "" : s).replace(/[&<>"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]));
  }
  function formatBytes(bytes) {
    if (bytes == null) return "—";
    if (bytes === 0) return "0 Б";
    if (bytes < 1024) return bytes + " Б";
    if (bytes < 1024*1024) return (bytes/1024).toFixed(1) + " КБ";
    if (bytes < 1024*1024*1024) return (bytes/(1024*1024)).toFixed(1) + " МБ";
    return (bytes/(1024*1024*1024)).toFixed(2) + " ГБ";
  }
  function fmtDate(d) {
    if (!d) return "—";
    const dt = new Date(d);
    return isNaN(dt.getTime()) ? "—" : dt.toLocaleString("ru-RU");
  }
  function fmtDateShort(d) {
    if (!d) return "";
    const dt = new Date(d);
    return isNaN(dt.getTime()) ? "" : dt.toLocaleDateString("ru-RU");
  }

  function modal(title, contentHtml, onClose) {
    const overlay = el("div", { style: "position:fixed;inset:0;background:rgba(0,0,0,0.55);z-index:9999;display:flex;align-items:center;justify-content:center;padding:20px;" });
    const dlg = el("div", { style: "background:var(--panel-2);border:1px solid var(--line);border-radius:12px;width:min(720px,95vw);max-height:90vh;overflow:auto;color:var(--t-1);" });
    dlg.innerHTML = `
      <div style="display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-bottom:1px solid var(--line);position:sticky;top:0;background:var(--panel-2);">
        <h3 style="margin:0;font-size:15px;">${escapeHtml(title)}</h3>
        <button class="icon-btn" data-modal-close style="width:24px;height:24px;">${icon("x",12)}</button>
      </div>
      <div style="padding:18px;" data-modal-body></div>`;
    dlg.querySelector("[data-modal-body]").innerHTML = contentHtml;
    overlay.appendChild(dlg);
    document.body.appendChild(overlay);
    function close() { overlay.remove(); if (onClose) onClose(); }
    dlg.querySelector("[data-modal-close]").addEventListener("click", close);
    overlay.addEventListener("click", (e) => { if (e.target === overlay) close(); });
    return { close, body: dlg.querySelector("[data-modal-body]") };
  }

  // =====================================================
  // S3 BROWSER
  // =====================================================
  const S3State = {
    bucket: null,
    prefix: "",
    items: [],
    nextToken: null,
    isTruncated: false,
  };

  function buildS3Browser() {
    const root = $("#screen-s3-browser");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">S3 Browser</h1>
        <p class="page-sub">Просмотр содержимого бакета. Файл открывается через presigned URL (TTL 5 мин).</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="s3bRefresh">${icon("refresh",12)} Обновить</button>
      </div>`;
    wrap.appendChild(head);

    const card = el("div", { class: "card" });
    card.innerHTML = `
      <div class="card-head">
        <div class="card-head-l" style="flex:1;">
          <div id="s3bCrumb" class="mono" style="font-size:12px;color:var(--t-2);"></div>
        </div>
        <div class="card-head-r">
          <span class="t3 mono" id="s3bSummary">—</span>
        </div>
      </div>
      <div style="overflow-x:auto;">
        <table style="width:100%;border-collapse:collapse;font-size:12px;">
          <thead>
            <tr>
              <th style="text-align:left;padding:8px;font-size:10px;color:var(--t-3);text-transform:uppercase;">Имя</th>
              <th style="text-align:right;padding:8px;font-size:10px;color:var(--t-3);text-transform:uppercase;">Размер</th>
              <th style="text-align:left;padding:8px;font-size:10px;color:var(--t-3);text-transform:uppercase;">Изменён</th>
              <th style="text-align:right;padding:8px;font-size:10px;color:var(--t-3);text-transform:uppercase;">Действия</th>
            </tr>
          </thead>
          <tbody id="s3bTbody">
            <tr><td colspan="4" style="padding:20px;text-align:center;color:var(--t-3);">Выберите бакет</td></tr>
          </tbody>
        </table>
      </div>
      <div style="padding:10px;text-align:center;" id="s3bLoadMore"></div>`;
    wrap.appendChild(card);
    root.appendChild(wrap);

    $("#s3bRefresh").addEventListener("click", () => loadS3Page(true));
  }

  async function loadS3Page(reset = true) {
    const tbody = $("#s3bTbody");
    if (!tbody) return;
    if (!S3State.bucket) {
      tbody.innerHTML = `<tr><td colspan="4" style="padding:20px;text-align:center;color:var(--t-3);">Выберите бакет в боковой панели</td></tr>`;
      return;
    }
    const url = new URL(`/api/s3/buckets/${encodeURIComponent(S3State.bucket)}/objects`, window.location.origin);
    if (S3State.prefix) url.searchParams.set("prefix", S3State.prefix);
    url.searchParams.set("maxKeys", "200");
    if (!reset && S3State.nextToken) url.searchParams.set("continuationToken", S3State.nextToken);
    try {
      const data = await fetchJson(url.toString().replace(window.location.origin, ""));
      if (reset) S3State.items = [];
      const objs = data.objects || data.Objects || [];
      S3State.items = S3State.items.concat(objs);
      S3State.nextToken = data.nextContinuationToken || data.NextContinuationToken || null;
      S3State.isTruncated = !!(data.isTruncated || data.IsTruncated);
      renderS3Table();
    } catch (e) {
      tbody.innerHTML = `<tr><td colspan="4" style="padding:20px;text-align:center;color:#ef4444;">Ошибка: ${escapeHtml(e.message)}</td></tr>`;
    }
  }

  function renderS3Table() {
    const tbody = $("#s3bTbody");
    const crumb = $("#s3bCrumb");
    const summary = $("#s3bSummary");
    const loadMore = $("#s3bLoadMore");
    if (!tbody || !crumb) return;

    // crumb
    const parts = (S3State.prefix || "").split("/").filter(Boolean);
    let crumbHtml = `<a href="#" data-prefix="" style="color:var(--accent);text-decoration:none;">${escapeHtml(S3State.bucket || "—")}</a>`;
    let acc = "";
    parts.forEach(p => {
      acc += p + "/";
      crumbHtml += ` / <a href="#" data-prefix="${escapeHtml(acc)}" style="color:var(--accent);text-decoration:none;">${escapeHtml(p)}</a>`;
    });
    crumb.innerHTML = crumbHtml;
    crumb.querySelectorAll("a[data-prefix]").forEach(a => {
      a.addEventListener("click", (e) => { e.preventDefault(); S3State.prefix = a.dataset.prefix; loadS3Page(true); });
    });

    if (S3State.items.length === 0) {
      tbody.innerHTML = `<tr><td colspan="4" style="padding:20px;text-align:center;color:var(--t-3);">Пусто</td></tr>`;
      summary.textContent = "0 объектов";
      loadMore.innerHTML = "";
      return;
    }
    summary.textContent = `${S3State.items.length} объектов${S3State.isTruncated ? " (truncated)" : ""}`;

    tbody.innerHTML = "";
    S3State.items.forEach(o => {
      const tr = document.createElement("tr");
      tr.style.borderTop = "1px solid var(--line)";
      const isFolder = o.isFolder || o.IsFolder;
      const name = o.name || o.Name || o.key || o.Key;
      const key = o.key || o.Key;
      const size = isFolder ? "—" : formatBytes(o.size != null ? o.size : o.Size);
      const lm = isFolder ? "" : fmtDate(o.lastModified || o.LastModified);
      const iconName = isFolder ? "folder" : "file";
      tr.innerHTML = `
        <td style="padding:8px;">
          <span style="display:inline-flex;align-items:center;gap:8px;">
            <span style="color:${isFolder ? 'var(--accent)' : 'var(--t-2)'}">${icon(iconName, 14)}</span>
            <span class="mono" style="cursor:${isFolder ? 'pointer' : 'default'};${isFolder ? 'color:var(--accent);' : ''}" data-folder="${escapeHtml(key)}">${escapeHtml(name || "")}</span>
          </span>
        </td>
        <td style="padding:8px;text-align:right;" class="mono t3">${size}</td>
        <td style="padding:8px;font-size:11px;" class="mono t3">${lm}</td>
        <td style="padding:8px;text-align:right;">
          ${!isFolder ? `<button class="btn btn-ghost btn-sm" data-open="${escapeHtml(key)}">${icon("external", 12)} Открыть</button>` : ""}
        </td>`;
      if (isFolder) {
        const span = tr.querySelector("[data-folder]");
        span.addEventListener("click", () => { S3State.prefix = key; loadS3Page(true); });
      } else {
        const btn = tr.querySelector("[data-open]");
        if (btn) btn.addEventListener("click", () => openS3File(key));
      }
      tbody.appendChild(tr);
    });

    loadMore.innerHTML = "";
    if (S3State.isTruncated && S3State.nextToken) {
      const btn = el("button", { class: "btn btn-ghost btn-sm" }, "Загрузить ещё");
      btn.addEventListener("click", () => loadS3Page(false));
      loadMore.appendChild(btn);
    }
  }

  async function openS3File(key) {
    try {
      const data = await fetchJson(`/api/s3/buckets/${encodeURIComponent(S3State.bucket)}/presign?key=${encodeURIComponent(key)}`);
      if (data && data.url) window.open(data.url, "_blank");
    } catch (e) {
      toast("Ошибка получения URL: " + e.message, { kind: "err" });
    }
  }

  function showS3Browser(bucket) {
    if (bucket) {
      if (bucket !== S3State.bucket) S3State.prefix = "";
      S3State.bucket = bucket;
    }
    if (!S3State.bucket) {
      const tbody = $("#s3bTbody");
      if (tbody) tbody.innerHTML = `<tr><td colspan="4" style="padding:20px;text-align:center;color:var(--t-3);">Выберите бакет в боковой панели</td></tr>`;
      return;
    }
    loadS3Page(true);
  }

  App.registerScreen("s3-browser", { render: buildS3Browser, show: showS3Browser });
  window.ScreenS3Browser = { render: buildS3Browser };

  // =====================================================
  // BADGES
  // =====================================================
  let badgesList = [];

  function buildBadges() {
    const root = $("#screen-badges");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Бейджи</h1>
        <p class="page-sub">Награды, отображаемые рядом с никнеймами пользователей.</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="badgesRefresh">${icon("refresh",12)} Обновить</button>
        <button class="btn btn-primary btn-sm" id="badgesNew">${icon("plus",12)} Новый бейдж</button>
      </div>`;
    wrap.appendChild(head);

    const card = el("div", { class: "card" });
    card.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Список бейджей</h3>
          <span class="card-sub" id="badgesSummary">—</span>
        </div>
      </div>
      <div class="svc-grid" id="badgesGrid"><div class="t3" style="padding:16px;">Загрузка…</div></div>`;
    wrap.appendChild(card);

    root.appendChild(wrap);

    $("#badgesRefresh").addEventListener("click", () => loadBadges());
    $("#badgesNew").addEventListener("click", () => openBadgeForm(null));
  }

  async function loadBadges() {
    const grid = $("#badgesGrid");
    try {
      badgesList = await fetchJson("/api/badges");
      $("#badgesSummary").textContent = `${badgesList.length} бейджей`;
      if (!badgesList.length) {
        grid.innerHTML = `<div class="t3" style="padding:16px;">Нет бейджей</div>`;
        return;
      }
      grid.innerHTML = "";
      badgesList.forEach(b => {
        const cell = el("div", { class: "svc-cell" });
        cell.innerHTML = `
          <div class="svc-cell-head">
            <span class="status-dot ${b.isActive ? 'ok' : 'idle'}"></span>
            <span class="svc-cell-name mono">#${b.id}</span>
            <span class="svc-cell-ver mono t3">${b.isActive ? 'Активен' : 'Выключен'}</span>
          </div>
          <div style="display:flex;gap:10px;align-items:center;padding:8px 0;">
            <div style="width:48px;height:48px;border-radius:8px;background:var(--panel-3);display:flex;align-items:center;justify-content:center;flex-shrink:0;overflow:hidden;">
              ${b.imageUrl ? `<img src="${escapeHtml(b.imageUrl)}" style="width:100%;height:100%;object-fit:contain;"/>` : icon("star",18)}
            </div>
            <div style="flex:1;min-width:0;">
              <div style="font-weight:600;font-size:13px;color:var(--t-1);">${escapeHtml(b.name)}</div>
              <div class="t3" style="font-size:11px;line-height:1.4;margin-top:2px;">${escapeHtml(b.description || "")}</div>
              <div class="t3 mono" style="font-size:10px;margin-top:4px;">created ${fmtDateShort(b.createdDate)}</div>
            </div>
          </div>
          <div style="display:flex;gap:6px;justify-content:flex-end;">
            <button class="btn btn-ghost btn-sm" data-edit="${b.id}">${icon("edit",12)}</button>
            <button class="btn btn-ghost btn-sm" data-del="${b.id}">${icon("trash",12)}</button>
          </div>`;
        cell.querySelector("[data-edit]").addEventListener("click", () => openBadgeForm(b));
        cell.querySelector("[data-del]").addEventListener("click", () => deleteBadge(b));
        grid.appendChild(cell);
      });
    } catch (e) {
      grid.innerHTML = `<div style="padding:16px;color:#ef4444;">Ошибка: ${escapeHtml(e.message)}</div>`;
    }
  }

  function openBadgeForm(badge) {
    const isEdit = !!badge;
    const body = `
      <form id="badgeForm" enctype="multipart/form-data" style="display:flex;flex-direction:column;gap:12px;">
        <label class="field">
          <span class="field-label">Название</span>
          <div class="field-input">
            <input type="text" name="name" required value="${badge ? escapeHtml(badge.name) : ""}" />
          </div>
        </label>
        <label class="field">
          <span class="field-label">Описание</span>
          <div class="field-input">
            <input type="text" name="description" value="${badge ? escapeHtml(badge.description || "") : ""}" />
          </div>
        </label>
        <label style="display:flex;align-items:center;gap:8px;">
          <input type="checkbox" name="isActive" ${!badge || badge.isActive ? "checked" : ""}/> Активен
        </label>
        <label class="field">
          <span class="field-label">Картинка ${isEdit ? "(оставьте пустым чтобы не менять)" : "(PNG, требуется)"}</span>
          <input type="file" name="image" accept="image/png" ${isEdit ? "" : "required"} />
        </label>
        ${badge && badge.imageUrl ? `<img src="${escapeHtml(badge.imageUrl)}" style="max-width:100px;max-height:100px;border-radius:8px;background:var(--panel-3);"/>` : ""}
        <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:8px;">
          <button type="button" class="btn btn-ghost btn-sm" data-modal-close>Отмена</button>
          <button type="submit" class="btn btn-primary btn-sm">${isEdit ? "Сохранить" : "Создать"}</button>
        </div>
      </form>`;
    const m = modal(isEdit ? `Редактировать бейдж #${badge.id}` : "Новый бейдж", body);
    const form = m.body.querySelector("#badgeForm");
    m.body.querySelector("[data-modal-close]").addEventListener("click", () => m.close());
    form.addEventListener("submit", async (e) => {
      e.preventDefault();
      const fd = new FormData(form);
      fd.set("isActive", form.isActive.checked ? "true" : "false");
      // Если редактирование и картинка не выбрана — удалим поле image (multipart с пустым файлом не отправится)
      if (isEdit) {
        const f = fd.get("image");
        if (!f || (f instanceof File && f.size === 0)) fd.delete("image");
      }
      const url = isEdit ? `/api/badges/${badge.id}` : "/api/badges";
      const method = isEdit ? "PUT" : "POST";
      try {
        const res = await fetch(url, { method, body: fd });
        if (!res.ok) {
          let msg = `HTTP ${res.status}`;
          try { msg = (await res.json()).message || msg; } catch (_) {}
          toast("Ошибка: " + msg, { kind: "err" });
          return;
        }
        toast(isEdit ? "Бейдж обновлён" : "Бейдж создан", { kind: "ok" });
        m.close();
        loadBadges();
      } catch (err) {
        toast("Ошибка сети", { kind: "err" });
      }
    });
  }

  async function deleteBadge(badge) {
    if (!confirm(`Удалить бейдж "${badge.name}"?`)) return;
    try {
      const res = await fetch(`/api/badges/${badge.id}`, { method: "DELETE" });
      if (!res.ok) { toast("Ошибка удаления", { kind: "err" }); return; }
      toast("Бейдж удалён", { kind: "ok" });
      loadBadges();
    } catch (e) {
      toast("Ошибка сети", { kind: "err" });
    }
  }

  App.registerScreen("badges", { render: buildBadges, show: () => loadBadges() });
  window.ScreenBadges = { render: buildBadges };

  // =====================================================
  // STICKERS
  // =====================================================
  let stickerPacks = [];
  let currentPackId = null;

  function buildStickers() {
    const root = $("#screen-stickers");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Стикеры</h1>
        <p class="page-sub">Паки стикеров. Откройте пак, чтобы управлять содержимым.</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="stRefresh">${icon("refresh",12)} Обновить</button>
        <button class="btn btn-primary btn-sm" id="stNewPack">${icon("plus",12)} Новый пак</button>
      </div>`;
    wrap.appendChild(head);

    const card = el("div", { class: "card" });
    card.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Паки</h3>
          <span class="card-sub" id="stSummary">—</span>
        </div>
      </div>
      <div class="svc-grid" id="stGrid"><div class="t3" style="padding:16px;">Загрузка…</div></div>`;
    wrap.appendChild(card);
    root.appendChild(wrap);

    $("#stRefresh").addEventListener("click", () => loadPacks());
    $("#stNewPack").addEventListener("click", () => openPackForm(null));
  }

  async function loadPacks() {
    const grid = $("#stGrid");
    try {
      const data = await fetchJson("/api/stickers/packs");
      stickerPacks = data.packs || data.Packs || [];
      $("#stSummary").textContent = `${stickerPacks.length} паков`;
      if (!stickerPacks.length) {
        grid.innerHTML = `<div class="t3" style="padding:16px;">Нет паков</div>`;
        return;
      }
      grid.innerHTML = "";
      stickerPacks.forEach(p => {
        const cell = el("div", { class: "svc-cell", style: "cursor:pointer;" });
        cell.innerHTML = `
          <div class="svc-cell-head">
            <span class="status-dot ok"></span>
            <span class="svc-cell-name mono">${escapeHtml(p.id)}</span>
            <span class="svc-cell-ver mono t3">${p.stickerCount || 0} стикеров</span>
          </div>
          <div style="display:flex;gap:10px;align-items:center;padding:8px 0;">
            <div style="width:64px;height:64px;border-radius:8px;background:var(--panel-3);display:flex;align-items:center;justify-content:center;flex-shrink:0;overflow:hidden;">
              ${p.coverUrl ? `<img src="${escapeHtml(p.coverUrl)}" style="width:100%;height:100%;object-fit:contain;"/>` : icon("sticker",18)}
            </div>
            <div style="flex:1;min-width:0;">
              <div style="font-weight:600;font-size:13px;color:var(--t-1);">${escapeHtml(p.name)}</div>
              <div class="t3" style="font-size:11px;margin-top:4px;">${escapeHtml(p.description || "")}</div>
            </div>
          </div>`;
        cell.addEventListener("click", () => openPackDetail(p.id));
        grid.appendChild(cell);
      });
    } catch (e) {
      grid.innerHTML = `<div style="padding:16px;color:#ef4444;">Ошибка: ${escapeHtml(e.message)}</div>`;
    }
  }

  function openPackForm(pack) {
    const isEdit = !!pack;
    const body = `
      <form id="packForm" enctype="multipart/form-data" style="display:flex;flex-direction:column;gap:12px;">
        <label class="field">
          <span class="field-label">Название</span>
          <div class="field-input"><input type="text" name="name" required value="${pack ? escapeHtml(pack.name) : ""}"/></div>
        </label>
        <label class="field">
          <span class="field-label">Описание</span>
          <div class="field-input"><input type="text" name="description" value="${pack ? escapeHtml(pack.description || "") : ""}"/></div>
        </label>
        <label class="field">
          <span class="field-label">Обложка ${isEdit ? "(оставьте пустым чтобы не менять)" : "(требуется)"}</span>
          <input type="file" name="image" accept="image/*" ${isEdit ? "" : "required"} />
        </label>
        <div style="display:flex;justify-content:flex-end;gap:8px;">
          <button type="button" class="btn btn-ghost btn-sm" data-modal-close>Отмена</button>
          <button type="submit" class="btn btn-primary btn-sm">${isEdit ? "Сохранить" : "Создать"}</button>
        </div>
      </form>`;
    const m = modal(isEdit ? "Редактировать пак" : "Новый пак", body);
    m.body.querySelector("[data-modal-close]").addEventListener("click", () => m.close());
    m.body.querySelector("#packForm").addEventListener("submit", async (e) => {
      e.preventDefault();
      const fd = new FormData(e.target);
      const url = isEdit ? `/api/stickers/packs/${pack.id}` : "/api/stickers/packs";
      const method = isEdit ? "PUT" : "POST";
      if (isEdit) {
        const f = fd.get("image");
        if (!f || (f instanceof File && f.size === 0)) fd.delete("image");
      }
      try {
        const res = await fetch(url, { method, body: fd });
        if (!res.ok) { toast("Ошибка", { kind: "err" }); return; }
        toast(isEdit ? "Пак обновлён" : "Пак создан", { kind: "ok" });
        m.close();
        loadPacks();
      } catch (_) { toast("Ошибка сети", { kind: "err" }); }
    });
  }

  async function openPackDetail(packId) {
    currentPackId = packId;
    let data;
    try { data = await fetchJson(`/api/stickers/packs/${encodeURIComponent(packId)}`); }
    catch (e) { toast("Ошибка: " + e.message, { kind: "err" }); return; }
    const pack = data.pack || data.Pack || data;
    const stickers = data.stickers || data.Stickers || (pack && pack.stickers) || [];
    const body = `
      <div style="display:flex;gap:16px;margin-bottom:16px;">
        <div style="width:80px;height:80px;border-radius:8px;background:var(--panel-3);overflow:hidden;display:flex;align-items:center;justify-content:center;">
          ${pack.coverUrl ? `<img src="${escapeHtml(pack.coverUrl)}" style="width:100%;height:100%;object-fit:contain;"/>` : icon("sticker",24)}
        </div>
        <div style="flex:1;">
          <div style="font-size:15px;font-weight:600;">${escapeHtml(pack.name)}</div>
          <div class="t3" style="font-size:12px;margin-top:4px;">${escapeHtml(pack.description || "")}</div>
          <div style="display:flex;gap:6px;margin-top:8px;">
            <button class="btn btn-ghost btn-sm" id="packEditBtn">${icon("edit",12)} Изменить</button>
            <button class="btn btn-ghost btn-sm" id="packDelBtn">${icon("trash",12)} Удалить</button>
          </div>
        </div>
      </div>
      <h4 style="margin:0 0 8px;font-size:13px;">Стикеры (${stickers.length})</h4>
      <div id="stickersGrid" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:10px;"></div>
      <div style="margin-top:16px;border-top:1px solid var(--line);padding-top:12px;">
        <h4 style="margin:0 0 8px;font-size:13px;">Добавить стикер</h4>
        <form id="stickerAddForm" enctype="multipart/form-data" style="display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap;">
          <label class="field" style="flex:0 0 120px;">
            <span class="field-label">Emoji</span>
            <div class="field-input"><input type="text" name="emoji" required placeholder="😀" /></div>
          </label>
          <label class="field" style="flex:1;min-width:200px;">
            <span class="field-label">Картинка</span>
            <input type="file" name="image" accept="image/*" required />
          </label>
          <button type="submit" class="btn btn-primary btn-sm">${icon("plus",12)} Добавить</button>
        </form>
      </div>`;
    const m = modal(`Пак: ${pack.name}`, body, () => { currentPackId = null; });

    const grid = m.body.querySelector("#stickersGrid");
    if (stickers.length === 0) {
      grid.innerHTML = `<div class="t3" style="grid-column:1/-1;font-size:12px;">Нет стикеров</div>`;
    } else {
      stickers.forEach(s => {
        const card = el("div", { style: "border:1px solid var(--line);border-radius:8px;padding:8px;text-align:center;background:var(--panel-3);" });
        card.innerHTML = `
          <div style="height:80px;display:flex;align-items:center;justify-content:center;">
            ${s.imageUrl ? `<img src="${escapeHtml(s.imageUrl)}" style="max-width:100%;max-height:100%;"/>` : icon("sticker", 24)}
          </div>
          <div style="font-size:18px;margin:6px 0;">${escapeHtml(s.emoji || "")}</div>
          <button class="btn btn-ghost btn-sm" data-del-sticker="${s.id}">${icon("trash",10)}</button>`;
        card.querySelector("[data-del-sticker]").addEventListener("click", async () => {
          if (!confirm(`Удалить стикер ${s.emoji}?`)) return;
          try {
            const res = await fetch(`/api/stickers/${s.id}`, { method: "DELETE" });
            if (!res.ok) { toast("Ошибка", { kind: "err" }); return; }
            toast("Удалён", { kind: "ok" });
            m.close();
            openPackDetail(packId);
          } catch (_) { toast("Ошибка сети", { kind: "err" }); }
        });
        grid.appendChild(card);
      });
    }

    m.body.querySelector("#packEditBtn").addEventListener("click", () => { m.close(); openPackForm(pack); });
    m.body.querySelector("#packDelBtn").addEventListener("click", async () => {
      if (!confirm(`Удалить пак "${pack.name}"?`)) return;
      try {
        const res = await fetch(`/api/stickers/packs/${pack.id}`, { method: "DELETE" });
        if (!res.ok) { toast("Ошибка", { kind: "err" }); return; }
        toast("Пак удалён", { kind: "ok" });
        m.close();
        loadPacks();
      } catch (_) { toast("Ошибка сети", { kind: "err" }); }
    });

    m.body.querySelector("#stickerAddForm").addEventListener("submit", async (e) => {
      e.preventDefault();
      const fd = new FormData(e.target);
      try {
        const res = await fetch(`/api/stickers/packs/${pack.id}/stickers`, { method: "POST", body: fd });
        if (!res.ok) { toast("Ошибка добавления", { kind: "err" }); return; }
        toast("Стикер добавлен", { kind: "ok" });
        m.close();
        openPackDetail(packId);
      } catch (_) { toast("Ошибка сети", { kind: "err" }); }
    });
  }

  App.registerScreen("stickers", { render: buildStickers, show: () => loadPacks() });
  window.ScreenStickers = { render: buildStickers };

  // =====================================================
  // USERS
  // =====================================================
  const UsersState = {
    list: [],
    offset: 0,
    total: 0,
    query: "",
    loading: false,
    pageSize: 30,
  };

  function buildUsers() {
    const root = $("#screen-users");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Юзеры</h1>
        <p class="page-sub">Поиск пользователей и управление профилями.</p>
      </div>
      <div class="page-head-r">
        <input id="usersSearch" type="text" placeholder="поиск по имени / username / id…" style="background:var(--panel-2);color:var(--t-1);border:1px solid var(--line);border-radius:6px;padding:6px 12px;font-size:12px;min-width:280px;" />
      </div>`;
    wrap.appendChild(head);

    const card = el("div", { class: "card" });
    card.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Список</h3>
          <span class="card-sub" id="usersSummary">—</span>
        </div>
      </div>
      <div id="usersList" style="display:flex;flex-direction:column;gap:6px;padding:8px 0;"></div>
      <div id="usersLoadMore" style="text-align:center;padding:10px;"></div>`;
    wrap.appendChild(card);
    root.appendChild(wrap);

    let timer = null;
    $("#usersSearch").addEventListener("input", (e) => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        UsersState.query = e.target.value.trim();
        loadUsers(true);
      }, 300);
    });
  }

  async function loadUsers(reset = true) {
    if (UsersState.loading) return;
    UsersState.loading = true;
    if (reset) { UsersState.offset = 0; UsersState.list = []; }
    try {
      const params = new URLSearchParams({ query: UsersState.query, offset: String(UsersState.offset), size: String(UsersState.pageSize) });
      const data = await fetchJson(`/api/users?${params.toString()}`);
      UsersState.total = data.totalCount || 0;
      UsersState.list = UsersState.list.concat(data.users || []);
      UsersState.offset += (data.users || []).length;
      renderUsers();
    } catch (e) {
      $("#usersList").innerHTML = `<div style="color:#ef4444;padding:14px;">Ошибка: ${escapeHtml(e.message)}</div>`;
    } finally {
      UsersState.loading = false;
    }
  }

  function renderUsers() {
    const list = $("#usersList");
    const summary = $("#usersSummary");
    summary.textContent = `${UsersState.list.length} из ${UsersState.total}`;
    if (!UsersState.list.length) {
      list.innerHTML = `<div class="t3" style="padding:14px;text-align:center;">Не найдено</div>`;
      $("#usersLoadMore").innerHTML = "";
      return;
    }
    list.innerHTML = "";
    UsersState.list.forEach(u => {
      const row = el("div", { style: "display:flex;align-items:center;gap:12px;padding:8px 12px;border:1px solid var(--line);border-radius:8px;cursor:pointer;background:var(--panel-3);" });
      const initials = ((u.firstName || "")[0] || "") + ((u.lastName || "")[0] || "");
      const badges = (u.badges || []).map(ub => ub.badge && ub.badge.imageUrl
        ? `<img src="${escapeHtml(ub.badge.imageUrl)}" title="${escapeHtml(ub.badge.name)}" style="width:16px;height:16px;object-fit:contain;"/>`
        : "").join("");
      const avatar = u.profilePicturePreview || u.profilePicture
        ? `<img src="${escapeHtml(u.profilePicturePreview || u.profilePicture)}" style="width:100%;height:100%;object-fit:cover;"/>`
        : `<span style="font-size:11px;font-weight:600;color:var(--t-2);">${initials.toUpperCase() || '?'}</span>`;
      row.innerHTML = `
        <div style="width:36px;height:36px;border-radius:50%;background:var(--panel-2);display:flex;align-items:center;justify-content:center;overflow:hidden;flex-shrink:0;">${avatar}</div>
        <div style="flex:1;min-width:0;">
          <div style="display:flex;align-items:center;gap:6px;">
            <span style="font-weight:500;color:var(--t-1);font-size:13px;">${escapeHtml(u.firstName || "")} ${escapeHtml(u.lastName || "")}</span>
            ${badges}
          </div>
          <div class="t3 mono" style="font-size:11px;">@${escapeHtml(u.username || "")}</div>
        </div>
        <span class="t3 mono" style="font-size:11px;flex-shrink:0;">id ${u.id}</span>`;
      row.addEventListener("click", () => openUserModal(u.id));
      list.appendChild(row);
    });
    const loadMore = $("#usersLoadMore");
    loadMore.innerHTML = "";
    if (UsersState.offset < UsersState.total) {
      const btn = el("button", { class: "btn btn-ghost btn-sm" }, "Загрузить ещё");
      btn.addEventListener("click", () => loadUsers(false));
      loadMore.appendChild(btn);
    }
  }

  async function openUserModal(userId) {
    const m = modal(`Юзер #${userId}`, `<div class="t3">Загрузка…</div>`);
    let data;
    try { data = await fetchJson(`/api/users/${userId}`); }
    catch (e) { m.body.innerHTML = `<div style="color:#ef4444;">Ошибка: ${escapeHtml(e.message)}</div>`; return; }

    const p = data.profile || data;
    const initials = ((p.firstName || "")[0] || "") + ((p.lastName || "")[0] || "");
    const sessions = data.sessions || [];
    const tfa = data.twoFactor || data.TwoFactor;
    const st = data.storage || data.Storage;

    let html = `
      <section style="margin-bottom:18px;">
        <h4 style="margin:0 0 10px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Профиль</h4>
        <div style="display:flex;gap:14px;align-items:flex-start;">
          <div style="width:72px;height:72px;border-radius:50%;background:var(--panel-3);overflow:hidden;display:flex;align-items:center;justify-content:center;flex-shrink:0;">
            ${p.profilePicture ? `<img src="${escapeHtml(p.profilePicture)}" style="width:100%;height:100%;object-fit:cover;"/>` : `<span style="font-size:18px;font-weight:700;color:var(--t-2);">${initials.toUpperCase() || '?'}</span>`}
          </div>
          <div style="flex:1;font-size:12px;line-height:1.7;">
            <div><span class="t3">Имя:</span> ${escapeHtml(p.firstName || "")} ${escapeHtml(p.lastName || "")}</div>
            <div><span class="t3">Username:</span> @${escapeHtml(p.username || "")}</div>
            <div><span class="t3">Email:</span> ${escapeHtml((data.contacts || {}).email || "—")}</div>
            <div><span class="t3">ID:</span> ${p.id}</div>
            <div><span class="t3">Регистрация:</span> ${fmtDate(p.registrationDate)}</div>
            ${p.bio ? `<div><span class="t3">Био:</span> ${escapeHtml(p.bio)}</div>` : ""}
          </div>
        </div>
      </section>`;

    // BADGES
    html += `
      <section style="margin-bottom:18px;">
        <h4 style="margin:0 0 10px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Бейджи</h4>
        <div id="userBadgesList" style="display:flex;flex-direction:column;gap:6px;margin-bottom:8px;">
          ${(p.badges && p.badges.length > 0) ? p.badges.map(ub => `
            <div style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:var(--panel-3);border-radius:6px;">
              ${ub.badge.imageUrl ? `<img src="${escapeHtml(ub.badge.imageUrl)}" style="width:20px;height:20px;object-fit:contain;"/>` : ""}
              <span style="flex:1;font-size:12px;">${escapeHtml(ub.badge.name)}</span>
              <button class="btn btn-ghost btn-sm" data-rm-badge="${ub.badge.id}">${icon("x",10)}</button>
            </div>`).join("") : `<div class="t3" style="font-size:12px;">Нет бейджей</div>`}
        </div>
        <div style="display:flex;gap:6px;">
          <select id="addBadgeSel" style="flex:1;background:var(--panel-2);color:var(--t-1);border:1px solid var(--line);border-radius:6px;padding:5px 10px;font-size:12px;">
            <option value="">Выберите бейдж…</option>
          </select>
          <button class="btn btn-primary btn-sm" id="addBadgeBtn">Добавить</button>
        </div>
      </section>`;

    // SESSIONS
    html += `
      <section style="margin-bottom:18px;">
        <h4 style="margin:0 0 10px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Устройства / сессии</h4>
        ${sessions.length > 0 ? `<div style="display:flex;flex-direction:column;gap:6px;">
          ${sessions.map(s => `
            <div style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:var(--panel-3);border-radius:6px;">
              <span class="t3">${icon("server",12)}</span>
              <div style="flex:1;min-width:0;">
                <div style="font-size:12px;">${escapeHtml(s.customName || s.originalName || "Неизвестное")}</div>
                <div class="t3 mono" style="font-size:10px;">${escapeHtml(s.operationSystem || "")} ${escapeHtml(s.appName || "")}</div>
              </div>
              <span class="t3 mono" style="font-size:10px;">${fmtDateShort(s.createdAt)}</span>
              <button class="btn btn-ghost btn-sm" data-rm-session="${escapeHtml(s.deviceId)}">${icon("x",10)}</button>
            </div>`).join("")}
        </div>` : `<div class="t3" style="font-size:12px;">Нет сессий</div>`}
      </section>`;

    // 2FA
    if (tfa) {
      html += `
        <section style="margin-bottom:18px;">
          <h4 style="margin:0 0 10px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">2FA</h4>
          <div style="display:flex;flex-direction:column;gap:6px;">
            <div style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:var(--panel-3);border-radius:6px;">
              <span style="flex:1;font-size:12px;">Authenticator</span>
              <span class="mono" style="font-size:11px;color:${tfa.authenticatorEnabled ? '#22c55e' : 'var(--t-3)'};">${tfa.authenticatorEnabled ? 'on' : 'off'}</span>
              <button class="btn btn-ghost btn-sm" data-disable-otp="1" ${!tfa.authenticatorEnabled ? 'disabled' : ''}>Отключить</button>
            </div>
            <div style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:var(--panel-3);border-radius:6px;">
              <span style="flex:1;font-size:12px;">Email</span>
              <span class="mono" style="font-size:11px;color:${tfa.emailEnabled ? '#22c55e' : 'var(--t-3)'};">${tfa.emailEnabled ? 'on' : 'off'}</span>
              <button class="btn btn-ghost btn-sm" data-disable-otp="2" ${!tfa.emailEnabled ? 'disabled' : ''}>Отключить</button>
            </div>
          </div>
        </section>`;
    }

    // STORAGE
    if (st) {
      const usedMb = (st.totalUsedStorage / (1024*1024)).toFixed(1);
      const limitGb = (st.storageLimit / (1024*1024*1024)).toFixed(1);
      const pct = st.storageLimit > 0 ? Math.min((st.totalUsedStorage / st.storageLimit) * 100, 100) : 0;
      const overflow = st.totalUsedStorage > st.storageLimit;
      html += `
        <section style="margin-bottom:18px;">
          <h4 style="margin:0 0 10px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Хранилище</h4>
          <div style="display:flex;justify-content:space-between;font-size:12px;margin-bottom:4px;">
            <span>${usedMb} МБ / ${limitGb} ГБ</span>
            <span class="mono">${pct.toFixed(1)}%</span>
          </div>
          <div style="height:8px;background:var(--panel-3);border-radius:4px;overflow:hidden;">
            <div style="height:100%;width:${pct.toFixed(1)}%;background:${overflow ? '#ef4444' : '#5d6cff'};"></div>
          </div>
          <div style="margin-top:12px;display:flex;align-items:center;gap:8px;">
            <span class="t3" style="font-size:12px;">Лимит:</span>
            <input type="range" id="storLimit" min="1" max="250" step="1" value="${p.storageLimitGb || 1}" style="flex:1;"/>
            <span id="storLimitVal" class="mono" style="width:40px;text-align:right;font-size:12px;">${p.storageLimitGb || 1}</span>
            <span class="t3" style="font-size:12px;">ГБ</span>
            <button class="btn btn-primary btn-sm" id="storLimitBtn">Применить</button>
          </div>
        </section>`;
    }

    // AVATAR
    html += `
      <section style="margin-bottom:8px;">
        <h4 style="margin:0 0 10px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Аватар</h4>
        <form id="avatarForm" enctype="multipart/form-data" style="display:flex;gap:8px;align-items:center;">
          <input type="file" name="image" accept="image/*" required />
          <button type="submit" class="btn btn-primary btn-sm">Загрузить</button>
        </form>
      </section>`;

    m.body.innerHTML = html;

    // wire up
    m.body.querySelectorAll("[data-rm-badge]").forEach(b => b.addEventListener("click", async () => {
      if (!confirm("Снять бейдж?")) return;
      try {
        await fetch(`/api/users/${userId}/badges/${b.dataset.rmBadge}`, { method: "DELETE" });
        toast("Бейдж снят", { kind: "ok" });
        m.close(); openUserModal(userId);
      } catch (_) {}
    }));
    m.body.querySelectorAll("[data-rm-session]").forEach(b => b.addEventListener("click", async () => {
      if (!confirm("Завершить сессию?")) return;
      try {
        await fetch(`/api/users/${userId}/sessions/${encodeURIComponent(b.dataset.rmSession)}`, { method: "DELETE" });
        toast("Сессия завершена", { kind: "ok" });
        m.close(); openUserModal(userId);
      } catch (_) {}
    }));
    m.body.querySelectorAll("[data-disable-otp]").forEach(b => b.addEventListener("click", async () => {
      if (!confirm("Отключить 2FA?")) return;
      try {
        await fetch(`/api/users/${userId}/2fa/disable`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ otpType: parseInt(b.dataset.disableOtp, 10) }),
        });
        toast("Отключено", { kind: "ok" });
        m.close(); openUserModal(userId);
      } catch (_) {}
    }));
    const slider = m.body.querySelector("#storLimit");
    if (slider) {
      slider.addEventListener("input", () => { m.body.querySelector("#storLimitVal").textContent = slider.value; });
      m.body.querySelector("#storLimitBtn").addEventListener("click", async () => {
        try {
          const res = await fetch(`/api/users/${userId}/storage-limit`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ storageLimitGb: parseInt(slider.value, 10) }),
          });
          if (!res.ok) { toast("Ошибка", { kind: "err" }); return; }
          toast("Лимит обновлён", { kind: "ok" });
        } catch (_) { toast("Ошибка сети", { kind: "err" }); }
      });
    }
    const avatarForm = m.body.querySelector("#avatarForm");
    avatarForm.addEventListener("submit", async (e) => {
      e.preventDefault();
      const fd = new FormData(avatarForm);
      try {
        const res = await fetch(`/api/users/${userId}/avatar`, { method: "POST", body: fd });
        if (!res.ok) { toast("Ошибка", { kind: "err" }); return; }
        toast("Аватар обновлён", { kind: "ok" });
        m.close(); openUserModal(userId);
      } catch (_) { toast("Ошибка сети", { kind: "err" }); }
    });

    // Populate badge select
    try {
      const allBadges = await fetchJson("/api/badges");
      const sel = m.body.querySelector("#addBadgeSel");
      const existing = new Set((p.badges || []).map(ub => ub.badge.id));
      (allBadges || []).filter(b => b.isActive && !existing.has(b.id)).forEach(b => {
        const opt = document.createElement("option");
        opt.value = b.id; opt.textContent = b.name;
        sel.appendChild(opt);
      });
      m.body.querySelector("#addBadgeBtn").addEventListener("click", async () => {
        const id = parseInt(sel.value, 10);
        if (!id) return;
        try {
          await fetch(`/api/users/${userId}/badges`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ badgeId: id }),
          });
          toast("Бейдж выдан", { kind: "ok" });
          m.close(); openUserModal(userId);
        } catch (_) { toast("Ошибка", { kind: "err" }); }
      });
    } catch (_) {}
  }

  App.registerScreen("users", { render: buildUsers, show: () => loadUsers(true) });
  window.ScreenUsers = { render: buildUsers };
})();
