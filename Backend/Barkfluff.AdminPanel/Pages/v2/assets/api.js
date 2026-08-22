/* =============================================================
   BarkFluff Admin — API helper with Telegram step-up (428) flow
   Exposes: BF.api(url, options), BF.confirm(action, params),
            BF.stepUpQuery(action, params), BF.roles, BF.can()
   ============================================================= */

(function () {
  const BF = window.BF || (window.BF = {});
  BF.roles = BF.roles || [];

  // Mirror of the server-side AdminPermissions matrix (UI hint only).
  const PERMISSION_ROLES = {
    'users.read': ['Support', 'SecurityAdmin'],
    'users.password.set': ['Support', 'SecurityAdmin'],
    'users.2fa.disable': ['SecurityAdmin'],
    'badges.manage': ['ContentAdmin'],
    'stickers.manage': ['ContentAdmin'],
    'bots.manage': ['ContentAdmin'],
    'reserved-names.manage': ['ContentAdmin'],
    's3.browse': ['ContentAdmin'],
    'notifications.manage': ['ContentAdmin'],
    'docker.control': ['OperationsAdmin'],
    'docker.deploy': ['OperationsAdmin'],
    'remote.servers': ['OperationsAdmin'],
    'remote.console': ['OperationsAdmin'],
    'config.read': ['OperationsAdmin', 'SecurityAdmin'],
    'config.write': ['OperationsAdmin'],
    'federation.manage': ['SecurityAdmin'],
    'seq.delete': ['OperationsAdmin', 'SecurityAdmin'],
    'mail.manage': ['Support', 'SecurityAdmin'],
    'admins.roles': ['SecurityAdmin'],
    'audit.read': ['SecurityAdmin']
  };

  BF.hasRole = function (role) {
    return BF.roles.indexOf(role) !== -1;
  };

  BF.can = function (permission) {
    const allowed = PERMISSION_ROLES[permission];
    if (!allowed) return true;
    return allowed.some(function (r) { return BF.roles.indexOf(r) !== -1; });
  };

  function loadRoles() {
    fetch('/api/auth/me')
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (me) {
        if (me && Array.isArray(me.roles)) {
          BF.roles = me.roles;
          document.dispatchEvent(new CustomEvent('bf:roles', { detail: me.roles }));
        }
      })
      .catch(function () {});
  }

  // -------- Step-up modal --------

  let activeModal = null;

  function closeModal() {
    if (activeModal) {
      activeModal.element.remove();
      activeModal = null;
    }
  }

  function showStepUpModal(title) {
    return new Promise(function (resolve, reject) {
      if (activeModal) {
        reject(new Error('Уже есть ожидающее подтверждение'));
        return;
      }

      const overlay = document.createElement('div');
      overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:9999;display:flex;align-items:center;justify-content:center;padding:16px;';
      overlay.innerHTML = `
        <div style="background:var(--md-surface,#fff);border-radius:16px;max-width:420px;width:100%;padding:24px;box-shadow:0 8px 32px rgba(0,0,0,.2);font-family:inherit;">
          <div style="font-size:20px;font-weight:600;margin-bottom:8px;">Подтверждение действия</div>
          <div style="font-size:14px;color:var(--md-on-surface-variant,#5f6368);margin-bottom:20px;">
            <b></b> — запрос отправлен в Telegram. Подтвердите его в течение 5 минут.
          </div>
          <div class="bf-stepup-status" style="display:flex;align-items:center;gap:8px;font-size:13px;color:var(--md-on-surface-variant,#5f6368);margin-bottom:20px;">
            <span style="width:16px;height:16px;border-radius:50%;border:2px solid var(--md-primary,#8c351c);border-top-color:transparent;display:inline-block;animation:bf-spin 1s linear infinite;"></span>
            <span class="text">Ожидание подтверждения…</span>
          </div>
          <div style="display:flex;justify-content:flex-end;gap:8px;">
            <button type="button" class="md-btn-text bf-stepup-cancel" style="border:none;background:none;cursor:pointer;padding:8px 16px;border-radius:20px;font-size:14px;color:var(--md-primary,#8c351c);">Отменить</button>
          </div>
        </div>`;
      overlay.querySelector('b').textContent = title;
      document.body.appendChild(overlay);

      const style = document.createElement('style');
      style.textContent = '@keyframes bf-spin{to{transform:rotate(360deg)}}';
      document.head.appendChild(style);

      activeModal = {
        element: overlay,
        style: style,
        resolve: resolve,
        reject: reject
      };

      overlay.querySelector('.bf-stepup-cancel').addEventListener('click', function () {
        const m = activeModal;
        closeModal();
        m.reject(new Error('Подтверждение отменено'));
      });
    });
  }

  function setModalStatus(text, spin) {
    if (!activeModal) return;
    const statusEl = activeModal.element.querySelector('.bf-stepup-status');
    if (!statusEl) return;
    statusEl.querySelector('.text').textContent = text;
    statusEl.querySelector('span').style.animationPlayState = spin ? 'running' : 'paused';
    if (!spin) statusEl.querySelector('span').style.display = 'none';
  }

  function sleep(ms) {
    return new Promise(function (r) { setTimeout(r, ms); });
  }

  // Requests a Telegram confirmation and resolves with the confirmation id.
  BF.confirm = async function (action, parameters) {
    const response = await fetch('/api/stepup/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action: action, parameters: parameters || '' })
    });
    if (!response.ok) {
      const body = await response.json().catch(function () { return {}; });
      throw new Error(body.message || 'Не удалось отправить подтверждение в Telegram');
    }
    const data = await response.json();

    await showStepUpModal(data.title || 'Действие');
    setModalStatus('Ожидание подтверждения…', true);

    const deadline = Date.now() + 3 * 60 * 1000;
    while (Date.now() < deadline) {
      await sleep(2000);
      if (!activeModal) throw new Error('Подтверждение отменено');

      const statusRes = await fetch('/api/stepup/status/' + data.confirmationId);
      if (!statusRes.ok) {
        closeModal();
        throw new Error('Подтверждение истекло');
      }
      const status = await statusRes.json();

      if (status.status === 'approved') {
        closeModal();
        return data.confirmationId;
      }
      if (status.status === 'rejected') {
        closeModal();
        throw new Error('Действие отклонено в Telegram');
      }
      if (status.status === 'expired' || status.status === 'used') {
        closeModal();
        throw new Error('Подтверждение истекло');
      }
    }

    closeModal();
    throw new Error('Истекло время ожидания подтверждения');
  };

  // fetch wrapper: transparently handles 428 step-up and retries once
  BF.api = async function (url, options) {
    options = options || {};
    let response = await fetch(url, options);

    if (response.status === 428) {
      const body = await response.json().catch(function () { return null; });
      if (!body || !body.action) return response;

      const confirmationId = await BF.confirm(body.action, body.parameters);

      const headers = new Headers(options.headers || {});
      headers.set('X-Confirmation-Id', confirmationId);
      response = await fetch(url, Object.assign({}, options, { headers: headers }));
    }

    return response;
  };

  // For WebSocket connections (SSH console): returns the query string with the confirmation id.
  BF.stepUpQuery = async function (action, parameters) {
    const confirmationId = await BF.confirm(action, parameters);
    return 'confirmation=' + encodeURIComponent(confirmationId);
  };

  loadRoles();
})();
