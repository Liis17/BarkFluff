/* =====================================================
   Logs — реальные события из Seq (/api/seq/events)
   ===================================================== */
(function () {
  "use strict";
  const { el, $, $$, icon, fmt, toast, fetchJson } = App;

  let pollTimer = null;
  let isPaused = false;
  let currentLevel = "";
  let currentService = "";
  let currentSearch = "";
  let knownServices = [];
  let allEvents = [];
  let selectedEvent = null;

  const PAGE_SIZE = 80;

  function getLevel(e) { return e.Level || e.level || "Information"; }
  function getMessage(e) {
    return e.RenderedMessage || e.renderedMessage ||
           e.MessageTemplate || e.messageTemplate ||
           e.Message || e.message || "";
  }
  function getTimestamp(e) {
    return e.Timestamp || e.timestamp || e.TimestampUtc || "";
  }
  function getApplication(e) {
    const props = e.Properties || e.properties;
    if (props) {
      if (Array.isArray(props)) {
        const found = props.find(p => (p.Name || p.name) === "Application");
        if (found) return found.Value || found.value || "";
      } else if (props.Application) {
        return props.Application;
      }
    }
    return e.Application || "";
  }

  function levelBadge(level) {
    const l = (level || "").toLowerCase();
    let bg = "rgba(148,163,184,0.15)", color = "#94a3b8";
    if (l === "information" || l === "info") { bg = "rgba(93,108,255,0.15)"; color = "#5d6cff"; }
    else if (l === "warning" || l === "warn") { bg = "rgba(234,179,8,0.18)"; color = "#eab308"; }
    else if (l === "error" || l === "fatal" || l === "critical") { bg = "rgba(239,68,68,0.18)"; color = "#ef4444"; }
    else if (l === "debug" || l === "verbose" || l === "trace") { bg = "rgba(148,163,184,0.18)"; color = "#94a3b8"; }
    return `<span class="mono" style="background:${bg};color:${color};padding:2px 8px;border-radius:4px;font-size:10px;text-transform:uppercase;letter-spacing:0.5px;">${level || "—"}</span>`;
  }

  function build() {
    const root = $("#screen-logs");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Логи</h1>
        <p class="page-sub">События в реальном времени из Seq. Фильтрация по уровню, сервису и тексту.</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="logsPause">${icon("pause",12)} Пауза</button>
        <button class="btn btn-ghost btn-sm" id="logsRefresh">${icon("refresh",12)} Обновить</button>
      </div>`;
    wrap.appendChild(head);

    const filtersCard = el("div", { class: "card" });
    filtersCard.innerHTML = `
      <div class="card-head">
        <div class="card-head-l" style="flex:1;">
          <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;">
            <div class="seg seg-sm" id="logsLevelSeg">
              <button class="active" data-level="">Все</button>
              <button data-level="Information">INFO</button>
              <button data-level="Warning">WARN</button>
              <button data-level="Error">ERROR</button>
              <button data-level="Debug">DEBUG</button>
            </div>
            <select id="logsServiceSelect" style="background:var(--panel-2);color:var(--t-1);border:1px solid var(--line);border-radius:6px;padding:5px 10px;font-size:12px;font-family:var(--font-mono);">
              <option value="">Все сервисы</option>
            </select>
            <input id="logsSearch" type="text" placeholder="поиск по тексту…" style="flex:1;min-width:200px;background:var(--panel-2);color:var(--t-1);border:1px solid var(--line);border-radius:6px;padding:5px 10px;font-size:12px;" />
            <span class="t3 mono" id="logsCounter">—</span>
          </div>
        </div>
      </div>`;
    wrap.appendChild(filtersCard);

    const splitWrap = el("div", { style: "display:grid;grid-template-columns:1fr;gap:14px;" });
    const tableCard = el("div", { class: "card", id: "logsTableCard" });
    tableCard.innerHTML = `
      <div style="overflow-x:auto;max-height:65vh;overflow-y:auto;">
        <table style="width:100%;border-collapse:collapse;font-size:12px;">
          <thead style="position:sticky;top:0;background:var(--panel-2);z-index:1;">
            <tr>
              <th style="text-align:left;padding:6px 8px;font-size:10px;color:var(--t-3);text-transform:uppercase;width:160px;">Время</th>
              <th style="text-align:left;padding:6px 8px;font-size:10px;color:var(--t-3);text-transform:uppercase;width:90px;">Уровень</th>
              <th style="text-align:left;padding:6px 8px;font-size:10px;color:var(--t-3);text-transform:uppercase;width:180px;">Сервис</th>
              <th style="text-align:left;padding:6px 8px;font-size:10px;color:var(--t-3);text-transform:uppercase;">Сообщение</th>
            </tr>
          </thead>
          <tbody id="logsTbody">
            <tr><td colspan="4" style="text-align:center;padding:24px;color:var(--t-3);">Загрузка…</td></tr>
          </tbody>
        </table>
      </div>`;
    splitWrap.appendChild(tableCard);

    const detailCard = el("div", { class: "card", id: "logsDetailCard", style: "display:none;" });
    detailCard.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Детали события</h3>
          <span class="card-sub" id="logsDetailSub">—</span>
        </div>
        <div class="card-head-r">
          <button class="btn btn-ghost btn-sm" id="logsDetailCopy">${icon("copy",12)} Copy JSON</button>
          <button class="btn btn-ghost btn-sm" id="logsDetailClose">${icon("x",12)}</button>
        </div>
      </div>
      <pre id="logsDetailBody" style="margin:0;padding:14px;background:var(--panel-3);border-radius:8px;overflow:auto;max-height:50vh;font-size:11px;font-family:var(--font-mono);color:var(--t-1);"></pre>`;
    splitWrap.appendChild(detailCard);

    wrap.appendChild(splitWrap);
    root.appendChild(wrap);

    $("#logsRefresh").addEventListener("click", () => loadLogs());
    $("#logsPause").addEventListener("click", () => {
      isPaused = !isPaused;
      $("#logsPause").innerHTML = isPaused ? `${icon("play",12)} Продолжить` : `${icon("pause",12)} Пауза`;
    });
    $$("#logsLevelSeg button").forEach(b => {
      b.addEventListener("click", () => {
        $$("#logsLevelSeg button").forEach(x => x.classList.remove("active"));
        b.classList.add("active");
        currentLevel = b.dataset.level || "";
        loadLogs();
      });
    });
    $("#logsServiceSelect").addEventListener("change", (e) => {
      currentService = e.target.value;
      loadLogs();
    });
    let searchTimer = null;
    $("#logsSearch").addEventListener("input", (e) => {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(() => {
        currentSearch = e.target.value.trim();
        loadLogs();
      }, 350);
    });
    $("#logsDetailClose").addEventListener("click", () => closeDetail());
    $("#logsDetailCopy").addEventListener("click", async () => {
      if (!selectedEvent) return;
      try {
        await navigator.clipboard.writeText(JSON.stringify(selectedEvent, null, 2));
        toast("JSON скопирован", { kind: "ok" });
      } catch (_) { toast("Не удалось скопировать", { kind: "err" }); }
    });
  }

  async function loadServices() {
    if (knownServices.length > 0) return;
    try {
      const list = await fetchJson("/api/seq/services");
      knownServices = list || [];
      const sel = $("#logsServiceSelect");
      if (!sel) return;
      knownServices.forEach(s => {
        const opt = document.createElement("option");
        opt.value = s; opt.textContent = s;
        sel.appendChild(opt);
      });
    } catch (e) {
      console.error("services load error", e);
    }
  }

  async function loadLogs() {
    const tbody = $("#logsTbody");
    if (!tbody) return;
    try {
      const params = new URLSearchParams();
      params.set("count", String(PAGE_SIZE));
      if (currentLevel) params.set("level", currentLevel);
      if (currentService) params.set("application", currentService);
      if (currentSearch) params.set("search", currentSearch);
      const data = await fetchJson(`/api/seq/events?${params.toString()}`);
      const events = extractEvents(data);
      allEvents = events;
      const counter = $("#logsCounter");
      if (counter) counter.textContent = `${events.length} событий`;
      renderRows(events);
    } catch (e) {
      console.error("logs error", e);
      tbody.innerHTML = `<tr><td colspan="4" style="text-align:center;padding:20px;color:#ef4444;">Ошибка: ${e.message}</td></tr>`;
    }
  }

  function extractEvents(data) {
    if (Array.isArray(data)) return data;
    if (data && Array.isArray(data.Events)) return data.Events;
    if (data && Array.isArray(data.events)) return data.events;
    return [];
  }

  function escapeHtml(s) {
    return String(s || "").replace(/[&<>]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));
  }

  function renderRows(events) {
    const tbody = $("#logsTbody");
    if (!tbody) return;
    if (events.length === 0) {
      tbody.innerHTML = `<tr><td colspan="4" style="text-align:center;padding:24px;color:var(--t-3);">Событий не найдено</td></tr>`;
      return;
    }
    tbody.innerHTML = "";
    events.forEach(e => {
      const ts = getTimestamp(e);
      const tsStr = ts ? new Date(ts).toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "—";
      const tr = document.createElement("tr");
      tr.style.borderTop = "1px solid var(--line)";
      tr.style.cursor = "pointer";
      tr.innerHTML = `
        <td class="mono" style="padding:6px 8px;font-size:11px;color:var(--t-2);">${tsStr}</td>
        <td style="padding:6px 8px;">${levelBadge(getLevel(e))}</td>
        <td class="mono" style="padding:6px 8px;font-size:11px;color:var(--t-2);">${getApplication(e) || "—"}</td>
        <td style="padding:6px 8px;font-family:var(--font-mono);font-size:11px;line-height:1.45;">${escapeHtml(getMessage(e)).slice(0, 500)}</td>`;
      tr.addEventListener("click", () => openDetail(e));
      tr.addEventListener("mouseover", () => tr.style.background = "rgba(255,255,255,0.02)");
      tr.addEventListener("mouseout", () => tr.style.background = "transparent");
      tbody.appendChild(tr);
    });
  }

  function openDetail(e) {
    selectedEvent = e;
    const card = $("#logsDetailCard");
    card.style.display = "";
    $("#logsDetailSub").textContent = `${getApplication(e) || "—"} · ${getLevel(e)} · ${getTimestamp(e)}`;
    $("#logsDetailBody").textContent = JSON.stringify(e, null, 2);
  }
  function closeDetail() {
    selectedEvent = null;
    const card = $("#logsDetailCard");
    if (card) card.style.display = "none";
  }

  function show() {
    loadServices();
    loadLogs();
    if (pollTimer) clearInterval(pollTimer);
    pollTimer = setInterval(() => {
      if (isPaused) return;
      loadLogs();
    }, 5000);
  }

  App.registerScreen("logs", { render: build, show });
  window.ScreenLogs = { render: build };
})();
