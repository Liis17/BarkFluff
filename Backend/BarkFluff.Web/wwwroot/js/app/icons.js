/**
 * Shared platform icon pack.
 * Requires: /css/icons.css
 * Exposes: BF.icons
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var ROOT = '/icons/';
    var NAME_PATTERN = /^[a-z0-9-]+$/;

    function iconPath(name) {
        name = String(name || '');
        return NAME_PATTERN.test(name) ? name : null;
    }

    function iconUrl(name) {
        var path = iconPath(name);
        return path ? ROOT + path + '.svg' : '';
    }

    function safeClassName(value) {
        return String(value || '').split(/\s+/).filter(function (name) {
            return /^[a-zA-Z0-9_-]+$/.test(name);
        }).join(' ');
    }

    function apply(element, name) {
        var url = iconUrl(name);
        if (!url || !element) return element;

        element.classList.add('bf-icon');
        element.dataset.bfIcon = iconPath(name);
        element.setAttribute('aria-hidden', 'true');
        element.style.setProperty('--bf-icon-url', 'url("' + url + '")');
        return element;
    }

    function html(name, className) {
        var path = iconPath(name);
        var url = iconUrl(name);
        if (!path) return '';
        var classes = ('bf-icon ' + safeClassName(className)).trim();
        return '<span class="' + classes + '" data-bf-icon="' + path + '" aria-hidden="true"' +
            ' style="--bf-icon-url:url(\'' + url + '\')"></span>';
    }

    function element(name, className) {
        var result = document.createElement('span');
        var extraClasses = safeClassName(className);
        if (extraClasses) result.className = extraClasses;
        return apply(result, name);
    }

    function hydrate(root) {
        root = root || document;
        var elements = [];
        if (root.nodeType === 1 && root.hasAttribute('data-bf-icon')) elements.push(root);
        if (root.querySelectorAll) {
            elements = elements.concat(Array.prototype.slice.call(root.querySelectorAll('[data-bf-icon]')));
        }
        elements.forEach(function (el) {
            apply(el, el.dataset.bfIcon);
        });
    }

    window.BF.icons = {
        url: iconUrl,
        html: html,
        element: element,
        hydrate: hydrate
    };

    // messenger.html loads this module after its markup, so static icon nodes
    // are ready immediately; dynamic nodes use html()/element() above.
    hydrate(document);
})();
