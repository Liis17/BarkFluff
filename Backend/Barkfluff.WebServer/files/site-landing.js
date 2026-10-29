/* Общее поведение страниц сайта: переключение RU/EN по data-ru/data-en и выбор языка по bf_lang. */
(function () {
  var toggle = document.getElementById('langToggle');

  function applyLang(lang) {
    document.documentElement.lang = lang;
    try { localStorage.setItem('bf_lang', lang); } catch (_) { }
    document.querySelectorAll('[data-ru]').forEach(function (el) {
      el.textContent = el.getAttribute('data-' + lang);
    });
    if (toggle) toggle.textContent = lang === 'ru' ? 'EN' : 'RU';
    document.dispatchEvent(new CustomEvent('bf:langchange', { detail: lang }));
  }

  var saved = null;
  try { saved = localStorage.getItem('bf_lang'); } catch (_) { }
  var detected = (navigator.language || '').indexOf('ru') === 0 ? 'ru' : 'en';

  if (toggle) {
    toggle.addEventListener('click', function () {
      applyLang(document.documentElement.lang === 'ru' ? 'en' : 'ru');
    });
  }
  window.bfLang = function () { return document.documentElement.lang === 'en' ? 'en' : 'ru'; };
  applyLang(saved === 'ru' || saved === 'en' ? saved : detected);
})();
