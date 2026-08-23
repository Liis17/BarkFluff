/* =============================================================
   BarkFluff Admin — shared API and UI feedback helpers.
   Keeps BF.api() response-compatible with fetch while adding:
   Telegram step-up, auth expiry, normalized errors, loading and toasts.
   ============================================================= */

(function () {
  const BF = window.BF || (window.BF = {});
  BF.roles = BF.roles || [];
  const nativeFetch = window.fetch.bind(window);

  // Mirror of the server-side AdminPermissions matrix (UI hint only).
  const PERMISSION_ROLES = {
    'users.read': ['Support', 'SecurityAdmin'],
    'users.sessions.revoke': ['Support', 'SecurityAdmin'],
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
    if (!allowed) return false;
    return allowed.some(function (r) { return BF.roles.indexOf(r) !== -1; });
  };

  function publishRoles(me) {
    if (!me || !Array.isArray(me.roles)) return;
    BF.roles = me.roles;
    document.dispatchEvent(new CustomEvent('bf:roles', { detail: me.roles }));
  }

  function redirectToLogin() {
    if (window.location.pathname === '/') return;
    window.location.assign('/');
  }

  function setElementBusy(target, busy) {
    const element = typeof target === 'string' ? document.querySelector(target) : target;
    if (!element) return;

    if (busy) {
      element.setAttribute('aria-busy', 'true');
      element.classList.add('bf-busy');
      if ('disabled' in element) {
        element.dataset.bfWasDisabled = element.disabled ? 'true' : 'false';
        element.disabled = true;
      }
      return;
    }

    element.removeAttribute('aria-busy');
    element.classList.remove('bf-busy');
    if ('disabled' in element) {
      element.disabled = element.dataset.bfWasDisabled === 'true';
      delete element.dataset.bfWasDisabled;
    }
  }

  function getToastHost() {
    let host = document.getElementById('bf-toast-host');
    if (host) return host;

    host = document.createElement('div');
    host.id = 'bf-toast-host';
    host.className = 'bf-toast-host';
    host.setAttribute('aria-live', 'polite');
    document.body.appendChild(host);
    return host;
  }

  BF.toast = function (message, type, duration) {
    if (!message) return;
    const kind = type || 'info';
    const toast = document.createElement('div');
    toast.className = 'bf-toast bf-toast-' + kind;
    toast.setAttribute('role', kind === 'error' ? 'alert' : 'status');

    const icon = document.createElement('span');
    icon.className = 'msr size-20';
    icon.textContent = kind === 'success' ? 'check_circle'
      : kind === 'error' ? 'error'
      : kind === 'warning' ? 'warning'
      : 'info';

    const text = document.createElement('span');
    text.textContent = String(message);
    toast.append(icon, text);
    getToastHost().appendChild(toast);

    window.setTimeout(function () {
      toast.classList.add('bf-toast-leaving');
      window.setTimeout(function () { toast.remove(); }, 180);
    }, duration || 4500);
  };

  BF.setLoading = setElementBusy;

  BF.pageReady = function () {
    const overlay = document.getElementById('loading-overlay');
    if (!overlay) return;
    overlay.classList.add('hidden');
    window.setTimeout(function () { overlay.remove(); }, 300);
  };

  BF.ApiError = class ApiError extends Error {
    constructor(message, status, details, response) {
      super(message);
      this.name = 'ApiError';
      this.status = status || 0;
      this.details = details || null;
      this.response = response || null;
    }
  };

  async function readError(response) {
    let body = null;
    const contentType = response.headers.get('content-type') || '';
    try {
      body = contentType.includes('json')
        ? await response.clone().json()
        : await response.clone().text();
    } catch (_) {}

    let message = '';
    if (typeof body === 'string') message = body;
    else if (body) {
      message = body.message || body.detail || body.title || '';
      if (!message && body.errors && typeof body.errors === 'object') {
        message = Object.values(body.errors).flat().filter(Boolean).join(' ');
      }
    }

    if (!message) {
      if (response.status === 403) message = 'Недостаточно прав для выполнения действия';
      else if (response.status >= 500) message = 'Сервис временно недоступен';
      else message = 'Запрос завершился с ошибкой HTTP ' + response.status;
    }

    return new BF.ApiError(message, response.status, body, response);
  }

  BF.readError = readError;

  // -------- Step-up modal --------

  let activeModal = null;

  function closeModal() {
    if (activeModal) {
      activeModal.element.remove();
      activeModal.style.remove();
      activeModal = null;
    }
  }

  function showStepUpModal(title) {
    if (activeModal) throw new Error('Уже есть ожидающее подтверждение');

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

    activeModal = { element: overlay, style: style };

    overlay.querySelector('.bf-stepup-cancel').addEventListener('click', function () {
      closeModal();
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
    const response = await nativeFetch('/api/stepup/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action: action, parameters: parameters || '' })
    });
    if (!response.ok) {
      const body = await response.json().catch(function () { return {}; });
      throw new Error(body.message || 'Не удалось отправить подтверждение в Telegram');
    }
    const data = await response.json();

    showStepUpModal(data.title || 'Действие');
    setModalStatus('Ожидание подтверждения…', true);

    const deadline = Date.now() + 5 * 60 * 1000;
    while (Date.now() < deadline) {
      await sleep(2000);
      if (!activeModal) throw new Error('Подтверждение отменено');

      let statusRes;
      try {
        statusRes = await nativeFetch('/api/stepup/status/' + data.confirmationId);
      } catch (e) {
        closeModal();
        throw new Error('Не удалось проверить подтверждение');
      }
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

  // Response-compatible fetch wrapper: handles 428 step-up and auth expiry.
  BF.api = async function (url, options) {
    options = options || {};
    let response;
    try {
      response = await nativeFetch(url, options);
    } catch (error) {
      document.dispatchEvent(new CustomEvent('bf:api-error', { detail: error }));
      throw new BF.ApiError('Не удалось связаться с сервером', 0, error, null);
    }

    if (response.status === 428) {
      const body = await response.clone().json().catch(function () { return null; });
      if (!body || !body.action) return response;

      const confirmationId = await BF.confirm(body.action, body.parameters);

      const headers = new Headers(options.headers || {});
      headers.set('X-Confirmation-Id', confirmationId);
      response = await nativeFetch(url, Object.assign({}, options, { headers: headers }));
    }

    if (response.status === 401) {
      document.dispatchEvent(new CustomEvent('bf:unauthorized'));
      redirectToLogin();
    }

    return response;
  };

  // Parsed API call for new code. Throws BF.ApiError for any non-2xx response.
  BF.request = async function (url, options) {
    options = options || {};
    const requestOptions = Object.assign({}, options);
    const loading = requestOptions.loading;
    const responseType = requestOptions.responseType || 'auto';
    const errorToast = requestOptions.errorToast !== false;
    const successToast = requestOptions.successToast;
    delete requestOptions.loading;
    delete requestOptions.responseType;
    delete requestOptions.errorToast;
    delete requestOptions.successToast;

    setElementBusy(loading, true);
    try {
      const response = await BF.api(url, requestOptions);
      if (!response.ok) throw await readError(response);

      let result;
      if (responseType === 'response') result = response;
      else if (responseType === 'blob') result = await response.blob();
      else if (responseType === 'text') result = await response.text();
      else if (response.status === 204) result = null;
      else {
        const contentType = response.headers.get('content-type') || '';
        result = contentType.includes('json') ? await response.json() : await response.text();
      }

      if (successToast) BF.toast(successToast, 'success');
      return result;
    } catch (error) {
      const apiError = error instanceof BF.ApiError
        ? error
        : new BF.ApiError(error && error.message ? error.message : 'Неизвестная ошибка', 0, error, null);
      if (errorToast && apiError.status !== 401) BF.toast(apiError.message, 'error');
      throw apiError;
    } finally {
      setElementBusy(loading, false);
    }
  };

  let currentAdminPromise = null;
  BF.requireAuth = function (forceReload) {
    if (!currentAdminPromise || forceReload) {
      currentAdminPromise = BF.request('/api/auth/me', { errorToast: false })
        .then(function (me) {
          publishRoles(me);
          return me;
        })
        .catch(function (error) {
          currentAdminPromise = null;
          if (error.status !== 401) BF.toast(error.message, 'error');
          return null;
        });
    }
    return currentAdminPromise;
  };

  BF.logout = async function () {
    try {
      await BF.api('/api/auth/logout', { method: 'POST' });
    } finally {
      window.location.assign('/');
    }
  };

  // For WebSocket connections (SSH console): returns the query string with the confirmation id.
  BF.stepUpQuery = async function (action, parameters) {
    const confirmationId = await BF.confirm(action, parameters);
    return 'confirmation=' + encodeURIComponent(confirmationId);
  };

  BF.requireAuth();
})();
