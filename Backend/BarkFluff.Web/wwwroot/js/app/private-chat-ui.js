/**
 * UI flow for passphrase-protected private chats.
 * Requires: BF.api, BF.privateChat, BF.realtime
 * Exposes: BF.privateChatUI
 */
(function () {
    "use strict";

    window.BF = window.BF || {};

    var deps;
    var privatePassOverlay;
    var privatePassTitle;
    var privatePassInput;
    var privatePassRemember;
    var privatePassError;
    var privatePassCancel;
    var privatePassOk;
    var privatePassActive = null;
    var chatEmpty;
    var chatHeader;
    var chatHeaderAvatar;
    var chatHeaderName;
    var chatHeaderStatus;
    var messagesArea;
    var messagesInner;
    var inputBar;
    var loadingMessages;
    var messageInput;
    var sendButton;
    var scrollToBottomButton;

    function $(selector) {
        return document.querySelector(selector);
    }

    function closePassphraseModal() {
        privatePassOverlay.classList.remove("visible");
        privatePassActive = null;
    }

    function promptPassphrase(chat, title, onDone) {
        var context = { chat: chat, onDone: onDone };
        privatePassActive = context;
        privatePassTitle.textContent = title;
        privatePassInput.value = "";
        privatePassRemember.checked = true;
        privatePassError.textContent = "";
        privatePassOk.disabled = false;
        privatePassOverlay.classList.add("visible");
        setTimeout(function () {
            privatePassInput.focus();
        }, 50);
    }

    function submitPassphrase() {
        if (!privatePassActive) return;
        var context = privatePassActive;
        var passphrase = privatePassInput.value;
        if (!passphrase) {
            privatePassError.textContent = BF.i18n.t(
                "privatechat.error.emptyPassword",
            );
            return;
        }
        privatePassOk.disabled = true;
        privatePassError.textContent = BF.i18n.t("privatechat.checking");
        BF.privateChat
            .deriveKey(passphrase, context.chat.kdfSalt)
            .then(function (key) {
                return BF.privateChat
                    .validateVerifier(key, context.chat.passphraseVerifier)
                    .then(function (isValid) {
                        if (privatePassActive !== context) return;
                        if (!isValid) {
                            privatePassOk.disabled = false;
                            privatePassError.textContent = BF.i18n.t(
                                "privatechat.error.wrongPassword",
                            );
                            return;
                        }
                        var remember = privatePassRemember.checked;
                        closePassphraseModal();
                        context.onDone(key, remember);
                    });
            })
            .catch(function (error) {
                console.error("[privateChat] deriveKey failed", error);
                if (privatePassActive !== context) return;
                privatePassOk.disabled = false;
                privatePassError.textContent = BF.i18n.t(
                    "privatechat.error.checkFailed",
                );
            });
    }

    function showCard(title, text, buttons) {
        messagesInner.innerHTML = "";
        var card = document.createElement("div");
        card.className = "private-card";
        var heading = document.createElement("div");
        heading.className = "private-card-title";
        heading.textContent = title;
        card.appendChild(heading);
        if (text) {
            var paragraph = document.createElement("div");
            paragraph.className = "private-card-text";
            paragraph.textContent = text;
            card.appendChild(paragraph);
        }
        if (buttons && buttons.length) {
            var actions = document.createElement("div");
            actions.className = "private-card-actions";
            buttons.forEach(function (button) {
                var element = document.createElement("button");
                element.className =
                    "private-card-btn" + (button.primary ? " primary" : "");
                element.textContent = button.label;
                element.addEventListener("click", button.onClick);
                actions.appendChild(element);
            });
            card.appendChild(actions);
        }
        messagesInner.appendChild(card);
    }

    function toUiMessage(chatId, encryptedMessage, text) {
        return {
            id: encryptedMessage.id,
            senderId: encryptedMessage.senderId,
            readBy: [],
            sentAt: encryptedMessage.sentAt,
            type: 1,
            isEdited: encryptedMessage.isEdited,
            editedAt: encryptedMessage.editedAt,
            content: {
                text:
                    text !== null && text !== undefined
                        ? text
                        : "\u{1F512} " + BF.i18n.t("privatechat.decryptFailed"),
                attachments: [],
            },
        };
    }

    function decryptBatch(chatId, encryptedMessages) {
        var alive = (encryptedMessages || []).filter(function (message) {
            return !message.isDeleted;
        });
        return Promise.all(
            alive.map(function (message) {
                return BF.privateChat
                    .decryptMessage(chatId, message)
                    .then(function (text) {
                        return toUiMessage(chatId, message, text);
                    });
            }),
        );
    }

    function open(chat) {
        deps.stopTypingSend(true);
        if (BF.pinned && BF.pinned.closeForChat) BF.pinned.closeForChat();

        deps.setCurrentChatId(chat.id);
        deps.updateOpenChatUrl(chat.id);
        if (BF.personalization) BF.personalization.applyForChat(chat.id);
        deps.setCurrentChatInfo(null);
        deps.setCurrentChatType(1);
        deps.setCurrentChatPeerIsBot(false);
        BF.realtime.unsubscribeTyping();
        deps.setMessages([]);
        deps.setNoMoreOlder(false);
        deps.resetKnownMessageIds();
        deps.clearPendingReply();
        deps.clearPendingEdit();
        deps.closeContextMenu();
        if (scrollToBottomButton)
            scrollToBottomButton.classList.remove("visible");
        chatEmpty.style.display = "none";
        chatHeader.classList.add("visible");
        messagesArea.parentElement.classList.add("visible");
        messagesArea.classList.add("visible");
        messagesInner.innerHTML = "";
        inputBar.classList.remove("visible");
        inputBar.classList.add("private-chat");
        loadingMessages.classList.remove("visible");
        deps.setChatCallButtonsVisible(false);
        deps.resetChatTabContext();

        var privatePeer = (chat.members || []).find(function (member) {
            return member.userId !== deps.getMyUserId();
        });
        if (privatePeer) {
            deps.getUser(privatePeer.userId)
                .then(function (peer) {
                    if (deps.getCurrentChatId() !== chat.id || !peer) return;
                    deps.setChatTabContext(
                        deps.chatTabTitle(peer),
                        peer.profilePicturePreview ||
                            peer.profilePicture ||
                            chat.picture ||
                            null,
                    );
                })
                .catch(function () {});
        }

        chatHeaderName.textContent =
            "\u{1F512} " + (chat.title || BF.i18n.t("newchat.mode.private"));
        if (chat.picture)
            chatHeaderAvatar.innerHTML =
                '<img src="' + deps.escapeHtml(chat.picture) + '" alt="">';
        else
            chatHeaderAvatar.textContent = (chat.title || "?")[0].toUpperCase();
        chatHeaderStatus.hidden = false;
        chatHeaderStatus.classList.remove("online");
        chatHeaderStatus.textContent = BF.i18n.t("newchat.mode.private");

        if (chat.countUnread > 0) {
            chat.countUnread = 0;
            deps.updateTitleBadge();
        }
        deps.renderChatList();

        if (chat.privateInviteState === 2) {
            showCard(
                BF.i18n.t("privatechat.inviteRejected"),
                BF.i18n.t("privatechat.inviteRejected.text"),
            );
            return;
        }
        if (chat.privateInviteState === 0) {
            if (
                chat.privateInviterUserId === deps.getMyUserId() ||
                !chat.privateInviterUserId
            ) {
                showCard(
                    BF.i18n.t("privatechat.waitingPeer"),
                    BF.i18n.t("privatechat.waitingPeer.text"),
                );
            } else {
                showInviteCard(chat);
            }
            return;
        }
        if (BF.privateChat.hasKey(chat.id)) {
            loadMessages(chat);
        } else {
            showUnlockCard(chat);
        }
    }

    function showInviteCard(chat) {
        showCard(
            BF.i18n.t("privatechat.invite"),
            BF.i18n.t("privatechat.invite.text"),
            [
                {
                    label: BF.i18n.t("call.reject"),
                    onClick: function () {
                        rejectInvite(chat);
                    },
                },
                {
                    label: BF.i18n.t("call.accept"),
                    primary: true,
                    onClick: function () {
                        acceptInvite(chat);
                    },
                },
            ],
        );
    }

    function showUnlockCard(chat) {
        showCard(
            BF.i18n.t("privatechat.locked"),
            BF.i18n.t("privatechat.locked.text"),
            [
                {
                    label: BF.i18n.t("privatechat.enterPassword"),
                    primary: true,
                    onClick: function () {
                        promptPassphrase(
                            chat,
                            BF.i18n.t("privatechat.password.title"),
                            function (key, remember) {
                                BF.privateChat.saveKey(chat.id, key, remember);
                                if (deps.getCurrentChatId() === chat.id)
                                    loadMessages(chat);
                            },
                        );
                    },
                },
            ],
        );
    }

    function acceptInvite(chat) {
        promptPassphrase(
            chat,
            BF.i18n.t("privatechat.password.title"),
            function (key, remember) {
                BF.api
                    .acceptPrivateChat(chat.id)
                    .then(function (response) {
                        BF.privateChat.saveKey(chat.id, key, remember);
                        var chats = deps.getChats();
                        var index = chats.findIndex(function (item) {
                            return item.id === chat.id;
                        });
                        var updated =
                            response && response.chat ? response.chat : chat;
                        updated.privateInviteState = 1;
                        updated.countUnread = 0;
                        if (index >= 0) chats[index] = updated;
                        deps.renderChatList();
                        if (deps.getCurrentChatId() === chat.id)
                            loadMessages(updated);
                    })
                    .catch(function (error) {
                        console.error(
                            "[privateChat] acceptPrivateChat failed",
                            error,
                        );
                        if (deps.getCurrentChatId() === chat.id)
                            showInviteCard(chat);
                    });
            },
        );
    }

    function rejectInvite(chat) {
        BF.api
            .rejectPrivateChat(chat.id)
            .then(function () {
                var chats = deps.getChats();
                var index = chats.findIndex(function (item) {
                    return item.id === chat.id;
                });
                if (index >= 0) chats.splice(index, 1);
                deps.renderChatList();
                deps.updateTitleBadge();
                if (deps.getCurrentChatId() === chat.id) close();
            })
            .catch(function (error) {
                console.error("[privateChat] rejectPrivateChat failed", error);
            });
    }

    function close() {
        deps.setCurrentChatId(null);
        deps.setCurrentChatType(0);
        deps.setMessages([]);
        messagesInner.innerHTML = "";
        chatHeader.classList.remove("visible");
        messagesArea.classList.remove("visible");
        messagesArea.parentElement.classList.remove("visible");
        inputBar.classList.remove("visible");
        chatEmpty.style.display = "";
        deps.resetChatTabContext();
    }

    function loadMessages(chat) {
        var chatId = chat.id;
        messagesInner.innerHTML = "";
        loadingMessages.classList.add("visible");
        return BF.api
            .listPrivateMessages(chatId, 0, 50, 0)
            .then(function (data) {
                if (chatId !== deps.getCurrentChatId()) return;
                return decryptBatch(chatId, data && data.messages).then(
                    function (mapped) {
                        if (chatId !== deps.getCurrentChatId()) return;
                        mapped.sort(function (left, right) {
                            return left.id - right.id;
                        });
                        deps.setMessages(mapped);
                        inputBar.classList.add("visible");
                        deps.renderMessages().then(deps.scrollToBottom);
                        var last = mapped.length
                            ? mapped[mapped.length - 1].id
                            : 0;
                        if (last)
                            BF.api
                                .markPrivateMessagesAsRead(chatId, last)
                                .catch(function () {});
                    },
                );
            })
            .catch(function (error) {
                console.error(
                    "[privateChat] listPrivateMessages failed",
                    error,
                );
                return false;
            })
            .finally(function () {
                loadingMessages.classList.remove("visible");
            });
    }

    function reload() {
        var chatId = deps.getCurrentChatId();
        if (deps.getCurrentChatType() !== 1 || !chatId)
            return Promise.resolve(true);
        var chat = deps.getChats().find(function (item) {
            return item.id === chatId;
        });
        if (
            chat &&
            chat.privateInviteState === 1 &&
            BF.privateChat.hasKey(chat.id)
        ) {
            return loadMessages(chat).then(function (result) {
                return result !== false;
            });
        }
        return Promise.resolve(true);
    }

    function send(text) {
        var sentChatId = deps.getCurrentChatId();
        sendButton.disabled = true;
        BF.privateChat
            .encryptText(sentChatId, text)
            .then(function (encrypted) {
                return BF.api.sendPrivateMessage(
                    sentChatId,
                    encrypted.ciphertext,
                    encrypted.nonce,
                    encrypted.associatedData,
                );
            })
            .then(function (response) {
                messageInput.value = "";
                messageInput.style.height = "auto";
                sendButton.disabled = false;
                messageInput.focus();
                if (response && response.message) {
                    var message = toUiMessage(
                        sentChatId,
                        response.message,
                        text,
                    );
                    BF.sound.play("tick");
                    var messages = deps.getMessages();
                    if (
                        sentChatId === deps.getCurrentChatId() &&
                        !messages.some(function (item) {
                            return item.id === message.id;
                        })
                    ) {
                        messages.push(message);
                        deps.appendMessageToView(message).then(
                            deps.scrollToBottom,
                        );
                    }
                    var chats = deps.getChats();
                    var chatIndex = chats.findIndex(function (item) {
                        return item.id === sentChatId;
                    });
                    if (chatIndex >= 0) {
                        var chat = chats[chatIndex];
                        chat.lastActivityAt = message.sentAt || Date.now();
                        chats.splice(chatIndex, 1);
                        chats.unshift(chat);
                        deps.renderChatList();
                    }
                }
            })
            .catch(function (error) {
                console.error("[privateChat] send failed", error);
                sendButton.disabled = false;
            });
    }

    function handleMessage(chatId, encryptedMessage) {
        var chats = deps.getChats();
        var chat = chats.find(function (item) {
            return item.id === chatId;
        });
        if (chat) {
            chat.lastActivityAt = encryptedMessage.sentAt || Date.now();
            if (
                chatId !== deps.getCurrentChatId() &&
                encryptedMessage.senderId !== deps.getMyUserId()
            ) {
                chat.countUnread = (chat.countUnread || 0) + 1;
            }
            var index = chats.indexOf(chat);
            chats.splice(index, 1);
            chats.unshift(chat);
            deps.renderChatList();
        } else {
            deps.loadChats(true);
        }
        deps.updateTitleBadge();

        if (encryptedMessage.senderId !== deps.getMyUserId()) {
            BF.sound.play("chime");
            deps.showNewMessageNotification(
                chat ? chat.title : BF.i18n.t("newchat.mode.private"),
                {
                    id: encryptedMessage.id,
                    chatId: chatId,
                    content: {
                        text:
                            "\u{1F512} " + BF.i18n.t("notification.newMessage"),
                    },
                },
            );
        }

        if (chatId !== deps.getCurrentChatId()) return;
        if (encryptedMessage.isDeleted) return;
        if (!BF.privateChat.hasKey(chatId)) return;
        var messages = deps.getMessages();
        if (
            messages.some(function (item) {
                return item.id === encryptedMessage.id;
            })
        )
            return;
        BF.privateChat
            .decryptMessage(chatId, encryptedMessage)
            .then(function (text) {
                if (chatId !== deps.getCurrentChatId()) return;
                var currentMessages = deps.getMessages();
                if (
                    currentMessages.some(function (item) {
                        return item.id === encryptedMessage.id;
                    })
                )
                    return;
                var message = toUiMessage(chatId, encryptedMessage, text);
                var isAtBottom =
                    messagesArea.scrollHeight -
                        messagesArea.scrollTop -
                        messagesArea.clientHeight <
                    300;
                currentMessages.push(message);
                deps.appendMessageToView(message).then(function () {
                    if (isAtBottom) deps.scrollToBottom();
                    else if (scrollToBottomButton)
                        scrollToBottomButton.classList.add("visible");
                });
                if (encryptedMessage.senderId !== deps.getMyUserId()) {
                    BF.api
                        .markPrivateMessagesAsRead(chatId, encryptedMessage.id)
                        .catch(function () {});
                }
            });
    }

    function init(options) {
        deps = options;
        privatePassOverlay = $("#privatePassOverlay");
        privatePassTitle = $("#privatePassTitle");
        privatePassInput = $("#privatePassInput");
        privatePassRemember = $("#privatePassRemember");
        privatePassError = $("#privatePassError");
        privatePassCancel = $("#privatePassCancel");
        privatePassOk = $("#privatePassOk");
        chatEmpty = $("#chatEmpty");
        chatHeader = $("#chatHeader");
        chatHeaderAvatar = $("#chatHeaderAvatar");
        chatHeaderName = $("#chatHeaderName");
        chatHeaderStatus = $("#chatHeaderStatus");
        messagesArea = $("#messagesArea");
        messagesInner = $("#messagesInner");
        inputBar = $("#inputBar");
        loadingMessages = $("#loadingMessages");
        messageInput = $("#messageInput");
        sendButton = $("#sendBtn");
        scrollToBottomButton = $("#scrollToBottomBtn");

        if (privatePassOk)
            privatePassOk.addEventListener("click", submitPassphrase);
        if (privatePassCancel)
            privatePassCancel.addEventListener("click", closePassphraseModal);
        if (privatePassInput)
            privatePassInput.addEventListener("keydown", function (event) {
                if (event.key === "Enter") {
                    event.preventDefault();
                    submitPassphrase();
                }
            });
        BF.realtime.on("private_message", function (data) {
            handleMessage(data.chatId, data.message);
        });
    }

    window.BF.privateChatUI = {
        init: init,
        open: open,
        reload: reload,
        send: send,
        decryptMessages: decryptBatch,
    };
})();
