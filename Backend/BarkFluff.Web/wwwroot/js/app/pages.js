/**
 * Разделы в области чата: «Звонки» и «Настройки» открываются на месте открытого чата, а не модалкой.
 * Виден один раздел; открытие другого закрывает текущий через его модуль, открытие чата — closeAll().
 * Requires: нет (только DOM)
 * Exposes: BF.pages
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var pages = {};
    var current = null;

    function area() {
        return document.querySelector('.chat-area');
    }

    // close — функция модуля раздела: снимает его состояние и вызывает hide(name).
    function register(name, el, close) {
        pages[name] = { el: el, close: close };
    }

    function show(name) {
        var page = pages[name];
        if (!page || current === name) return;
        if (current) pages[current].close();
        current = name;
        page.el.classList.add('visible');
        var chatArea = area();
        if (chatArea) chatArea.classList.add('page-open');
        if (window.__mobileShowChat) window.__mobileShowChat();
        page.el.focus({ preventScroll: true });
    }

    function hide(name) {
        var page = pages[name];
        if (!page || current !== name) return;
        current = null;
        page.el.classList.remove('visible');
        var chatArea = area();
        if (chatArea) chatArea.classList.remove('page-open');
    }

    function closeAll() {
        if (current) pages[current].close();
    }

    function isOpen(name) {
        return name ? current === name : current !== null;
    }

    window.BF.pages = { register: register, show: show, hide: hide, closeAll: closeAll, isOpen: isOpen };
})();
