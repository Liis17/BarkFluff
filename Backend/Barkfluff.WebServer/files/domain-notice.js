(function () {
  const notice = document.getElementById('domainNotice');
  if (!notice) return;

  const storageKey = 'bf_domain_notice_v1';
  const close = document.getElementById('domainNoticeClose');
  const text = document.getElementById('domainNoticeText');
  const link = document.getElementById('domainNoticeLink');
  const copy = {
    ru: {
      text: 'BarkFluff — некоммерческий проект мессенджера с открытым исходным кодом. Мы не связаны с прежним магазином товаров для животных, который использовал barkfluff.com.',
      link: 'Подробнее о проекте и истории домена →',
      close: 'Закрыть уведомление об истории домена'
    },
    en: {
      text: 'BarkFluff is a noncommercial, open-source messenger project. We are not affiliated with the former pet-products store that used barkfluff.com.',
      link: 'More about the project and domain history →',
      close: 'Dismiss the domain history notice'
    }
  };

  function applyLanguage() {
    const lang = document.documentElement.lang === 'ru' ? 'ru' : 'en';
    text.textContent = copy[lang].text;
    link.textContent = copy[lang].link;
    close.setAttribute('aria-label', copy[lang].close);
  }

  try { notice.hidden = localStorage.getItem(storageKey) === 'dismissed'; } catch (_) { }
  close.hidden = false;
  close.addEventListener('click', function () {
    notice.hidden = true;
    try { localStorage.setItem(storageKey, 'dismissed'); } catch (_) { }
  });

  applyLanguage();
  new MutationObserver(applyLanguage).observe(document.documentElement, {
    attributes: true, attributeFilter: ['lang']
  });
})();
