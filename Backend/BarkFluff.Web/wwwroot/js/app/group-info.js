/**
 * Group information overlay: members, title, avatar and chat media.
 * Requires: BF.api, BF.files
 * Exposes: BF.groupInfo
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var deps;
    var overlay;
    var closeButton;
    var avatar;
    var avatarEdit;
    var avatarInput;
    var name;
    var nameEdit;
    var count;
    var membersElement;
    var addButton;
    var addBox;
    var addInput;
    var addResults;
    var searchTimer = null;

    function $(selector) {
        return document.querySelector(selector);
    }

    function renderAvatar(picture, title) {
        if (picture) {
            var image = document.createElement('img');
            image.src = picture;
            image.alt = '';
            avatar.replaceChildren(image);
        } else {
            avatar.textContent = (title || '?')[0].toUpperCase();
        }
    }

    function open() {
        var chat = deps.getCurrentChatInfo();
        var chatId = deps.getCurrentChatId();
        if (!chat || !chatId) return;
        name.textContent = chat.title || BF.i18n.t('group.default');
        var groupChatId = $('#groupChatId');
        if (groupChatId) groupChatId.textContent = chatId || '—';
        renderAvatar(chat.picture, chat.title);
        addBox.classList.add('hidden');
        addInput.value = '';
        addResults.innerHTML = '';
        loadMembers();
        deps.setMediaTabActive('#groupOverlay .group-media-tab', deps.groupMediaPanels, 'media');
        deps.renderChatMedia('media', deps.groupMediaPanels);
        BF.utils.openOverlay(overlay);
    }

    function loadMembers() {
        var chatId = deps.getCurrentChatId();
        membersElement.innerHTML = '';
        BF.api
            .listChatMembers(chatId)
            .then(function (data) {
                if (chatId !== deps.getCurrentChatId()) return;
                var members = (data && data.members) || [];
                count.textContent = BF.i18n.tp('group.memberCount', members.length);
                members.forEach(function (member) {
                    var fullName =
                        ((member.firstName || '') + ' ' + (member.lastName || '')).trim() || 'ID ' + member.userId;

                    var row = document.createElement('div');
                    row.className = 'group-member';

                    var memberAvatar = document.createElement('div');
                    memberAvatar.className = 'group-member-avatar';
                    memberAvatar.textContent = (fullName || '?')[0].toUpperCase();
                    row.appendChild(memberAvatar);

                    var memberName = document.createElement('div');
                    memberName.className = 'group-member-name';
                    memberName.textContent =
                        member.userId === deps.getMyUserId()
                            ? BF.i18n.t('group.member.you', { name: fullName })
                            : fullName;
                    row.appendChild(memberName);

                    if (member.userId !== deps.getMyUserId()) {
                        var removeButton = document.createElement('button');
                        removeButton.className = 'group-member-remove';
                        removeButton.innerHTML = '&times;';
                        removeButton.title = BF.i18n.t('common.delete');
                        removeButton.addEventListener('click', function () {
                            confirmRemoveMember(member, fullName);
                        });
                        row.appendChild(removeButton);
                    }

                    membersElement.appendChild(row);

                    deps.getUser(member.userId)
                        .then(function (user) {
                            if (!user) return;
                            var picture = user.profilePicturePreview || user.profilePicture;
                            if (picture) {
                                var image = document.createElement('img');
                                image.src = picture;
                                image.alt = '';
                                memberAvatar.replaceChildren(image);
                            }
                        })
                        .catch(function () {});
                });
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('group.error.loadMembers'));
            });
    }

    function confirmRemoveMember(member, memberName) {
        if (!window.confirm(BF.i18n.t('group.removeMember.confirm', { name: memberName }))) return;
        BF.api
            .kickUser(deps.getCurrentChatId(), member.userId)
            .then(loadMembers)
            .catch(function () {
                deps.showToast(BF.i18n.t('group.error.removeMember'));
            });
    }

    function rename() {
        var chat = deps.getCurrentChatInfo();
        var title = chat ? chat.title : '';
        var next = window.prompt(BF.i18n.t('newchat.groupTitle.placeholder'), title || '');
        if (next == null) return;
        next = next.trim();
        if (!next) {
            deps.showToast(BF.i18n.t('group.error.emptyTitle'));
            return;
        }
        var chatId = deps.getCurrentChatId();
        BF.api
            .updateGroupChat(chatId, next, null)
            .then(function (result) {
                var updatedTitle = (result && result.chat && result.chat.title) || next;
                var currentChat = deps.getCurrentChatInfo();
                if (currentChat) currentChat.title = updatedTitle;
                name.textContent = updatedTitle;
                deps.chatHeaderName.textContent = updatedTitle;
                var chatItem = deps.getChats().find(function (item) {
                    return item.id === chatId;
                });
                if (chatItem) {
                    chatItem.title = updatedTitle;
                    deps.renderChatList();
                }
                deps.showToast(BF.i18n.t('group.titleUpdated'));
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('group.error.renameFailed'));
            });
    }

    function addMember(userId, memberName) {
        BF.api
            .addUser(deps.getCurrentChatId(), userId)
            .then(function () {
                addBox.classList.add('hidden');
                addInput.value = '';
                addResults.innerHTML = '';
                loadMembers();
                deps.showToast(BF.i18n.t('group.memberAdded', { name: memberName }));
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('group.error.addMember'));
            });
    }

    function uploadAvatar() {
        var file = avatarInput.files[0];
        avatarInput.value = '';
        if (!file) return;
        deps.showToast(BF.i18n.t('common.loadingShort'));
        var chatId = deps.getCurrentChatId();
        BF.files
            .uploadFile(file, 6 /* CHAT_PICTURE */)
            .then(function (fileId) {
                return BF.api.updateGroupChat(chatId, null, fileId);
            })
            .then(function (result) {
                var picture = result && result.chat && result.chat.picture;
                if (picture) {
                    var currentChat = deps.getCurrentChatInfo();
                    if (currentChat) currentChat.picture = picture;
                    renderAvatar(picture, currentChat && currentChat.title);
                    deps.chatHeaderAvatar.innerHTML = '<img src="' + deps.escapeHtml(picture) + '" alt="">';
                    var chatItem = deps.getChats().find(function (item) {
                        return item.id === chatId;
                    });
                    if (chatItem) {
                        chatItem.picture = picture;
                        deps.renderChatList();
                    }
                }
                deps.showToast(BF.i18n.t('group.avatarUpdated'));
            })
            .catch(function () {
                deps.showToast(BF.i18n.t('group.error.avatarFailed'));
            });
    }

    function searchUsers() {
        var query = addInput.value.trim();
        if (searchTimer) clearTimeout(searchTimer);
        if (!query) {
            addResults.innerHTML = '';
            return;
        }
        searchTimer = setTimeout(function () {
            BF.api
                .searchUsers(query, 0, 20)
                .then(function (data) {
                    addResults.innerHTML = '';
                    (data.users || []).forEach(function (user) {
                        var fullName = ((user.firstName || '') + ' ' + (user.lastName || '')).trim() || user.username;
                        var row = document.createElement('div');
                        row.className = 'group-add-result';

                        var memberAvatar = document.createElement('div');
                        memberAvatar.className = 'group-member-avatar';
                        var picture = user.profilePicturePreview || user.profilePicture;
                        if (picture) {
                            var image = document.createElement('img');
                            image.src = picture;
                            image.alt = '';
                            memberAvatar.appendChild(image);
                        } else {
                            memberAvatar.textContent = (fullName || '?')[0].toUpperCase();
                        }
                        row.appendChild(memberAvatar);

                        var memberName = document.createElement('div');
                        memberName.className = 'group-member-name';
                        memberName.textContent = fullName;
                        row.appendChild(memberName);

                        row.addEventListener('click', function () {
                            addMember(user.id, fullName);
                        });
                        addResults.appendChild(row);
                    });
                })
                .catch(function () {});
        }, 300);
    }

    function init(options) {
        deps = options;
        overlay = $('#groupOverlay');
        closeButton = $('#groupClose');
        avatar = $('#groupAvatar');
        avatarEdit = $('#groupAvatarEdit');
        avatarInput = $('#groupAvatarInput');
        name = $('#groupName');
        nameEdit = $('#groupNameEdit');
        count = $('#groupCount');
        membersElement = $('#groupMembers');
        addButton = $('#groupAddBtn');
        addBox = $('#groupAddBox');
        addInput = $('#groupAddInput');
        addResults = $('#groupAddResults');

        closeButton.addEventListener('click', function () {
            BF.utils.closeOverlay(overlay);
        });
        overlay.addEventListener('click', function (event) {
            if (event.target === overlay) BF.utils.closeOverlay(overlay);
        });
        nameEdit.addEventListener('click', rename);

        var backgroundButton = $('#groupBackgroundButton');
        if (backgroundButton)
            backgroundButton.addEventListener('click', function () {
                var chat = deps.getCurrentChatInfo();
                deps.openChatBackgroundSelector(deps.getCurrentChatId(), chat && chat.title);
            });

        avatarEdit.addEventListener('click', function () {
            avatarInput.click();
        });
        avatarInput.addEventListener('change', uploadAvatar);

        addButton.addEventListener('click', function () {
            addBox.classList.toggle('hidden');
            if (!addBox.classList.contains('hidden')) addInput.focus();
        });
        addInput.addEventListener('input', searchUsers);

        document.querySelectorAll('.group-media-tab').forEach(function (tab) {
            tab.addEventListener('click', function () {
                deps.setMediaTabActive('#groupOverlay .group-media-tab', deps.groupMediaPanels, tab.dataset.type);
                deps.renderChatMedia(tab.dataset.type, deps.groupMediaPanels);
            });
        });
    }

    window.BF.groupInfo = {
        init: init,
        open: open
    };
})();
