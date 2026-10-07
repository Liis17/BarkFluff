/**
 * Sidebar drag-resize: тянущаяся граница списка чатов + компакт-режим (только аватары).
 * Requires: нет (только DOM + localStorage)
 * Exposes: BF.sidebarResize
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var MIN_WIDTH = 200;
    var COMPACT_WIDTH = 76;
    var DEFAULT_WIDTH = 340;
    var MOBILE_BP = 768;
    var STORAGE_KEY_WIDTH = 'bf_sidebar_width';
    var STORAGE_KEY_COMPACT = 'bf_sidebar_compact';

    var resizer;
    var normalWidth = DEFAULT_WIDTH;
    var dragging = false;

    function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

    function maxWidth() { return window.innerWidth / 2; }

    function applyWidth(px) {
        document.documentElement.style.setProperty('--sidebar-width', px + 'px');
    }

    function setCompact(on) {
        document.documentElement.classList.toggle('sidebar-compact', on);
    }

    function resetSearchForCompact() {
        var input = document.getElementById('searchInput');
        var results = document.getElementById('searchResults');
        if (input) input.value = '';
        if (results) {
            results.classList.remove('visible');
            results.innerHTML = '';
        }
    }

    function restore() {
        var compact = localStorage.getItem(STORAGE_KEY_COMPACT) === '1';
        var savedWidth = parseInt(localStorage.getItem(STORAGE_KEY_WIDTH), 10);
        normalWidth = savedWidth ? clamp(savedWidth, MIN_WIDTH, maxWidth()) : DEFAULT_WIDTH;
        setCompact(compact);
        applyWidth(compact ? COMPACT_WIDTH : normalWidth);
    }

    function save() {
        localStorage.setItem(STORAGE_KEY_COMPACT, document.documentElement.classList.contains('sidebar-compact') ? '1' : '0');
        localStorage.setItem(STORAGE_KEY_WIDTH, String(normalWidth));
    }

    function isMobile() {
        return window.innerWidth <= MOBILE_BP;
    }

    function onPointerDown(e) {
        if (isMobile()) return;
        dragging = true;
        document.documentElement.classList.add('sidebar-resizing');
        try { resizer.setPointerCapture(e.pointerId); } catch (_) { }
        e.preventDefault();
    }

    function onPointerMove(e) {
        if (!dragging) return;
        var newWidth = e.clientX - 3;
        if (newWidth < MIN_WIDTH) {
            setCompact(true);
            resetSearchForCompact();
            applyWidth(COMPACT_WIDTH);
        } else {
            setCompact(false);
            normalWidth = clamp(newWidth, MIN_WIDTH, maxWidth());
            applyWidth(normalWidth);
        }
    }

    function onPointerUp() {
        if (!dragging) return;
        dragging = false;
        document.documentElement.classList.remove('sidebar-resizing');
        save();
    }

    function onWindowResize() {
        if (document.documentElement.classList.contains('sidebar-compact')) return;
        var max = maxWidth();
        if (normalWidth > max) {
            normalWidth = Math.max(MIN_WIDTH, max);
            applyWidth(normalWidth);
        }
    }

    function init() {
        resizer = document.getElementById('sidebarResizer');
        if (!resizer) return;
        restore();
        resizer.addEventListener('pointerdown', onPointerDown);
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);
        window.addEventListener('resize', onWindowResize);
    }

    window.BF.sidebarResize = { init: init };
})();
