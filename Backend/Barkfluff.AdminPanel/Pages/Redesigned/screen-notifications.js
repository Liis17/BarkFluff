/* =====================================================
   Notifications — рассылка push-уведомлений на Android
   ===================================================== */
(function () {
  "use strict";
  const { el, $, icon, toast, fetchJson } = App;

  const State = {
    title: "",
    body: "",
    imageUrl: "",
    deviceIdsRaw: "",
  };

  function escapeHtml(s) {
    return String(s == null ? "" : s).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
  }

  function confirmModal({ title, message, confirmLabel, confirmKind, onConfirm }) {
    const overlay = el("div", { style: "position:fixed;inset:0;background:rgba(0,0,0,0.55);z-index:9999;display:flex;align-items:center;justify-content:center;padding:20px;" });
    const dlg = el("div", { style: "background:var(--panel-2);border:1px solid var(--line);border-radius:12px;width:min(440px,95vw);color:var(--t-1);overflow:hidden;" });
    dlg.innerHTML = `
      <div style="padding:14px 18px;border-bottom:1px solid var(--line);font-size:15px;font-weight:600;">
        ${escapeHtml(title)}
      </div>
      <div style="padding:18px;font-size:13px;color:var(--t-2);line-height:1.55;">
        ${message}
      </div>
      <div style="padding:12px 18px;border-top:1px solid var(--line);display:flex;justify-content:flex-end;gap:8px;">
        <button class="btn btn-ghost btn-sm" data-cancel>Отмена</button>
        <button class="btn btn-sm ${confirmKind === "danger" ? "btn-danger" : "btn-primary"}" data-confirm>${escapeHtml(confirmLabel)}</button>
      </div>`;
    overlay.appendChild(dlg);
    document.body.appendChild(overlay);
    const close = () => overlay.remove();
    dlg.querySelector("[data-cancel]").addEventListener("click", close);
    overlay.addEventListener("click", (e) => { if (e.target === overlay) close(); });
    dlg.querySelector("[data-confirm]").addEventListener("click", async () => {
      close();
      await onConfirm();
    });
  }

  function syncPreview() {
    const t = $("#nfPreviewTitle");
    const b = $("#nfPreviewBody");
    const imgWrap = $("#nfPreviewImage");
    const imgEl = $("#nfPreviewImage img");
    if (!t || !b || !imgWrap || !imgEl) return;
    t.textContent = State.title || "Заголовок уведомления";
    b.textContent = State.body || "Текст уведомления отобразится здесь.";
    if (State.imageUrl && State.imageUrl.trim()) {
      imgEl.src = State.imageUrl.trim();
      imgWrap.style.display = "block";
    } else {
      imgEl.removeAttribute("src");
      imgWrap.style.display = "none";
    }
  }

  function buildScreen() {
    const root = $("#screen-notifications");
    root.innerHTML = "";

    const wrap = el("div", { class: "screen-inner" });

    const head = el("div", { class: "page-head" });
    head.innerHTML = `
      <div class="page-head-l">
        <h1 class="page-h1">Уведомления</h1>
        <p class="page-sub">Рассылка push на Android-устройства через Firebase Cloud Messaging.</p>
      </div>`;
    wrap.appendChild(head);

    const layout = el("div", { style: "display:grid;gap:18px;grid-template-columns:minmax(0,1fr) 360px;align-items:start;" });

    /* ----- LEFT: форма ----- */
    const form = el("div", { class: "card" });
    form.innerHTML = `
      <div class="card-head"><div class="card-head-l"><span class="card-title">Содержимое</span></div></div>
      <div style="padding:18px;display:flex;flex-direction:column;gap:14px;">
        <label style="display:flex;flex-direction:column;gap:6px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.04em;">
          URL картинки (необязательно)
          <input type="text" id="nfImageUrl" placeholder="https://..." style="background:var(--panel-3);border:1px solid var(--line);border-radius:6px;padding:9px 11px;color:var(--t-1);font-size:13px;font-family:var(--font-mono);" />
        </label>
        <label style="display:flex;flex-direction:column;gap:6px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.04em;">
          Заголовок
          <input type="text" id="nfTitle" maxlength="120" placeholder="Новое обновление" style="background:var(--panel-3);border:1px solid var(--line);border-radius:6px;padding:9px 11px;color:var(--t-1);font-size:13px;" />
        </label>
        <label style="display:flex;flex-direction:column;gap:6px;font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.04em;">
          Тело уведомления
          <textarea id="nfBody" maxlength="500" rows="4" placeholder="Текст уведомления..." style="background:var(--panel-3);border:1px solid var(--line);border-radius:6px;padding:9px 11px;color:var(--t-1);font-size:13px;resize:vertical;font-family:inherit;"></textarea>
        </label>

        <div style="border-top:1px solid var(--line);margin-top:4px;padding-top:14px;display:flex;flex-direction:column;gap:10px;">
          <div style="font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.04em;">Рассылка всем</div>
          <button class="btn btn-primary" id="nfSendAll" style="align-self:flex-start;">
            ${icon("bell", 14)} Отправить на все устройства
          </button>
        </div>

        <div style="border-top:1px solid var(--line);margin-top:4px;padding-top:14px;display:flex;flex-direction:column;gap:10px;">
          <div style="font-size:11px;color:var(--t-3);text-transform:uppercase;letter-spacing:0.04em;">Адресная отправка</div>
          <label style="display:flex;flex-direction:column;gap:6px;">
            <span style="font-size:11px;color:var(--t-3);">Device IDs (Guid через запятую или с новой строки)</span>
            <textarea id="nfDeviceIds" rows="3" placeholder="00000000-0000-0000-0000-000000000000, ..." style="background:var(--panel-3);border:1px solid var(--line);border-radius:6px;padding:9px 11px;color:var(--t-1);font-size:12px;font-family:var(--font-mono);resize:vertical;"></textarea>
          </label>
          <button class="btn btn-ghost" id="nfSendDevices" style="align-self:flex-start;">
            ${icon("arrowRight", 14)} Отправить на устройства
          </button>
        </div>
      </div>`;

    /* ----- RIGHT: Android preview ----- */
    const previewCard = el("div", { class: "card" });
    previewCard.innerHTML = `
      <div class="card-head"><div class="card-head-l"><span class="card-title">Предпросмотр Android</span></div></div>
      <div style="padding:18px;display:flex;flex-direction:column;gap:10px;">
        <div style="background:#1a1d23;border:1px solid #2a2f3a;border-radius:14px;padding:14px;box-shadow:0 4px 16px rgba(0,0,0,0.4);">
          <div style="display:flex;align-items:center;gap:8px;margin-bottom:8px;color:#a0a4ad;font-size:11px;">
            <div style="width:18px;height:18px;border-radius:5px;background:linear-gradient(135deg,#5d6cff,#8a5cff);display:flex;align-items:center;justify-content:center;color:#fff;font-weight:700;font-size:10px;">B</div>
            <span style="font-weight:600;color:#cfd2d8;">BarkFluff</span>
            <span style="opacity:0.6;">· сейчас</span>
          </div>
          <div id="nfPreviewTitle" style="color:#f0f1f4;font-size:14px;font-weight:600;line-height:1.3;margin-bottom:2px;word-break:break-word;">Заголовок уведомления</div>
          <div id="nfPreviewBody" style="color:#bcc0c9;font-size:13px;line-height:1.4;word-break:break-word;white-space:pre-wrap;">Текст уведомления отобразится здесь.</div>
          <div id="nfPreviewImage" style="display:none;margin-top:10px;border-radius:8px;overflow:hidden;background:#0a0d14;">
            <img alt="preview" style="display:block;width:100%;max-height:200px;object-fit:cover;" onerror="this.parentNode.style.display='none';" />
          </div>
        </div>
        <p style="font-size:11px;color:var(--t-3);line-height:1.5;margin:4px 0 0 2px;">
          Так уведомление увидит пользователь. BigPictureStyle — если указан URL картинки.
        </p>
      </div>`;

    layout.appendChild(form);
    layout.appendChild(previewCard);
    wrap.appendChild(layout);
    root.appendChild(wrap);

    /* ----- bindings ----- */
    const titleInput = $("#nfTitle");
    const bodyInput = $("#nfBody");
    const imageInput = $("#nfImageUrl");
    const deviceIdsInput = $("#nfDeviceIds");

    titleInput.value = State.title;
    bodyInput.value = State.body;
    imageInput.value = State.imageUrl;
    deviceIdsInput.value = State.deviceIdsRaw;

    titleInput.addEventListener("input", () => { State.title = titleInput.value; syncPreview(); });
    bodyInput.addEventListener("input", () => { State.body = bodyInput.value; syncPreview(); });
    imageInput.addEventListener("input", () => { State.imageUrl = imageInput.value; syncPreview(); });
    deviceIdsInput.addEventListener("input", () => { State.deviceIdsRaw = deviceIdsInput.value; });

    $("#nfSendAll").addEventListener("click", onSendAll);
    $("#nfSendDevices").addEventListener("click", onSendDevices);

    syncPreview();
  }

  function validateCommon() {
    if (!State.title.trim()) { toast("Введите заголовок", { kind: "warn" }); return false; }
    if (!State.body.trim()) { toast("Введите текст уведомления", { kind: "warn" }); return false; }
    return true;
  }

  function parseDeviceIds(raw) {
    return raw
      .split(/[\s,;]+/)
      .map(s => s.trim())
      .filter(Boolean);
  }

  function onSendAll() {
    if (!validateCommon()) return;
    confirmModal({
      title: "Подтверждение рассылки",
      message: `Отправить уведомление <strong>на ВСЕ Android-устройства</strong> с активным FCM-токеном?<br><br><span style="color:var(--t-3);">Заголовок:</span> ${escapeHtml(State.title)}<br><span style="color:var(--t-3);">Текст:</span> ${escapeHtml(State.body)}`,
      confirmLabel: "Отправить всем",
      confirmKind: "danger",
      onConfirm: async () => {
        try {
          const res = await fetchJson("/api/notifications/broadcast/all", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              title: State.title.trim(),
              body: State.body.trim(),
              imageUrl: State.imageUrl.trim() || null,
              confirm: true,
            }),
          });
          if (res && res.enqueued) {
            toast("Рассылка поставлена в очередь", { kind: "ok" });
          }
        } catch (e) {
          toast("Ошибка: " + e.message, { kind: "err" });
        }
      }
    });
  }

  function onSendDevices() {
    if (!validateCommon()) return;
    const ids = parseDeviceIds(State.deviceIdsRaw);
    if (ids.length === 0) {
      toast("Укажите хотя бы один Device ID", { kind: "warn" });
      return;
    }
    confirmModal({
      title: "Подтверждение отправки",
      message: `Отправить уведомление на <strong>${ids.length}</strong> устройств(а)?<br><br><span style="color:var(--t-3);">Заголовок:</span> ${escapeHtml(State.title)}<br><span style="color:var(--t-3);">Текст:</span> ${escapeHtml(State.body)}`,
      confirmLabel: "Отправить",
      confirmKind: "primary",
      onConfirm: async () => {
        try {
          const res = await fetchJson("/api/notifications/broadcast/devices", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              title: State.title.trim(),
              body: State.body.trim(),
              imageUrl: State.imageUrl.trim() || null,
              deviceIds: ids,
            }),
          });
          if (res && res.enqueued) {
            toast(`Рассылка поставлена в очередь (${res.deviceCount} устройств)`, { kind: "ok" });
          }
        } catch (e) {
          toast("Ошибка: " + e.message, { kind: "err" });
        }
      }
    });
  }

  function render() {
    buildScreen();
  }

  function show() {
    syncPreview();
  }

  window.ScreenNotifications = { render, show };
  App.registerScreen("notifications", { render, show });
})();
