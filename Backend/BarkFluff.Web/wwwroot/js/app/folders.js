/**
 * Chat folders module — UI vertical tabs above chat list, state, drag-and-drop reorder,
 * create/edit/delete modal, integration with chat context menu.
 *
 * Requires: BF.api, BF.utils
 * Exposes: BF.folders
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var u = BF.utils;

    // --- State ---
    var foldersById = new Map();      // folderId → ChatFolderData {folderId, folderName, folderIcon, chatList, sortOrder}
    var sortedFolderIds = [];         // sorted by sortOrder ASC, id ASC
    var activeFolderId = 'all';
    var chatToFolders = new Map();    // chatId(string) → Set<folderId>
    var onChangeCb = null;            // called when active folder OR folder set changes (UI ререндер)

    // --- Emoji presets (closed grid in edit modal) ---
    var EMOJI_PRESETS = [
        '📥', // 📥
        '💼', // 💼
        '✈️', // ✈️
        '🎓', // 🎓
        '❤️', // ❤️
        '👨‍👩‍👧', // 👨‍👩‍👧
        '🛒', // 🛒
        '🎮', // 🎮
        '📚', // 📚
        '🏠', // 🏠
        '💰', // 💰
        '🎵', // 🎵
        '🍔', // 🍔
        '🏋️', // 🏋️
        '📰', // 📰
        '🐾', // 🐾
        '⚽',       // ⚽
        '🎬', // 🎬
        '✏️', // ✏️
        '⭐'        // ⭐
    ];

    // --- DOM refs (resolved at init) ---
    var folderTabsEl = null;
    var folderEditOverlay = null;
    var folderEditTitle = null;
    var folderNameInput = null;
    var folderEmojiGrid = null;
    var folderEditSaveBtn = null;
    var folderEditCancelBtn = null;
    var folderEditDeleteBtn = null;
    var folderEditCloseBtn = null;

    var editingFolderId = null;       // null = create mode
    var selectedIcon = '';            // currently selected emoji in modal

    // --- Helpers ---

    function rebuildChatToFolders() {
        chatToFolders = new Map();
        foldersById.forEach(function (folder) {
            (folder.chatList || []).forEach(function (chatId) {
                if (!chatToFolders.has(chatId)) chatToFolders.set(chatId, new Set());
                chatToFolders.get(chatId).add(folder.folderId);
            });
        });
    }

    function recomputeSortedIds() {
        var arr = [];
        foldersById.forEach(function (f) { arr.push(f); });
        arr.sort(function (a, b) {
            if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder;
            return (a.folderId < b.folderId) ? -1 : 1;
        });
        sortedFolderIds = arr.map(function (f) { return f.folderId; });
    }

    function applyServerFolder(folder) {
        if (!folder || !folder.folderId) return;
        foldersById.set(folder.folderId, folder);
        recomputeSortedIds();
        rebuildChatToFolders();
    }

    function notifyChange() {
        if (typeof onChangeCb === 'function') {
            try { onChangeCb(); } catch (e) { console.error(e); }
        }
    }

    // --- Public: init ---

    function init() {
        folderTabsEl       = document.getElementById('folderTabs');
        folderEditOverlay  = document.getElementById('folderEditOverlay');
        folderEditTitle    = document.getElementById('folderEditTitle');
        folderNameInput    = document.getElementById('folderNameInput');
        folderEmojiGrid    = document.getElementById('folderEmojiGrid');
        folderEditSaveBtn  = document.getElementById('folderEditSave');
        folderEditCancelBtn = document.getElementById('folderEditCancel');
        folderEditDeleteBtn = document.getElementById('folderEditDelete');
        folderEditCloseBtn = document.getElementById('folderEditClose');

        wireModal();

        return BF.api.getChatFolders().then(function (data) {
            foldersById.clear();
            (data && data.folders ? data.folders : []).forEach(function (f) {
                foldersById.set(f.folderId, f);
            });
            recomputeSortedIds();
            rebuildChatToFolders();
            renderTabs();
        }).catch(function (e) {
            console.error('[folders] getChatFolders failed', e);
            renderTabs();
        });
    }

    // --- Public: state queries ---

    function getActiveFolderId() { return activeFolderId; }

    function setActiveFolderId(id) {
        if (id !== 'all' && !foldersById.has(id)) id = 'all';
        if (id === activeFolderId) return;
        activeFolderId = id;
        renderTabs();
        notifyChange();
    }

    function filterChats(chats) {
        if (activeFolderId === 'all' || !foldersById.has(activeFolderId)) return chats;
        var folder = foldersById.get(activeFolderId);
        var ids = new Set(folder.chatList || []);
        return chats.filter(function (c) { return ids.has(c.id); });
    }

    function getFoldersForChat(chatId) {
        var ids = chatToFolders.get(chatId);
        if (!ids) return [];
        var res = [];
        sortedFolderIds.forEach(function (fid) {
            if (ids.has(fid)) res.push(foldersById.get(fid));
        });
        return res;
    }

    function getFoldersWithoutChat(chatId) {
        var ids = chatToFolders.get(chatId) || new Set();
        var res = [];
        sortedFolderIds.forEach(function (fid) {
            if (!ids.has(fid)) res.push(foldersById.get(fid));
        });
        return res;
    }

    function getAllFolders() {
        return sortedFolderIds.map(function (fid) { return foldersById.get(fid); });
    }

    function setOnChange(cb) { onChangeCb = cb; }

    // --- Public: mutations ---

    function addChatToFolder(folderId, chatId) {
        return BF.api.addChatToFolder(folderId, chatId).then(function (resp) {
            if (resp && resp.folder) applyServerFolder(resp.folder);
            renderTabs();
            notifyChange();
        }).catch(function (e) {
            console.error('[folders] addChatToFolder failed', e);
        });
    }

    function removeChatFromFolder(folderId, chatId) {
        return BF.api.removeChatFromFolder(folderId, chatId).then(function (resp) {
            if (resp && resp.folder) applyServerFolder(resp.folder);
            renderTabs();
            notifyChange();
        }).catch(function (e) {
            console.error('[folders] removeChatFromFolder failed', e);
        });
    }

    function createFolder(name, icon) {
        return BF.api.createChatFolder(name, icon || '').then(function (resp) {
            if (resp && resp.folder) {
                applyServerFolder(resp.folder);
                renderTabs();
                notifyChange();
            }
            return resp && resp.folder;
        });
    }

    function updateFolder(folderId, opts) {
        return BF.api.updateChatFolder(folderId, opts).then(function (resp) {
            if (resp && resp.folder) {
                applyServerFolder(resp.folder);
                renderTabs();
                notifyChange();
            }
            return resp && resp.folder;
        });
    }

    function deleteFolder(folderId) {
        return BF.api.deleteChatFolder(folderId).then(function () {
            foldersById.delete(folderId);
            recomputeSortedIds();
            rebuildChatToFolders();
            if (activeFolderId === folderId) activeFolderId = 'all';
            renderTabs();
            notifyChange();
        }).catch(function (e) {
            console.error('[folders] deleteChatFolder failed', e);
        });
    }

    // --- Tabs render + drag-and-drop ---

    function renderTabs() {
        if (!folderTabsEl) return;
        folderTabsEl.innerHTML = '';

        var allTab = document.createElement('button');
        allTab.type = 'button';
        allTab.className = 'folder-tab' + (activeFolderId === 'all' ? ' active' : '');
        allTab.dataset.folderId = 'all';
        allTab.textContent = 'Все чаты';
        allTab.addEventListener('click', function () { setActiveFolderId('all'); });
        folderTabsEl.appendChild(allTab);

        sortedFolderIds.forEach(function (fid) {
            var f = foldersById.get(fid);
            if (!f) return;
            var tab = document.createElement('button');
            tab.type = 'button';
            tab.className = 'folder-tab' + (activeFolderId === fid ? ' active' : '');
            tab.dataset.folderId = fid;
            tab.draggable = true;

            var label = '';
            if (f.folderIcon) label += f.folderIcon + ' ';
            label += f.folderName || 'Папка';
            tab.textContent = label;

            tab.title = f.folderName || '';

            tab.addEventListener('click', function () { setActiveFolderId(fid); });

            // Контекстное меню на вкладке: редактировать/удалить
            tab.addEventListener('contextmenu', function (e) {
                e.preventDefault();
                openEditModal(fid);
            });

            // DnD reorder
            tab.addEventListener('dragstart', function (e) {
                tab.classList.add('dragging');
                e.dataTransfer.effectAllowed = 'move';
                try { e.dataTransfer.setData('text/plain', fid); } catch (er) {}
            });
            tab.addEventListener('dragend', function () {
                tab.classList.remove('dragging');
                folderTabsEl.querySelectorAll('.folder-tab.drop-target').forEach(function (el) {
                    el.classList.remove('drop-target');
                });
            });
            tab.addEventListener('dragover', function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                tab.classList.add('drop-target');
            });
            tab.addEventListener('dragleave', function () {
                tab.classList.remove('drop-target');
            });
            tab.addEventListener('drop', function (e) {
                e.preventDefault();
                tab.classList.remove('drop-target');
                var draggedId = '';
                try { draggedId = e.dataTransfer.getData('text/plain'); } catch (er) {}
                if (!draggedId || draggedId === fid) return;
                handleReorderDrop(draggedId, fid, e);
            });

            folderTabsEl.appendChild(tab);
        });
    }

    function handleReorderDrop(draggedId, targetId, evt) {
        var fromIdx = sortedFolderIds.indexOf(draggedId);
        var toIdx = sortedFolderIds.indexOf(targetId);
        if (fromIdx < 0 || toIdx < 0) return;

        // Дроп левее половины целевой вкладки — вставка ДО неё; иначе ПОСЛЕ.
        var rect = evt.currentTarget.getBoundingClientRect();
        var insertAfter = (evt.clientX - rect.left) > rect.width / 2;

        var arr = sortedFolderIds.slice();
        arr.splice(fromIdx, 1);
        var newToIdx = arr.indexOf(targetId);
        if (insertAfter) newToIdx++;
        arr.splice(newToIdx, 0, draggedId);
        sortedFolderIds = arr;

        // Оптимистично пересчитать sortOrder
        var orders = [];
        sortedFolderIds.forEach(function (fid, i) {
            var f = foldersById.get(fid);
            if (f) {
                f.sortOrder = i;
                orders.push({ folderId: fid, sortOrder: i });
            }
        });
        renderTabs();

        BF.api.reorderChatFolders(orders).catch(function (e) {
            console.error('[folders] reorderChatFolders failed, refetching', e);
            // На ошибке — полный рефреш с сервера
            BF.api.getChatFolders().then(function (data) {
                foldersById.clear();
                (data && data.folders ? data.folders : []).forEach(function (f) {
                    foldersById.set(f.folderId, f);
                });
                recomputeSortedIds();
                rebuildChatToFolders();
                renderTabs();
                notifyChange();
            });
        });
    }

    // --- Edit modal (create / edit / delete) ---

    function wireModal() {
        if (!folderEditOverlay) return;

        renderEmojiGrid();

        if (folderEditCancelBtn) folderEditCancelBtn.addEventListener('click', closeEditModal);
        if (folderEditCloseBtn) folderEditCloseBtn.addEventListener('click', closeEditModal);
        folderEditOverlay.addEventListener('click', function (e) {
            if (e.target === folderEditOverlay) closeEditModal();
        });

        if (folderEditSaveBtn) {
            folderEditSaveBtn.addEventListener('click', onSaveClick);
        }
        if (folderEditDeleteBtn) {
            folderEditDeleteBtn.addEventListener('click', onDeleteClick);
        }

        // Submit by Enter in name input
        if (folderNameInput) {
            folderNameInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); onSaveClick(); }
                if (e.key === 'Escape') { e.preventDefault(); closeEditModal(); }
            });
        }
    }

    function renderEmojiGrid() {
        if (!folderEmojiGrid) return;
        folderEmojiGrid.innerHTML = '';

        // "Без иконки"
        var none = document.createElement('button');
        none.type = 'button';
        none.className = 'emoji-cell emoji-none';
        none.textContent = '—';
        none.title = 'Без иконки';
        none.dataset.icon = '';
        none.addEventListener('click', function () { selectIcon(''); });
        folderEmojiGrid.appendChild(none);

        EMOJI_PRESETS.forEach(function (em) {
            var cell = document.createElement('button');
            cell.type = 'button';
            cell.className = 'emoji-cell';
            cell.textContent = em;
            cell.dataset.icon = em;
            cell.addEventListener('click', function () { selectIcon(em); });
            folderEmojiGrid.appendChild(cell);
        });
    }

    function selectIcon(icon) {
        selectedIcon = icon;
        if (!folderEmojiGrid) return;
        folderEmojiGrid.querySelectorAll('.emoji-cell').forEach(function (cell) {
            cell.classList.toggle('selected', (cell.dataset.icon || '') === icon);
        });
    }

    function openCreateModal() {
        editingFolderId = null;
        if (folderEditTitle) folderEditTitle.textContent = 'Новая папка';
        if (folderNameInput) { folderNameInput.value = ''; folderNameInput.disabled = false; }
        selectIcon('');
        if (folderEditDeleteBtn) folderEditDeleteBtn.style.display = 'none';
        if (folderEditOverlay) folderEditOverlay.classList.add('visible');
        setTimeout(function () { try { folderNameInput.focus(); } catch (e) {} }, 0);
    }

    function openEditModal(folderId) {
        var f = foldersById.get(folderId);
        if (!f) return;
        editingFolderId = folderId;
        if (folderEditTitle) folderEditTitle.textContent = 'Редактировать папку';
        if (folderNameInput) { folderNameInput.value = f.folderName || ''; folderNameInput.disabled = false; }
        selectIcon(f.folderIcon || '');
        if (folderEditDeleteBtn) folderEditDeleteBtn.style.display = '';
        if (folderEditOverlay) folderEditOverlay.classList.add('visible');
        setTimeout(function () { try { folderNameInput.focus(); folderNameInput.select(); } catch (e) {} }, 0);
    }

    function closeEditModal() {
        if (folderEditOverlay) folderEditOverlay.classList.remove('visible');
        editingFolderId = null;
    }

    function onSaveClick() {
        if (!folderNameInput) return;
        var name = (folderNameInput.value || '').trim();
        if (!name) { folderNameInput.focus(); return; }
        if (name.length > 64) name = name.slice(0, 64);

        if (folderEditSaveBtn) folderEditSaveBtn.disabled = true;
        var p;
        if (editingFolderId == null) {
            p = createFolder(name, selectedIcon);
        } else {
            p = updateFolder(editingFolderId, {
                folderName: name,
                folderIcon: selectedIcon
            });
        }
        p.then(function () {
            closeEditModal();
        }).catch(function (e) {
            console.error('[folders] save failed', e);
        }).then(function () {
            if (folderEditSaveBtn) folderEditSaveBtn.disabled = false;
        });
    }

    function onDeleteClick() {
        if (editingFolderId == null) return;
        if (!confirm('Удалить эту папку? Чаты в ней не будут удалены.')) return;
        if (folderEditDeleteBtn) folderEditDeleteBtn.disabled = true;
        deleteFolder(editingFolderId).then(function () {
            closeEditModal();
        }).catch(function () {}).then(function () {
            if (folderEditDeleteBtn) folderEditDeleteBtn.disabled = false;
        });
    }

    // --- Public exports ---

    window.BF.folders = {
        init: init,
        setOnChange: setOnChange,
        getActiveFolderId: getActiveFolderId,
        setActiveFolderId: setActiveFolderId,
        filterChats: filterChats,
        getFoldersForChat: getFoldersForChat,
        getFoldersWithoutChat: getFoldersWithoutChat,
        getAllFolders: getAllFolders,
        addChatToFolder: addChatToFolder,
        removeChatFromFolder: removeChatFromFolder,
        openCreateModal: openCreateModal,
        openEditModal: openEditModal
    };
})();
