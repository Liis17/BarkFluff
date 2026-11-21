(() => {
  "use strict";

  const REFRESH_MS = 60_000;
  const RETRY_MS = 15_000;
  const STALE_REPORT_MS = 2 * 60_000;

  const STATE = {
    operational: { label: "Работает", glyph: "✓" },
    degraded: { label: "С ограничениями", glyph: "!" },
    down: { label: "Недоступен", glyph: "✕" },
    partial_outage: { label: "Частичный сбой", glyph: "✕" },
    major_outage: { label: "Сбой", glyph: "✕" },
    starting: { label: "Прогрев", glyph: "↻" },
    unknown: { label: "Неизвестно", glyph: "?" },
    stale: { label: "Устарело", glyph: "⌛" },
    nodata: { label: "Нет данных", glyph: "–" },
    disabled: { label: "Не проверяется", glyph: "–" },
  };

  const OVERALL = {
    operational: { title: "Все сервисы работают", glyph: "✓" },
    degraded: { title: "Есть сервисы с ограничениями", glyph: "!" },
    partial_outage: { title: "Часть сервисов недоступна", glyph: "✕" },
    major_outage: { title: "Сервисы недоступны", glyph: "✕" },
    unknown: { title: "Состояние сейчас неизвестно", glyph: "?" },
    loading: { title: "Загрузка статуса", glyph: "…" },
  };

  const dateFormat = new Intl.DateTimeFormat("ru-RU", {
    timeZone: "UTC", day: "numeric", month: "long", year: "numeric",
  });
  const timeFormat = new Intl.DateTimeFormat("ru-RU", {
    timeZone: "UTC", hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  });
  const shortDateFormat = new Intl.DateTimeFormat("ru-RU", {
    timeZone: "UTC", day: "numeric", month: "short",
  });

  const $ = (id) => document.getElementById(id);
  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  let lastReport = null;

  function formatPercent(value) {
    return value === null || value === undefined ? "—" : `${value.toLocaleString("ru-RU", { maximumFractionDigits: 2 })}%`;
  }

  function formatDay(isoDate) {
    const [year, month, day] = isoDate.split("-").map(Number);
    return shortDateFormat.format(new Date(Date.UTC(year, month - 1, day)));
  }

  function formatLastCheck(iso) {
    if (!iso) return "нет проверок";
    const date = new Date(iso);
    return `${dateFormat.format(date)}, ${timeFormat.format(date)} UTC`;
  }

  function dayTitle(day) {
    const label = STATE[day.state]?.label ?? day.state;
    const [year, month, date] = day.date.split("-").map(Number);
    const human = dateFormat.format(new Date(Date.UTC(year, month - 1, date)));
    const coverage = day.expected > 0 ? `, проверок ${day.observed} из ${day.expected}` : "";
    return `${human}: ${label}${coverage}`;
  }

  function buildService(service) {
    const state = STATE[service.state] ? service.state : "unknown";
    const item = el("li", "service");
    item.dataset.state = state;

    const top = el("div", "service-top");
    top.append(el("span", "service-name", service.name));

    const badge = el("span", "badge");
    badge.append(el("span", "badge-glyph", STATE[state].glyph), el("span", null, STATE[state].label));
    top.append(badge);
    item.append(top);

    const metrics = el("ul", "metrics");
    metrics.setAttribute("aria-label", `Показатели: ${service.name}`);
    const addMetric = (label, value) => {
      const li = el("li");
      li.append(`${label}: `, el("strong", null, value));
      metrics.append(li);
    };
    addMetric("Доступность за 90 дней", formatPercent(service.uptimePercent));
    addMetric("Покрытие проверками", formatPercent(service.coveragePercent));
    addMetric("Деградация, проверок", String(service.degradedSlots));
    addMetric("Последняя проверка", formatLastCheck(service.lastCheckUtc));
    item.append(metrics);

    const days = service.days ?? [];
    const bars = el("div", "bars");
    const summary = `${service.name}: ${days.length} дней. ${dayTitle(days.at(-1) ?? { state: "nodata", date: "", expected: 0, observed: 0 })}`;
    bars.setAttribute("role", "img");
    bars.setAttribute("aria-label", summary);
    for (const day of days) {
      const bar = el("span", "bar");
      bar.dataset.state = STATE[day.state] ? day.state : "unknown";
      bar.title = dayTitle(day);
      bar.setAttribute("aria-hidden", "true");
      bars.append(bar);
    }
    item.append(bars);

    if (days.length > 0) {
      const scale = el("div", "bars-scale");
      scale.setAttribute("aria-hidden", "true");
      scale.append(el("span", null, formatDay(days[0].date)), el("span", null, "сегодня"));
      item.append(scale);
    }
    return item;
  }

  function render(report, fetchedAt) {
    const overallState = OVERALL[report.overallState] ? report.overallState : "unknown";
    const reportAge = fetchedAt - Date.parse(report.generatedAtUtc);
    const outdated = reportAge > STALE_REPORT_MS;
    const effective = outdated ? "unknown" : overallState;

    const overall = $("overall");
    overall.dataset.state = effective;
    $("overall-title").textContent = OVERALL[effective].title;
    overall.querySelector(".overall-icon").textContent = OVERALL[effective].glyph;

    const working = report.services.filter((s) => s.state === "operational").length;
    const monitored = report.services.filter((s) => s.state !== "disabled").length;
    $("overall-meta").textContent = outdated
      ? "Данные не обновлялись больше двух минут. Показываем последний известный ответ."
      : `Работают ${working} из ${monitored}. Обновлено ${timeFormat.format(new Date(report.generatedAtUtc))} UTC.`;

    const notice = $("notice");
    notice.hidden = !outdated;
    notice.textContent = outdated ? "Статус может быть устаревшим: сервер статуса не отвечает свежими данными." : "";

    const list = $("services");
    list.replaceChildren(...report.services.map(buildService));
    list.setAttribute("aria-busy", "false");

    $("footer-updated").textContent = `Данные за 90 дней (UTC). Проверка каждые ${report.intervalSeconds} секунд.`;
  }

  function renderError() {
    const overall = $("overall");
    overall.dataset.state = "unknown";
    $("overall-title").textContent = "Не удалось загрузить статус";
    overall.querySelector(".overall-icon").textContent = "?";
    $("overall-meta").textContent = "Повторим попытку автоматически.";

    const notice = $("notice");
    notice.hidden = false;
    notice.textContent = lastReport
      ? "Показаны последние полученные данные. Сервер статуса сейчас недоступен."
      : "Данные пока недоступны.";
    $("services").setAttribute("aria-busy", "false");
  }

  async function load() {
    try {
      const response = await fetch("/api/status", { cache: "no-store", headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const report = await response.json();
      if (!Array.isArray(report.services)) throw new Error("unexpected payload");
      lastReport = report;
      render(report, Date.now());
      schedule(REFRESH_MS);
    } catch {
      if (lastReport) render(lastReport, Date.now());
      renderError();
      schedule(RETRY_MS);
    }
  }

  let timer = 0;
  function schedule(delay) {
    clearTimeout(timer);
    timer = setTimeout(load, delay);
  }

  load();
})();
