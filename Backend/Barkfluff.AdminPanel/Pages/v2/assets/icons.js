/* =============================================================
   BarkFluff — shared SVG icon pack helper
   Usage: bfIcon('restart', 'size-20')
   ============================================================= */

(function () {
  const ICONS_BASE = '/assets/icons/';

  function normalizePath(path) {
    return String(path || '')
      .split('/')
      .filter(Boolean)
      .map(part => encodeURIComponent(part))
      .join('/');
  }

  function iconUrl(path) {
    return `${ICONS_BASE}${normalizePath(path)}.svg`;
  }

  function classNames(className) {
    return String(className || '')
      .split(/\s+/)
      .filter(Boolean)
      .map(name => name.replace(/[^a-zA-Z0-9_-]/g, ''))
      .filter(Boolean)
      .join(' ');
  }

  window.bfIconUrl = iconUrl;

  window.bfIcon = function (path, className) {
    const classes = ['bf-icon', classNames(className)].filter(Boolean).join(' ');
    return `<span class="${classes}" style="--bf-icon-url:url('${iconUrl(path)}')" aria-hidden="true"></span>`;
  };

  window.bfSetIcon = function (element, path) {
    if (!element) return;
    element.style.setProperty('--bf-icon-url', `url('${iconUrl(path)}')`);
  };

  function hydrate(root) {
    root.querySelectorAll('[data-bf-icon]').forEach(element => {
      window.bfSetIcon(element, element.getAttribute('data-bf-icon'));
    });
  }

  window.bfHydrateIcons = hydrate;
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => hydrate(document));
  } else {
    hydrate(document);
  }
})();
