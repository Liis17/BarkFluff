/* =====================================================
   S3 Storage — список бакетов + credentials (real data)
   ===================================================== */
(function () {
  "use strict";
  const { el, $, $$, icon, fmt, toast, fetchJson } = App;

  let buckets = [];
  let credentials = null;

  function build() {
    const root = $("#screen-s3-storage");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Хранилище S3</h1>
        <p class="page-sub">Список бакетов проекта. Откройте бакет, чтобы просмотреть содержимое.</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="s3Refresh">${icon("refresh",12)} Обновить</button>
        <button class="btn btn-primary btn-sm" id="s3CredsBtn">${icon("lock",12)} Credentials</button>
      </div>`;
    wrap.appendChild(head);

    const grid = el("div", { class: "card", id: "s3GridCard" });
    grid.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Бакеты</h3>
          <span class="card-sub" id="s3Summary">—</span>
        </div>
      </div>
      <div class="svc-grid" id="s3BucketGrid">
        <div class="t3" style="padding:16px;">Загрузка…</div>
      </div>`;
    wrap.appendChild(grid);

    // creds modal
    const modal = el("div", { id: "credsModal", style: "display:none;position:fixed;inset:0;background:rgba(0,0,0,0.55);z-index:9999;align-items:center;justify-content:center;" });
    modal.innerHTML = `
      <div style="background:var(--panel-2);border:1px solid var(--line);border-radius:12px;width:min(640px,90vw);padding:20px;color:var(--t-1);">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px;">
          <h3 style="margin:0;font-size:16px;">S3 Credentials</h3>
          <button class="icon-btn" id="credsClose" style="width:24px;height:24px;">${icon("x",14)}</button>
        </div>
        <div id="credsBody" class="t3" style="font-size:12px;">Загрузка…</div>
      </div>`;
    wrap.appendChild(modal);

    root.appendChild(wrap);

    $("#s3Refresh").addEventListener("click", () => loadBuckets());
    $("#s3CredsBtn").addEventListener("click", () => openCreds());
    $("#credsClose").addEventListener("click", () => { $("#credsModal").style.display = "none"; });
    modal.addEventListener("click", (e) => { if (e.target.id === "credsModal") modal.style.display = "none"; });
  }

  async function loadBuckets() {
    const grid = $("#s3BucketGrid");
    if (!grid) return;
    try {
      buckets = await fetchJson("/api/s3/buckets");
      $("#s3Summary").textContent = `${buckets.length} бакетов`;
      grid.innerHTML = "";
      buckets.forEach(b => {
        const cell = el("a", { class: "svc-cell", href: `#s3-browser/${encodeURIComponent(b.id)}` });
        cell.innerHTML = `
          <div class="svc-cell-head">
            <span class="status-dot ok"></span>
            <span class="svc-cell-name mono">${b.id}</span>
          </div>
          <div style="padding:6px 0;font-size:13px;color:var(--t-1);">${b.displayName || b.id}</div>
          <div class="svc-cell-stats">
            <div><span class="svc-cell-l">id</span><span class="svc-cell-v mono">${b.id}</span></div>
          </div>`;
        cell.addEventListener("click", (e) => {
          e.preventDefault();
          App.go(`s3-browser/${b.id}`);
        });
        grid.appendChild(cell);
      });
      // sync sidebar buckets
      syncSidebarBuckets();
    } catch (e) {
      grid.innerHTML = `<div style="padding:16px;color:#ef4444;">Ошибка загрузки: ${e.message}</div>`;
    }
  }

  function syncSidebarBuckets() {
    const sub = document.getElementById("bucketSubNav");
    if (!sub) return;
    sub.innerHTML = "";
    buckets.forEach(b => {
      const a = document.createElement("a");
      a.className = "nav-sub-item";
      a.dataset.screen = "s3-browser";
      a.dataset.bucket = b.id;
      a.href = `#s3-browser/${b.id}`;
      a.innerHTML = `${b.displayName || b.id} <span class="nav-sub-meta mono">${b.id}</span>`;
      a.addEventListener("click", (e) => { e.preventDefault(); App.go(`s3-browser/${b.id}`); });
      sub.appendChild(a);
    });
  }

  async function openCreds() {
    const modal = $("#credsModal");
    const body = $("#credsBody");
    modal.style.display = "flex";
    body.innerHTML = "Загрузка…";
    try {
      credentials = await fetchJson("/api/configuration/s3-configuration");
      renderCreds(credentials);
    } catch (e) {
      body.innerHTML = `<span style="color:#ef4444;">Ошибка: ${e.message}</span>`;
    }
  }

  function renderCreds(creds) {
    const body = $("#credsBody");
    if (!creds || Object.keys(creds).length === 0) {
      body.innerHTML = "Нет настроенных бакетов";
      return;
    }
    let html = '<div style="display:flex;flex-direction:column;gap:14px;max-height:60vh;overflow-y:auto;">';
    Object.entries(creds).forEach(([bucketId, cfg]) => {
      const editedAt = cfg.editedAt ? new Date(cfg.editedAt).toLocaleString("ru-RU") : "—";
      const secretId = `secret-${bucketId.replace(/[^a-z0-9]/gi, "_")}`;
      html += `
        <div style="border:1px solid var(--line);border-radius:8px;padding:12px;background:var(--panel-3);">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;">
            <span class="mono" style="font-size:13px;color:var(--t-1);font-weight:600;">${bucketId}</span>
            <span class="mono t3" style="font-size:10px;">edited: ${editedAt}</span>
          </div>
          <div style="display:grid;grid-template-columns:120px 1fr;gap:6px;font-size:11px;font-family:var(--font-mono);">
            <span class="t3">ServiceUrl</span><span style="word-break:break-all;">${cfg.serviceUrl || "—"}</span>
            <span class="t3">BucketName</span><span>${cfg.bucketName || "—"}</span>
            <span class="t3">AccessKey</span><span style="word-break:break-all;">${cfg.accessKey || "—"}</span>
            <span class="t3">SecretKey</span>
            <span style="display:flex;gap:6px;align-items:center;">
              <span id="${secretId}" style="word-break:break-all;">••••••••</span>
              <button class="btn btn-ghost btn-sm" data-secret="${secretId}" data-val="${escapeAttr(cfg.secretKey || "")}">${icon("eye",10)}</button>
            </span>
          </div>
        </div>`;
    });
    html += "</div>";
    body.innerHTML = html;
    body.querySelectorAll("button[data-secret]").forEach(btn => {
      btn.addEventListener("click", () => {
        const span = document.getElementById(btn.dataset.secret);
        if (span.textContent === "••••••••") {
          span.textContent = btn.dataset.val || "";
          btn.innerHTML = icon("eyeOff", 10);
        } else {
          span.textContent = "••••••••";
          btn.innerHTML = icon("eye", 10);
        }
      });
    });
  }

  function escapeAttr(s) {
    return String(s || "").replace(/"/g, "&quot;").replace(/</g, "&lt;");
  }

  function show() {
    loadBuckets();
  }

  App.registerScreen("s3-storage", { render: build, show });
  window.ScreenS3Storage = { render: build, getBuckets: () => buckets };
})();
