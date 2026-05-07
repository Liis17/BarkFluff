/* =====================================================
   Services — Docker контейнеры + Seq статус (real data)
   ===================================================== */
(function () {
  "use strict";
  const { el, $, $$, icon, fmt, toast, fetchJson } = App;

  let refreshTimer = null;

  function fmtNum(v) { return v == null ? "—" : Number(v).toLocaleString("ru-RU"); }
  function relTime(iso) {
    if (!iso) return "—";
    const d = new Date(iso);
    if (isNaN(d.getTime())) return "—";
    const sec = Math.max(0, Math.floor((Date.now() - d.getTime()) / 1000));
    return fmt.rel(sec);
  }
  function shortName(s) {
    if (!s) return s;
    return s.replace(/^\//, "");
  }

  function build() {
    const root = $("#screen-services");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Сервисы</h1>
        <p class="page-sub">Docker-контейнеры BarkFluff и инфраструктурные сервисы. Действия применяются немедленно.</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="svcRefresh">${icon("refresh",12)} Обновить</button>
        <button class="btn btn-ghost btn-sm" id="svcRestartAll">${icon("loop",12)} Restart All</button>
        <button class="btn btn-primary btn-sm" id="svcUpdateAll">${icon("download",12)} Update All</button>
      </div>`;
    wrap.appendChild(head);

    const tableCard = el("div", { class: "card" });
    tableCard.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Контейнеры</h3>
          <span class="card-sub" id="svcSummary">—</span>
        </div>
      </div>
      <div style="overflow-x:auto;">
        <table class="data-table" id="svcTable" style="width:100%;border-collapse:collapse;">
          <thead>
            <tr>
              <th style="text-align:left;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Статус</th>
              <th style="text-align:left;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Имя</th>
              <th style="text-align:left;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Образ</th>
              <th style="text-align:left;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Состояние</th>
              <th style="text-align:left;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Создан</th>
              <th style="text-align:right;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">События</th>
              <th style="text-align:right;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Ошибки</th>
              <th style="text-align:left;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Last seen</th>
              <th style="text-align:right;padding:8px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.5px;">Действия</th>
            </tr>
          </thead>
          <tbody id="svcTbody">
            <tr><td colspan="9" style="text-align:center;color:var(--t-3);padding:20px;">Загрузка…</td></tr>
          </tbody>
        </table>
      </div>`;
    wrap.appendChild(tableCard);

    root.appendChild(wrap);

    $("#svcRefresh").addEventListener("click", () => loadAll());
    $("#svcRestartAll").addEventListener("click", () => bulkAction("restart-all", "Перезапустить все сервисы BarkFluff?"));
    $("#svcUpdateAll").addEventListener("click", () => bulkAction("update-all", "Обновить (pull + recreate) все сервисы BarkFluff? Это может занять несколько минут."));
  }

  async function bulkAction(kind, confirmText) {
    if (!confirm(confirmText)) return;
    try {
      const url = kind === "restart-all" ? "/api/docker/containers/restart-all" : "/api/docker/containers/update-all";
      const res = await fetch(url, { method: "POST" });
      const data = await res.json().catch(() => ({}));
      if (res.ok && (data.success !== false)) {
        toast(kind === "restart-all" ? "Перезапуск всех сервисов запущен" : "Обновление всех сервисов запущено", { kind: "ok" });
        setTimeout(loadAll, 2500);
      } else {
        toast("Ошибка: " + (data.message || `HTTP ${res.status}`), { kind: "err" });
      }
    } catch (e) {
      toast("Ошибка сети", { kind: "err" });
    }
  }

  async function containerAction(name, action, confirmText) {
    if (confirmText && !confirm(confirmText)) return;
    try {
      const res = await fetch(`/api/docker/containers/${encodeURIComponent(name)}/${action}`, { method: "POST" });
      const data = await res.json().catch(() => ({}));
      if (res.ok && (data.success !== false)) {
        toast(`${action}: ${name}`, { kind: "ok", sub: data.message });
        setTimeout(loadAll, 1500);
      } else {
        toast(`Ошибка ${action}: ` + (data.message || `HTTP ${res.status}`), { kind: "err" });
      }
    } catch (e) {
      toast("Ошибка сети", { kind: "err" });
    }
  }

  function statusDotClass(state) {
    if (!state) return "idle";
    const s = state.toLowerCase();
    if (s === "running") return "ok";
    if (s === "restarting" || s === "paused") return "warn";
    if (s === "exited" || s === "dead" || s === "stopped") return "err";
    return "idle";
  }

  function renderRow(container, statusByName) {
    const name = shortName(container.name);
    const state = container.state || "";
    const dot = statusDotClass(state);
    const status = statusByName[name] || statusByName[container.name] || null;
    const eventCount = status ? status.eventCount : null;
    const errorCount = status ? status.errorCount : null;
    const lastSeen = status ? status.lastSeen : null;

    const isRunning = state.toLowerCase() === "running";
    const tr = el("tr");
    tr.style.borderTop = "1px solid var(--line)";
    tr.innerHTML = `
      <td style="padding:8px;"><span class="status-dot ${dot}"></span><span class="mono t3" style="margin-left:6px;font-size:11px;">${state || "—"}</span></td>
      <td style="padding:8px;" class="mono">${name}</td>
      <td style="padding:8px;font-size:11px;color:var(--t-3);" class="mono">${container.image || "—"}</td>
      <td style="padding:8px;font-size:11px;color:var(--t-3);">${container.status || "—"}</td>
      <td style="padding:8px;font-size:11px;color:var(--t-3);" class="mono">${container.createdAt ? new Date(container.createdAt).toLocaleString("ru-RU") : "—"}</td>
      <td style="padding:8px;text-align:right;" class="mono">${fmtNum(eventCount)}</td>
      <td style="padding:8px;text-align:right;" class="mono ${errorCount > 0 ? 'err' : ''}">${fmtNum(errorCount)}</td>
      <td style="padding:8px;font-size:11px;color:var(--t-3);" class="mono">${lastSeen ? relTime(lastSeen) : "—"}</td>
      <td style="padding:8px;text-align:right;white-space:nowrap;">
        <button class="btn btn-ghost btn-sm" data-act="restart" title="Restart">${icon("loop",12)}</button>
        ${isRunning
          ? `<button class="btn btn-ghost btn-sm" data-act="stop" title="Stop">${icon("pause",12)}</button>`
          : `<button class="btn btn-ghost btn-sm" data-act="start" title="Start">${icon("play",12)}</button>`}
        <button class="btn btn-ghost btn-sm" data-act="pull" title="Pull + recreate">${icon("download",12)}</button>
      </td>`;
    tr.querySelectorAll("button[data-act]").forEach(btn => {
      btn.addEventListener("click", () => {
        const act = btn.dataset.act;
        const isAdminPanel = name === "admin-panel";
        if (act === "restart") {
          if (isAdminPanel) {
            containerAction("admin-panel", "restart-own", "Перезапустить admin-panel? Соединение временно прервётся.");
            return;
          }
          containerAction(name, "restart", `Перезапустить контейнер ${name}?`);
        } else if (act === "stop") {
          containerAction(name, "stop", `Остановить контейнер ${name}?`);
        } else if (act === "start") {
          containerAction(name, "start");
        } else if (act === "pull") {
          if (isAdminPanel) {
            containerAction("admin-panel", "update-own", "Обновить admin-panel? Соединение временно прервётся.");
            return;
          }
          containerAction(name, "pull", `Pull + recreate контейнера ${name}?`);
        }
      });
    });
    return tr;
  }

  async function loadAll() {
    const tbody = $("#svcTbody");
    if (!tbody) return;
    try {
      const [containers, statuses] = await Promise.all([
        fetchJson("/api/docker/containers"),
        fetchJson("/api/seq/services/status?hours=24").catch(() => []),
      ]);
      const statusByName = {};
      (statuses || []).forEach(s => {
        statusByName[s.name] = s;
        const lower = (s.name || "").toLowerCase();
        const stripped = lower.replace(/^barkfluff\./, "");
        statusByName[stripped] = s;
      });

      tbody.innerHTML = "";
      const sorted = (containers || []).slice().sort((a, b) => (a.name || "").localeCompare(b.name || ""));
      const running = sorted.filter(c => (c.state || "").toLowerCase() === "running").length;
      const stopped = sorted.length - running;
      const summary = $("#svcSummary");
      if (summary) summary.textContent = `${sorted.length} контейнеров · ${running} running · ${stopped} stopped`;

      sorted.forEach(c => tbody.appendChild(renderRow(c, statusByName)));
      if (sorted.length === 0) {
        tbody.innerHTML = `<tr><td colspan="9" style="text-align:center;color:var(--t-3);padding:20px;">Нет контейнеров</td></tr>`;
      }
    } catch (e) {
      console.error("services load error", e);
      tbody.innerHTML = `<tr><td colspan="9" style="text-align:center;color:#ef4444;padding:20px;">Ошибка загрузки: ${e.message}</td></tr>`;
    }
  }

  function show() {
    loadAll();
    if (refreshTimer) clearInterval(refreshTimer);
    refreshTimer = setInterval(() => loadAll(), 15000);
  }

  App.registerScreen("services", { render: build, show });
  window.ScreenServices = { render: build };
})();
