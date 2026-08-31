(() => {
  const state = { csrf: '', groups: [], current: 0, locked: false };
  const $ = (selector) => document.querySelector(selector);
  const loginView = $('#login-view');
  const setupView = $('#setup-view');

  $('#toggle-token').addEventListener('click', () => {
    const input = $('#token');
    input.type = input.type === 'password' ? 'text' : 'password';
  });

  $('#login-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    setMessage($('#login-error'), '');
    const token = $('#token').value;
    try {
      const result = await api('/api/session', { method: 'POST', body: { token } });
      state.csrf = result.csrfToken;
      $('#token').value = '';
      await loadState();
    } catch (error) {
      setMessage($('#login-error'), error.message || 'Не удалось войти.', 'error');
    }
  });

  $('#logout').addEventListener('click', async () => {
    await fetch('/api/session', { method: 'DELETE', credentials: 'same-origin' });
    state.csrf = '';
    state.groups = [];
    setupView.hidden = true;
    loginView.hidden = false;
    $('#logout').hidden = true;
    $('#progress').hidden = true;
    $('#page-title').textContent = 'Вход в консоль';
  });

  $('#previous').addEventListener('click', () => {
    if (state.current > 0) { state.current--; render(); }
  });
  $('#save').addEventListener('click', () => saveCurrent(false));
  $('#next').addEventListener('click', () => saveCurrent(true));
  $('#complete').addEventListener('click', complete);

  async function loadState() {
    try {
      const response = await api('/api/setup/state');
      state.groups = response.groups || [];
      state.locked = response.locked;
      state.current = firstIncomplete();
      loginView.hidden = true;
      setupView.hidden = false;
      $('#logout').hidden = false;
      $('#page-title').textContent = response.locked ? 'Настройка завершена' : 'Настройка сервера';
      render();
    } catch (error) {
      if (error.status === 401) {
        loginView.hidden = false;
        setupView.hidden = true;
        $('#logout').hidden = true;
      } else {
        setMessage($('#login-error'), error.message || 'Settings пока недоступен.', 'error');
      }
    }
  }

  function firstIncomplete() {
    const index = state.groups.findIndex(group => !group.complete);
    return index < 0 ? Math.max(0, state.groups.length - 1) : index;
  }

  function render() {
    renderSteps();
    const group = state.groups[state.current];
    if (!group) return;
    $('#progress').hidden = false;
    $('#progress').textContent = `${state.current + 1} / ${state.groups.length}`;
    $('#group-content').innerHTML = groupTemplate(group, state.current);
    $('#previous').disabled = state.current === 0;
    $('#save').hidden = state.locked;
    $('#next').hidden = state.locked || state.current === state.groups.length - 1;
    $('#complete').hidden = state.locked || state.current !== state.groups.length - 1;
    $('#locked-banner').hidden = !state.locked;
    $('#action-message').hidden = true;
    $('#group-content').querySelectorAll('[data-field-id]').forEach(input => {
      input.addEventListener('input', () => clearFieldError(input));
      input.addEventListener('change', () => clearFieldError(input));
    });
  }

  function renderSteps() {
    $('#steps').innerHTML = state.groups.map((group, index) => {
      const active = index === state.current ? ' active' : '';
      const complete = group.complete ? ' complete' : '';
      const optional = group.id === 'federation' && !group.applicable ? ' optional' : '';
      return `<button class="step${active}${complete}${optional}" type="button" data-step="${index}">
        <span class="step-number">${group.complete ? '✓' : index + 1}</span>
        <span><span class="step-title">${escapeHtml(group.title)}</span><small class="step-state">${group.complete ? 'Готово' : 'Требует заполнения'}</small></span>
      </button>`;
    }).join('');
    $('#steps').querySelectorAll('[data-step]').forEach(button => {
      button.addEventListener('click', () => {
        const index = Number(button.dataset.step);
        if (state.locked || index <= firstIncomplete() || state.groups[index].complete) {
          state.current = index;
          render();
        }
      });
    });
  }

  function groupTemplate(group, index) {
    const fields = (group.fields || []).filter(field => field.applicable);
    return `<article class="group-card">
      <div class="group-heading">
        <span class="group-index">${index + 1}</span>
        <div><h3>${escapeHtml(group.title)}</h3><p>${escapeHtml(group.description)}</p></div>
        ${group.complete ? '<span class="optional-tag">ГОТОВО</span>' : ''}
      </div>
      <div class="field-list">${fields.map(fieldTemplate).join('')}</div>
    </article>`;
  }

  function fieldTemplate(field) {
    const required = field.required ? '<span class="required"> · обязательно</span>' : '';
    const configured = field.configured ? '<span class="configured">Сохранено</span>' : '';
    const value = field.sensitive ? '' : (field.value || '');
    const placeholder = field.sensitive && field.configured ? 'Оставьте пустым, чтобы сохранить текущее значение' : (field.placeholder || '');
    const control = controlTemplate(field, value, placeholder);
    return `<div class="setup-field${field.error ? ' invalid' : ''}">
      <div class="field-top"><label for="field-${escapeAttr(field.id)}">${escapeHtml(field.label)}${required}</label>${configured}</div>
      <p class="field-description">${escapeHtml(field.description)}</p>
      ${control}
      <div class="field-error">${escapeHtml(field.error || '')}</div>
    </div>`;
  }

  function controlTemplate(field, value, placeholder) {
    const id = `field-${escapeAttr(field.id)}`;
    const common = `id="${id}" data-field-id="${escapeAttr(field.id)}" placeholder="${escapeAttr(placeholder)}" ${state.locked ? 'disabled' : ''}`;
    switch (field.inputType) {
      case 'TextArea': return `<textarea ${common} rows="3">${escapeHtml(value)}</textarea>`;
      case 'Color': return `<input ${common} type="text" value="${escapeAttr(value)}" maxlength="7">`;
      case 'Integer': return `<input ${common} type="number" value="${escapeAttr(value)}">`;
      case 'Secret': return `<input ${common} type="password" value="" autocomplete="new-password">`;
      case 'Email': return `<input ${common} type="email" value="${escapeAttr(value)}">`;
      case 'Boolean': return `<select ${common}><option value="false" ${value !== 'true' ? 'selected' : ''}>Выключено</option><option value="true" ${value === 'true' ? 'selected' : ''}>Включено</option></select>`;
      default: return `<input ${common} type="text" value="${escapeAttr(value)}">`;
    }
  }

  async function saveCurrent(advance) {
    const group = state.groups[state.current];
    const values = {};
    let valid = true;
    $('#group-content').querySelectorAll('[data-field-id]').forEach(input => {
      values[input.dataset.fieldId] = input.value;
      const field = group.fields.find(item => item.id === input.dataset.fieldId);
      if (field?.required && !input.value.trim() && !(field.sensitive && field.configured)) {
        showFieldError(input, 'Заполните обязательное поле.');
        valid = false;
      }
    });
    if (!valid) return;

    try {
      const response = await api(`/api/setup/groups/${encodeURIComponent(group.id)}`, { method: 'PUT', body: { values }, csrf: true });
      applyResponse(response);
      setMessage($('#action-message'), advance && state.current < state.groups.length - 1 ? 'Группа сохранена.' : 'Изменения сохранены.', 'success');
      if (advance && state.current < state.groups.length - 1) { state.current++; render(); }
    } catch (error) {
      setMessage($('#action-message'), error.message || 'Не удалось сохранить группу.', 'error');
      if (error.fieldId) showFieldError(document.querySelector(`[data-field-id="${CSS.escape(error.fieldId)}"]`), error.message);
    }
  }

  async function complete() {
    try {
      const response = await api('/api/setup/complete', { method: 'POST', csrf: true, body: {} });
      applyResponse(response);
      state.locked = true;
      render();
    } catch (error) {
      setMessage($('#action-message'), error.message || 'Заполните все обязательные поля.', 'error');
    }
  }

  function applyResponse(response) {
    const next = response.state || response;
    if (next.groups) {
      state.groups = next.groups;
      state.locked = !!next.locked;
    }
  }

  async function api(url, options = {}) {
    const init = { method: options.method || 'GET', credentials: 'same-origin', headers: { 'Accept': 'application/json' } };
    if (options.body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(options.body);
    }
    if (options.csrf) init.headers['X-CSRF-Token'] = state.csrf;
    const response = await fetch(url, init);
    const csrf = response.headers.get('X-CSRF-Token');
    if (csrf) state.csrf = csrf;
    let payload = null;
    try { payload = await response.json(); } catch { /* empty response */ }
    if (!response.ok) {
      const error = new Error(payload?.detail || payload?.error || `Ошибка запроса (${response.status})`);
      error.status = response.status;
      error.fieldId = payload?.fieldId;
      throw error;
    }
    return payload;
  }

  function showFieldError(input, message) {
    if (!input) return;
    const wrapper = input.closest('.setup-field');
    wrapper.classList.add('invalid');
    wrapper.querySelector('.field-error').textContent = message || '';
  }
  function clearFieldError(input) {
    const wrapper = input.closest('.setup-field');
    wrapper.classList.remove('invalid');
    wrapper.querySelector('.field-error').textContent = '';
  }
  function setMessage(element, message, type = '') {
    element.textContent = message || '';
    element.hidden = !message;
    element.className = `message ${type}`;
  }
  function escapeHtml(value) { return String(value ?? '').replace(/[&<>'"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[char])); }
  function escapeAttr(value) { return escapeHtml(value); }

  loadState();
})();
