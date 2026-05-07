/* =====================================================
   Login screen — Telegram approval flow
   POST /api/auth/request -> poll /api/auth/status/{id}
   ===================================================== */
(function () {
  "use strict";
  const { el, $, icon, toast, go, seedRng } = App;

  let pollInterval = null;
  let pollTimeout = null;

  function detectDevice() {
    const ua = navigator.userAgent;
    let browser = "Неизвестный браузер";
    let os = "Неизвестная ОС";
    if (ua.indexOf("Firefox/") !== -1) browser = "Firefox";
    else if (ua.indexOf("Edg/") !== -1) browser = "Edge";
    else if (ua.indexOf("OPR/") !== -1 || ua.indexOf("Opera/") !== -1) browser = "Opera";
    else if (ua.indexOf("YaBrowser/") !== -1) browser = "Yandex Browser";
    else if (ua.indexOf("Chrome/") !== -1) browser = "Chrome";
    else if (ua.indexOf("Safari/") !== -1 && ua.indexOf("Chrome") === -1) browser = "Safari";
    if (ua.indexOf("Windows NT 10") !== -1) os = "Windows 10/11";
    else if (ua.indexOf("Windows NT") !== -1) os = "Windows";
    else if (ua.indexOf("Mac OS X") !== -1) os = "macOS";
    else if (ua.indexOf("Linux") !== -1 && ua.indexOf("Android") !== -1) os = "Android";
    else if (ua.indexOf("Linux") !== -1) os = "Linux";
    else if (ua.indexOf("iPhone") !== -1 || ua.indexOf("iPad") !== -1) os = "iOS";
    return browser + " / " + os;
  }

  function stopPolling() {
    if (pollInterval) { clearInterval(pollInterval); pollInterval = null; }
    if (pollTimeout) { clearTimeout(pollTimeout); pollTimeout = null; }
  }

  function build() {
    const root = $("#screen-login");
    root.innerHTML = "";
    const rng = seedRng(11);

    // left: telemetry rail (декоративная)
    const tickers = [];
    for (let i = 0; i < 18; i++) {
      tickers.push({
        t: new Date(Date.now() - i * 1300).toISOString().slice(11, 19),
        svc: ["api", "media", "auth", "ws", "cdn", "worker", "search"][Math.floor(rng()*7)],
        msg: [
          "ingress healthy",
          "p95=42ms region=eu-c",
          "deploy v2.4.1 ok",
          "cache warm 94%",
          "snapshot complete",
          "rate-limit window resets",
          "queue depth=0",
        ][Math.floor(rng()*7)],
      });
    }

    const left = el("div", { class: "login-left" });
    const grid = el("div", { class: "login-grid" });
    left.appendChild(grid);

    const corner = el("div", { class: "login-corner" });
    corner.innerHTML = `<div class="brand-mark" style="width:30px;height:30px;border-radius:9px"></div>
      <div>
        <div class="login-corner-title">BARKFLUFF</div>
        <div class="login-corner-sub mono">admin · operator console</div>
      </div>`;
    left.appendChild(corner);

    const status = el("div", { class: "login-status" });
    status.innerHTML = `
      <div class="login-status-row">
        <span class="status-dot ok"></span>
        <span>Доступ через Telegram-бот</span>
        <span class="mono">approval</span>
      </div>
      <div class="login-status-row">
        <span class="status-dot ok"></span>
        <span>Сессия cookie</span>
        <span class="mono">7 дней</span>
      </div>
      <div class="login-status-row">
        <span class="status-dot ok"></span>
        <span>Audit-trail</span>
        <span class="mono">on</span>
      </div>`;
    left.appendChild(status);

    const ticker = el("div", { class: "login-ticker" });
    ticker.innerHTML = '<div class="login-ticker-head">live · stream</div>';
    const tickerList = el("div", { class: "login-ticker-list" });
    tickers.forEach(t => {
      const row = el("div", { class: "login-ticker-row" });
      row.innerHTML = `<span class="mono t3">${t.t}</span><span class="login-ticker-svc">${t.svc}</span><span>${t.msg}</span>`;
      tickerList.appendChild(row);
    });
    ticker.appendChild(tickerList);
    left.appendChild(ticker);

    const meta = el("div", { class: "login-meta mono" });
    meta.textContent = "device · " + detectDevice();
    left.appendChild(meta);

    // right: form
    const right = el("div", { class: "login-right" });
    right.innerHTML = `
      <div class="login-form-wrap">
        <div class="login-eyebrow mono">SECURE · TELEGRAM APPROVAL · ROLE=ADMIN</div>
        <h1 class="login-h1">Запрос доступа<br/><span class="t-grad">в админ-консоль</span>.</h1>
        <p class="login-sub">Введите ваш ник в Telegram. Запрос придёт боту BarkFluff — администратор подтвердит вход в чате.</p>

        <form class="login-form" id="loginForm">
          <label class="field">
            <span class="field-label">Никнейм Telegram <span class="field-aux">без @</span></span>
            <div class="field-input">
              <span class="field-icon">${icon("user", 14)}</span>
              <input type="text" id="loginNickname" placeholder="username" autocomplete="username" />
            </div>
          </label>

          <div class="login-row" id="loginValidation" style="display:none;color:#ff6b6b;font-size:12px;">
            Введите никнейм
          </div>

          <button class="btn btn-primary btn-lg" id="loginSubmit" type="submit">
            ${icon("arrowRight", 14)} Запросить вход
          </button>

          <div id="loginStatus" style="display:none;margin-top:14px;padding:14px;border-radius:8px;background:rgba(108,92,231,0.08);border:1px solid rgba(108,92,231,0.25);">
            <div id="loginStatusText" style="font-size:13px;line-height:1.5;"></div>
          </div>

          <div class="login-foot">
            <span class="t3">Доступ выдаётся через Telegram-бот · audit-trail</span>
            <span class="t3 mono">v2</span>
          </div>
        </form>
      </div>`;

    root.appendChild(left);
    root.appendChild(right);

    const form = $("#loginForm");
    const nickInput = $("#loginNickname");
    const validation = $("#loginValidation");
    const submitBtn = $("#loginSubmit");
    const statusBox = $("#loginStatus");
    const statusText = $("#loginStatusText");

    function setStatus(kind, message) {
      statusBox.style.display = "block";
      statusText.innerHTML = message;
      const colors = {
        wait: { bg: "rgba(108,92,231,0.08)", border: "rgba(108,92,231,0.25)" },
        ok:   { bg: "rgba(81,207,102,0.08)", border: "rgba(81,207,102,0.3)" },
        err:  { bg: "rgba(255,107,107,0.08)", border: "rgba(255,107,107,0.3)" },
      };
      const c = colors[kind] || colors.wait;
      statusBox.style.background = c.bg;
      statusBox.style.borderColor = c.border;
    }
    function hideStatus() { statusBox.style.display = "none"; }

    function resetForm() {
      stopPolling();
      submitBtn.disabled = false;
      nickInput.disabled = false;
      submitBtn.innerHTML = `${icon("arrowRight", 14)} Запросить вход`;
    }

    function pollAuthStatus(requestId) {
      setStatus("wait", "⏳ Ожидание подтверждения в Telegram… (до 10 минут)");
      pollTimeout = setTimeout(() => {
        stopPolling();
        resetForm();
        setStatus("err", "Время ожидания истекло. Попробуйте снова.");
      }, 10 * 60 * 1000);

      pollInterval = setInterval(async () => {
        try {
          const res = await fetch("/api/auth/status/" + encodeURIComponent(requestId));
          if (!res.ok) return;
          const data = await res.json();
          if (data.status === 1) {
            stopPolling();
            setStatus("ok", "✓ Доступ подтверждён. Загрузка консоли…");
            // cookie уже выставлена сервером (или дублируем для надёжности)
            if (data.token) {
              const date = new Date();
              date.setTime(date.getTime() + 7 * 24 * 60 * 60 * 1000);
              document.cookie = "auth_token=" + encodeURIComponent(data.token) + "; expires=" + date.toUTCString() + "; path=/";
            }
            setTimeout(() => { window.location.href = "/v2/"; }, 600);
          } else if (data.status === 2) {
            stopPolling();
            resetForm();
            setStatus("err", "Запрос отклонён администратором.");
          } else if (data.status === 3) {
            stopPolling();
            resetForm();
            setStatus("err", "Время ожидания истекло.");
          }
        } catch (_) {
          stopPolling();
          resetForm();
          setStatus("err", "Ошибка соединения с сервером.");
        }
      }, 2000);
    }

    nickInput.addEventListener("input", () => { validation.style.display = "none"; });

    form.addEventListener("submit", async (e) => {
      e.preventDefault();
      const nickname = nickInput.value.trim().replace(/^@/, "");
      if (!nickname) {
        validation.style.display = "block";
        nickInput.focus();
        return;
      }
      hideStatus();
      submitBtn.disabled = true;
      nickInput.disabled = true;
      submitBtn.innerHTML = `<span class="spin"></span> Отправка запроса…`;
      try {
        const res = await fetch("/api/auth/request", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            userAgent: navigator.userAgent,
            tokenName: detectDevice(),
            nickname: nickname,
          }),
        });
        if (!res.ok) {
          let message = "Ошибка запроса";
          try { const data = await res.json(); message = data.message || message; } catch (_) {}
          resetForm();
          setStatus("err", message);
          return;
        }
        const data = await res.json();
        const requestId = data.requestId || data.RequestId;
        if (!requestId) {
          resetForm();
          setStatus("err", "Сервер не вернул requestId");
          return;
        }
        submitBtn.innerHTML = `<span class="spin"></span> Ожидание…`;
        pollAuthStatus(requestId);
      } catch (e) {
        resetForm();
        setStatus("err", "Сеть недоступна.");
      }
    });
  }

  function show() {
    // при показе экрана сбрасываем polling если был
    stopPolling();
  }

  App.registerScreen("login", { render: build, show });
  window.ScreenLogin = { render: build };
})();
