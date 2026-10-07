/**
 * Connection status and catch-up: the "reconnecting / offline" banner, the "synced" chip, and the quiet re-sync of
 * the chat list and of the open chat's tail after a connection drop, a stream reopen or a return to the tab.
 * Requires: BF.api, BF.i18n, BF.realtime, BF.feed, BF.markRead, BF.messages
 * Exposes: BF.connection
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var messagesArea;
    var loadingMessages;
    var scrollToBottomBtn;
    var connectionBanner;
    var connectionBannerText;
    var connectionRetryButton;
    var connectionSynced;
    var connectionHadProblem = false;
    var connectionCatchUpRunning = false;
    var connectionSyncedTimer = null;
    var connectionStatusInitialized = false;
    var connectionStatusEpoch = 0;
    var connectionState = 'reconnecting';
    var resyncTimer = null;

    function $(selector) {
        return document.querySelector(selector);
    }

    function hideConnectionSynced() {
        if (connectionSyncedTimer) {
            clearTimeout(connectionSyncedTimer);
            connectionSyncedTimer = null;
        }
        if (connectionSynced) connectionSynced.classList.remove('visible');
    }

    function showConnectionSynced() {
        if (!deps.getCurrentChatId() || !connectionSynced) return;
        hideConnectionSynced();
        connectionSynced.classList.add('visible');
        connectionSyncedTimer = setTimeout(hideConnectionSynced, 3000);
    }

    function showConnectionProblem(state) {
        if (!connectionBanner) return;
        var offline = state === 'offline';
        connectionBanner.classList.toggle('visible', state !== 'connected');
        connectionBanner.classList.toggle('offline', offline);
        if (connectionBannerText) {
            connectionBannerText.textContent = BF.i18n.t(offline ? 'connection.offline' : 'connection.reconnecting');
        }
        if (connectionRetryButton) connectionRetryButton.disabled = false;
    }

    function runConnectionCatchUp() {
        if (connectionCatchUpRunning) return;
        connectionCatchUpRunning = true;
        var retryAfterCatchUp = false;
        var statusEpoch = connectionStatusEpoch;
        Promise.all([deps.refreshChatList(), resyncCurrentChatTail()])
            .then(function (results) {
                if (statusEpoch !== connectionStatusEpoch || connectionState !== 'connected') return;
                if (!results[0] || !results[1]) {
                    retryAfterCatchUp = true;
                    return;
                }
                connectionHadProblem = false;
                showConnectionSynced();
            })
            .finally(function () {
                connectionCatchUpRunning = false;
                if (retryAfterCatchUp && statusEpoch === connectionStatusEpoch && connectionState === 'connected') {
                    BF.realtime.reconnect();
                    return;
                }
                if (statusEpoch !== connectionStatusEpoch && connectionState === 'connected' && connectionHadProblem) {
                    runConnectionCatchUp();
                }
            });
    }

    // Дифф свежезагруженного хвоста против показанного окна (по id, editedAt/тексту,
    // числу прочитавших и удалениям в диапазоне окна). null — различий нет.
    function diffFetchedTail(fetched) {
        var messages = deps.getMessages();
        var byId = new Map();
        messages.forEach(function (m) {
            byId.set(String(m.id), m);
        });
        var minId = Infinity,
            maxId = -Infinity;
        var edits = [],
            readUpdates = [],
            news = [];
        for (var i = 0; i < fetched.length; i++) {
            var f = fetched[i];
            var fid = Number(f.id);
            if (fid < minId) minId = fid;
            if (fid > maxId) maxId = fid;
            var cur = byId.get(String(f.id));
            if (!cur) {
                news.push(f); // новое сообщение
                continue;
            }
            var ct = (cur.content && cur.content.text) || '';
            var ft = (f.content && f.content.text) || '';
            if ((cur.editedAt || 0) !== (f.editedAt || 0) || ct !== ft) {
                edits.push(f); // правка
                continue;
            }
            if ((cur.readBy || []).length !== (f.readBy || []).length) readUpdates.push(f); // прочтение
        }
        // Удаление: есть наше сообщение в диапазоне окна, которого нет в свежей выборке.
        var fetchedIds = new Set(
            fetched.map(function (m) {
                return String(m.id);
            })
        );
        var deletes = [];
        for (var j = 0; j < messages.length; j++) {
            var mid = Number(messages[j].id);
            if (mid >= minId && mid <= maxId && !fetchedIds.has(String(messages[j].id))) deletes.push(messages[j].id);
        }
        if (deletes.length === 0 && edits.length === 0 && readUpdates.length === 0 && news.length === 0) return null;
        news.sort(function (a, b) {
            return Number(a.id) - Number(b.id);
        });
        return { deletes: deletes, edits: edits, readUpdates: readUpdates, news: news };
    }

    // Точечное обновление галочек прочтения — view-часть handleMessageRead,
    // без мутации счётчиков непрочитанного и без ререндера списка чатов.
    function applyReadByUpdate(fetchedMsg) {
        var msg = deps.getMessages().find(function (m) {
            return String(m.id) === String(fetchedMsg.id);
        });
        if (!msg) return;
        msg.readBy = fetchedMsg.readBy || [];
        var el = messagesArea.querySelector('.msg-status[data-msg-id="' + fetchedMsg.id + '"]');
        if (el) {
            var myUserId = deps.getMyUserId();
            var rc = msg.readBy.filter(function (id) {
                return id !== myUserId;
            }).length;
            BF.messages.updateMessageStatus(el, rc > 0);
        }
    }

    // true — окно актуально (или сверять нечего), false — не удалось / отложено: вызывающий повторит позже.
    function resyncCurrentChatTail() {
        var chatId = deps.getCurrentChatId();
        if (!chatId) return Promise.resolve(true);
        if (BF.feed.isLoadingOlder() || loadingMessages.classList.contains('visible')) return Promise.resolve(false);
        if (deps.getCurrentChatType() === 1) return deps.reloadPrivateChat();
        return BF.api
            .getChatInfo(chatId)
            .then(function (info) {
                if (chatId !== deps.getCurrentChatId()) return { skipped: true };
                if (!info || info.error) return null;
                var fromId = info.firstUnreadMessageId || info.lastMessageId || 0;
                return BF.api.listMessages(chatId, fromId, 30, 10).then(function (data) {
                    return { info: info, data: data };
                });
            })
            .then(function (res) {
                if (res && res.skipped) return true;
                if (!res || !res.data || !res.data.messages) return false;
                if (chatId !== deps.getCurrentChatId()) return true;
                var fetched = res.data.messages;
                var diff = diffFetchedTail(fetched);
                if (!diff) return true; // за окно реконнекта ничего не пропало — DOM не трогаем
                deps.setCurrentChatInfo(res.info);
                var wasAtBottom = messagesArea.scrollHeight - messagesArea.scrollTop - messagesArea.clientHeight < 300;

                // Новые сообщения не в хвосте (окно прыгало через scrollToMessage, или своё
                // сообщение ушло раньше resync-дебаунса) — точечно не вставить, полный render.
                var numericMessageIds = deps
                    .getMessages()
                    .map(function (m) {
                        return Number(m.id);
                    })
                    .filter(Number.isFinite);
                var maxCurId = numericMessageIds.length > 0 ? Math.max.apply(null, numericMessageIds) : -Infinity;
                var tailOnly =
                    numericMessageIds.length > 0 &&
                    diff.news.every(function (m) {
                        return Number(m.id) > maxCurId;
                    });
                // Якорь разделителя «Новые сообщения»: первое пропущенное. Не ставим, когда
                // пользователь был у нижнего края — там догруженное сразу помечается прочитанным.
                BF.feed.setResyncSeparatorId(!wasAtBottom && diff.news.length > 0 ? diff.news[0].id : null);
                if (!tailOnly) {
                    deps.setMessages(fetched);
                    deps.mergePendingUploads(chatId);
                    BF.feed.clearNewerGap(false); // буфер заменён хвостом — обрезанного «вперёд» больше нет
                    return deps.renderMessages().then(function () {
                        if (wasAtBottom) deps.scrollToBottom();
                        return deps.refreshChatList();
                    });
                }

                // Точечное применение диффа — как live-события, но без звуков/нотификаций.
                diff.deletes.forEach(function (id) {
                    deps.applyMessageDelete(chatId, id);
                });
                diff.edits.forEach(function (m) {
                    deps.applyMessageEdit(chatId, m);
                });
                diff.readUpdates.forEach(applyReadByUpdate);

                var chain = Promise.resolve();
                var firstNewsAppended = false;
                diff.news.forEach(function (m) {
                    chain = chain.then(function () {
                        if (chatId !== deps.getCurrentChatId()) return;
                        if (deps.reconcilePendingUpload(chatId, m)) return;
                        deps.getMessages().push(m);
                        // Перед первым дописанным — разделитель «Новые сообщения».
                        var sepKey = BF.feed.getResyncSeparatorId() && !firstNewsAppended ? 'chat.newMessages' : null;
                        firstNewsAppended = true;
                        return deps.appendMessageToView(m, sepKey);
                    });
                });
                return chain
                    .then(function () {
                        if (chatId !== deps.getCurrentChatId() || diff.news.length === 0) return;
                        if (wasAtBottom) {
                            deps.scrollToBottom();
                            if (scrollToBottomBtn) scrollToBottomBtn.classList.remove('visible');
                            var myUserId = deps.getMyUserId();
                            var incomingIds = diff.news
                                .filter(function (m) {
                                    return m.senderId !== myUserId;
                                })
                                .map(function (m) {
                                    return m.id;
                                });
                            if (incomingIds.length > 0) BF.markRead.markSoon(incomingIds);
                        } else {
                            if (scrollToBottomBtn) scrollToBottomBtn.classList.add('visible');
                        }
                    })
                    .then(function () {
                        // В открытом чате что-то пропустили → вероятно, пропуски есть и в других
                        // чатах: тихо освежаем превью/счётчики непрочитанного в сайдбаре.
                        return deps.refreshChatList();
                    });
            })
            .then(function (result) {
                return result !== false;
            })
            .catch(function () {
                return false;
            });
    }

    function init(options) {
        deps = options;
        messagesArea = $('#messagesArea');
        loadingMessages = $('#loadingMessages');
        scrollToBottomBtn = $('#scrollToBottomBtn');
        connectionBanner = $('#connectionBanner');
        connectionBannerText = $('#connectionBannerText');
        connectionRetryButton = $('#connectionRetryButton');
        connectionSynced = $('#connectionSynced');

        BF.realtime.on('connection_status', function (data) {
            var state = data && data.state ? data.state : data && data.connected ? 'connected' : 'reconnecting';
            var initialStatus = !connectionStatusInitialized;
            connectionStatusInitialized = true;
            connectionStatusEpoch++;
            connectionState = state;
            showConnectionProblem(state);
            if (state !== 'connected') {
                if (!initialStatus) connectionHadProblem = true;
                hideConnectionSynced();
                return;
            }
            if (connectionHadProblem) runConnectionCatchUp();
        });

        if (connectionRetryButton) {
            connectionRetryButton.addEventListener('click', function () {
                if (BF.realtime.reconnect()) connectionRetryButton.disabled = true;
            });
        }

        // ── RESYNC: реконнект отдельного стрима ──────────────────────────────────
        // realtime.js шлёт 'resync' при ЛЮБОМ переоткрытии стрима (backoff/watchdog/
        // age-timer/visibility). Это ловит случай, когда отвалился только поток новых
        // сообщений, а остальные живы — тогда connection_status (OR по 4 стримам) не
        // флипается и обычный catch-up не срабатывает. За время разрыва server-streaming
        // не реплеит пропущенное → сообщение видно лишь после ручного переоткрытия чата.
        //
        // Дебаунсим: при churn'е все 4 стрима реконнектятся почти одновременно — склеиваем
        // в один проход. Дозагрузка тихая: ререндерим только если хвост реально изменился,
        // иначе (обычный случай — за окно реконнекта ничего не пропало) DOM не трогаем.
        BF.realtime.on('resync', function () {
            if (resyncTimer) return;
            resyncTimer = setTimeout(function () {
                resyncTimer = null;
                if (!deps.getCurrentChatId()) {
                    // Чат не открыт — обновлять нечего в области сообщений; освежаем сайдбар.
                    deps.refreshChatList();
                    return;
                }
                // Тихо сверяем хвост открытого чата. Сайдбар трогаем только если что-то
                // реально пропустили (тогда вероятны пропуски и в других чатах), чтобы не
                // ререндерить список и не сбрасывать его прокрутку на каждом churn-цикле.
                resyncCurrentChatTail();
            }, 1200);
        });

        // Возврат на вкладку: тихий catch-up пропущенного за время скрытой вкладки — диффы вместо полного ререндера.
        BF.realtime.on('tab_visible', function () {
            deps.refreshChatList();
            resyncCurrentChatTail();
        });
    }

    window.BF.connection = {
        init: init,
        resyncCurrentChatTail: resyncCurrentChatTail,
        diffFetchedTail: diffFetchedTail
    };
})();
