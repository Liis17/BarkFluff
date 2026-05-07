/* =====================================================
   Dashboard — KPI + traffic + per-service metrics (real data)
   GET /api/seq/dashboard/kpis
   GET /api/seq/dashboard/traffic
   GET /api/seq/dashboard/service-metrics/{service}
   ===================================================== */
(function () {
  "use strict";
  const { el, $, $$, icon, fmt, sparkPath, toast, fetchJson } = App;

  const KNOWN_SERVICES = [
    "BarkFluff.Identity", "BarkFluff.Users", "BarkFluff.Messages",
    "BarkFluff.Files", "BarkFluff.Updates", "BarkFluff.Notification",
    "BarkFluff.Beacon", "BarkFluff.FastAuth", "BarkFluff.Onliner",
    "BarkFluff.Configuration"
  ];

  const SERVER_STARTED_AT = "{{SERVER_STARTED_AT_UTC}}";
  const serverStartedDate = (() => {
    const d = new Date(SERVER_STARTED_AT);
    return isNaN(d.getTime()) ? null : d;
  })();

  let refreshTimer = null;
  let serviceMetricCharts = [];

  function fmtNum(v) {
    if (v == null) return "—";
    return Number(v).toLocaleString("ru-RU");
  }
  function shortServiceName(full) {
    if (!full) return full;
    const parts = full.split(".");
    return parts.length > 1 ? parts[parts.length - 1] : full;
  }
  function formatHour(iso) {
    if (!iso) return "";
    const d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    return d.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
  }
  function formatMetricName(name) {
    if (!name) return name;
    return name.split("_").map(w => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()).join(" ");
  }
  function formatUptime(diffSec) {
    const days = Math.floor(diffSec / 86400);
    const hours = Math.floor((diffSec % 86400) / 3600);
    const minutes = Math.floor((diffSec % 3600) / 60);
    const seconds = diffSec % 60;
    let text = "";
    if (days > 0) text += days + "д ";
    text += String(hours).padStart(2, "0") + ":" + String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0");
    return text;
  }

  function kpi({ label, value, sub, spark, accent }) {
    return el("div", { class: "kpi", html: `
      <div class="kpi-head">
        <span class="kpi-label">${label}</span>
      </div>
      <div class="kpi-value" data-kpi-value="${spark}">${value}<span class="kpi-sub">${sub || ""}</span></div>` });
  }

  function trafficCard() {
    const node = el("div", { class: "card card-traffic" });
    node.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Трафик системы</h3>
          <span class="card-sub">events · errors · warnings · last 24h</span>
        </div>
        <div class="card-head-r">
          <div class="legend"><span class="dot accent"></span> all</div>
          <div class="legend"><span class="dot err"></span> errors</div>
          <div class="legend"><span class="dot" style="background:#eab308"></span> warnings</div>
        </div>
      </div>
      <div style="position:relative;height:240px;padding:8px 4px;">
        <canvas id="dashTrafficChart"></canvas>
      </div>
      <div class="traffic-meta" id="dashTrafficMeta">
        <div class="meta-item"><span class="meta-l">total</span><span class="meta-v mono" id="metaTotal">—</span></div>
        <div class="meta-item"><span class="meta-l">errors</span><span class="meta-v mono" id="metaErr">—</span></div>
        <div class="meta-item"><span class="meta-l">warnings</span><span class="meta-v mono" id="metaWarn">—</span></div>
        <div class="meta-item"><span class="meta-l">err rate</span><span class="meta-v mono" id="metaErrRate">—</span></div>
      </div>`;
    return node;
  }

  function servicesGridCard() {
    const node = el("div", { class: "card card-services" });
    node.innerHTML = `
      <div class="card-head">
        <div class="card-head-l">
          <h3 class="card-title">Метрики сервисов</h3>
          <span class="card-sub">последние 12 часов</span>
        </div>
        <div class="card-head-r">
          <button class="btn btn-ghost btn-sm" id="dashRefreshSvc">${icon("refresh",12)} Обновить</button>
        </div>
      </div>
      <div class="svc-grid" id="dashServiceCards"></div>`;
    return node;
  }

  function build() {
    const root = $("#screen-dashboard");
    root.innerHTML = "";
    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Дашборд</h1>
        <p class="page-sub">Сводный обзор инфраструктуры BarkFluff. Данные из Seq · обновление каждые 30 секунд.</p>
      </div>
      <div class="page-head-r">
        <button class="btn btn-ghost btn-sm" id="dashRefreshAll">${icon("refresh",12)} Обновить</button>
      </div>`;
    wrap.appendChild(head);

    const kpis = el("div", { class: "kpi-row" });
    kpis.appendChild(kpi({ label: "Всего событий (24ч)", value: "—", sub: "events",  spark: "total"  }));
    kpis.appendChild(kpi({ label: "Ошибки (24ч)",         value: "—", sub: "err+fatal", spark: "errors" }));
    kpis.appendChild(kpi({ label: "Предупреждения (24ч)", value: "—", sub: "warn",    spark: "warnings" }));
    kpis.appendChild(kpi({ label: "Активные сервисы",     value: "—", sub: "за 24ч",   spark: "services" }));
    kpis.appendChild(kpi({ label: "Время работы panel",   value: "—", sub: "uptime",   spark: "uptime" }));
    wrap.appendChild(kpis);

    wrap.appendChild(trafficCard());
    wrap.appendChild(servicesGridCard());

    root.appendChild(wrap);

    $("#dashRefreshAll").addEventListener("click", () => loadAll());
    $("#dashRefreshSvc").addEventListener("click", () => loadServiceMetrics());
  }

  function setKpi(spark, value) {
    const node = document.querySelector(`[data-kpi-value="${spark}"]`);
    if (!node) return;
    const sub = node.querySelector(".kpi-sub");
    const subHtml = sub ? sub.outerHTML : "";
    node.innerHTML = value + subHtml;
  }

  let trafficChart = null;
  function renderTrafficChart(traffic) {
    const labels = traffic.all.map(p => formatHour(p.timestamp));
    const allData = traffic.all.map(p => p.count);
    const errorData = traffic.errors.map(p => p.count);
    const warningData = (traffic.warnings || []).map(p => p.count);

    const total = allData.reduce((a,b) => a+b, 0);
    const totalErr = errorData.reduce((a,b) => a+b, 0);
    const totalWarn = warningData.reduce((a,b) => a+b, 0);
    $("#metaTotal").textContent = fmtNum(total);
    $("#metaErr").textContent = fmtNum(totalErr);
    $("#metaWarn").textContent = fmtNum(totalWarn);
    $("#metaErrRate").textContent = total > 0 ? ((totalErr / total) * 100).toFixed(2) + "%" : "—";

    const ctx = document.getElementById("dashTrafficChart");
    if (!ctx) return;
    if (trafficChart) {
      trafficChart.data.labels = labels;
      trafficChart.data.datasets[0].data = allData;
      trafficChart.data.datasets[1].data = errorData;
      trafficChart.data.datasets[2].data = warningData;
      trafficChart.update();
      return;
    }
    trafficChart = new Chart(ctx.getContext("2d"), {
      type: "line",
      data: {
        labels,
        datasets: [
          { label: "Все события", data: allData,     borderColor: "#5d6cff", backgroundColor: "rgba(93,108,255,0.12)", borderWidth: 1.8, fill: true,  tension: 0.35, pointRadius: 2, pointHoverRadius: 4 },
          { label: "Ошибки",       data: errorData,   borderColor: "#ef4444", backgroundColor: "rgba(239,68,68,0.10)",  borderWidth: 1.6, fill: true,  tension: 0.35, pointRadius: 2, pointHoverRadius: 4 },
          { label: "Предупреждения", data: warningData, borderColor: "#eab308", backgroundColor: "rgba(234,179,8,0.10)", borderWidth: 1.6, fill: true,  tension: 0.35, pointRadius: 2, pointHoverRadius: 4 },
        ]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: { backgroundColor: "#1e293b", padding: 10, cornerRadius: 6, displayColors: true }
        },
        scales: {
          x: { grid: { display: false }, ticks: { color: "#7480a0", font: { size: 10 } } },
          y: { beginAtZero: true, grid: { color: "rgba(148,163,184,0.10)" }, ticks: { color: "#7480a0", font: { size: 10 }, precision: 0 } }
        },
        interaction: { intersect: false, mode: "index" }
      }
    });
  }

  async function loadKpisAndTraffic() {
    try {
      const [kpis, traffic] = await Promise.all([
        fetchJson("/api/seq/dashboard/kpis?hours=24"),
        fetchJson("/api/seq/dashboard/traffic?hours=24&interval=1h"),
      ]);
      setKpi("total",    fmtNum(kpis.totalEvents));
      setKpi("errors",   fmtNum(kpis.errorCount));
      setKpi("warnings", fmtNum(kpis.warningCount));
      setKpi("services", fmtNum(kpis.perService ? Object.keys(kpis.perService).length : 0));
      renderTrafficChart(traffic);
    } catch (e) {
      console.error("dashboard kpis/traffic error", e);
    }
  }

  function renderEmptyServiceCards() {
    const grid = $("#dashServiceCards");
    if (!grid) return;
    grid.innerHTML = "";
    KNOWN_SERVICES.forEach(svc => {
      const cardId = `svcm-card-${svc.replace(/\./g, "-")}`;
      const canvasId = `svcm-canvas-${svc.replace(/\./g, "-")}`;
      const cell = el("div", { class: "svc-cell", id: cardId });
      cell.innerHTML = `
        <div class="svc-cell-head">
          <span class="status-dot ok"></span>
          <span class="svc-cell-name mono">${shortServiceName(svc)}</span>
          <span class="svc-cell-ver mono t3 svc-loading">…</span>
        </div>
        <div style="position:relative;height:90px;padding:4px 0;">
          <canvas id="${canvasId}"></canvas>
          <div class="svc-placeholder" style="position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:var(--t-3);font-size:11px;">Загрузка…</div>
        </div>`;
      grid.appendChild(cell);
    });
  }

  function renderServiceChart(svc, data) {
    const cardId = `svcm-card-${svc.replace(/\./g, "-")}`;
    const canvasId = `svcm-canvas-${svc.replace(/\./g, "-")}`;
    const card = document.getElementById(cardId);
    if (!card) return;
    const loading = card.querySelector(".svc-loading");
    if (loading) loading.remove();
    const placeholder = card.querySelector(".svc-placeholder");
    if (!data || !data.timeSeries || data.timeSeries.length === 0) {
      if (placeholder) placeholder.textContent = "Нет данных";
      return;
    }
    const metricNames = new Set();
    data.timeSeries.forEach(ts => Object.keys(ts.metrics || {}).forEach(k => metricNames.add(k)));
    if (metricNames.size === 0) {
      if (placeholder) placeholder.textContent = "Нет данных";
      return;
    }
    if (placeholder) placeholder.remove();
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const labels = data.timeSeries.map(ts => formatHour(ts.hour));
    const colors = ["#5d6cff", "#ef4444", "#22c55e", "#f59e0b", "#8b5cf6", "#ec4899", "#06b6d4", "#f97316"];
    const datasets = [];
    let i = 0;
    metricNames.forEach(metricName => {
      const c = colors[i++ % colors.length];
      datasets.push({
        label: formatMetricName(metricName),
        data: data.timeSeries.map(ts => (ts.metrics && ts.metrics[metricName]) || 0),
        borderColor: c, backgroundColor: c + "20",
        borderWidth: 1.3, fill: false, tension: 0.3, pointRadius: 1.5, pointHoverRadius: 3,
      });
    });
    const chart = new Chart(canvas.getContext("2d"), {
      type: "line",
      data: { labels, datasets },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: { legend: { display: false }, tooltip: { backgroundColor: "#1e293b", padding: 6, cornerRadius: 5, bodyFont: { size: 10 } } },
        scales: {
          x: { display: false },
          y: { beginAtZero: true, ticks: { color: "#7480a0", font: { size: 9 }, precision: 0 }, grid: { color: "rgba(148,163,184,0.06)" } }
        },
        interaction: { intersect: false, mode: "index" }
      }
    });
    serviceMetricCharts.push(chart);
  }

  async function loadServiceMetrics() {
    serviceMetricCharts.forEach(c => { try { c.destroy(); } catch (_) {} });
    serviceMetricCharts = [];
    renderEmptyServiceCards();
    for (const svc of KNOWN_SERVICES) {
      try {
        const data = await fetchJson(`/api/seq/dashboard/service-metrics/${encodeURIComponent(svc)}?hours=12`);
        renderServiceChart(svc, data);
      } catch (e) {
        const cardId = `svcm-card-${svc.replace(/\./g, "-")}`;
        const card = document.getElementById(cardId);
        if (card) {
          const placeholder = card.querySelector(".svc-placeholder");
          if (placeholder) { placeholder.textContent = "Ошибка"; placeholder.style.color = "#ef4444"; }
          const loading = card.querySelector(".svc-loading");
          if (loading) loading.remove();
        }
      }
    }
  }

  function updateUptime() {
    if (!serverStartedDate) { setKpi("uptime", "—"); return; }
    const diff = Math.max(0, Math.floor((Date.now() - serverStartedDate.getTime()) / 1000));
    setKpi("uptime", formatUptime(diff));
  }

  function loadAll() {
    loadKpisAndTraffic();
    updateUptime();
  }

  let uptimeTimer = null;
  let svcLoadedOnce = false;
  function show() {
    loadAll();
    if (!svcLoadedOnce) {
      svcLoadedOnce = true;
      loadServiceMetrics();
    }
    if (refreshTimer) clearInterval(refreshTimer);
    refreshTimer = setInterval(() => {
      if (App.getTweak("live") !== "on") return;
      loadKpisAndTraffic();
    }, 30000);
    if (uptimeTimer) clearInterval(uptimeTimer);
    uptimeTimer = setInterval(updateUptime, 1000);
  }

  App.registerScreen("dashboard", { render: build, show });
  window.ScreenDashboard = { render: build };
})();
