/**
 * High-level API wrappers over gRPC-Web clients.
 * All methods return Promises resolving to plain JS objects (not protobuf messages).
 * Requires: BF.clients, window.proto
 * Exposes: BF.api
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var c = function () { return BF.clients; };
    var identity = function () { return c().identity; };
    var messages = function () { return c().messages; };
    var users = function () { return c().users; };
    var files = function () { return c().files; };
    var onliner = function () { return c().onliner; };

    var sharedPb = function () { return window.proto.barkfluff.shared; };
    var msgPb = function () { return window.proto.barkfluff.messages; };
    var usrPb = function () { return window.proto.barkfluff.users; };
    var filePb = function () { return window.proto.barkfluff.files; };
    var onlPb = function () { return window.proto.barkfluff.onliner; };

    // --- Helpers to convert protobuf → plain JS ---

    function tsToMs(ts) {
        if (!ts) return null;
        return ts.toDate().getTime();
    }

    function mapAttachment(a) {
        return {
            id: a.getId(),
            type: enumName(a.getType()),
            fileId: a.getFileId(),
            previewUrl: a.getPreviewUrl(),
            attachmentSize: a.getAttachmentSize(),
            previewFileId: a.getPreviewFileId(),
            fileName: a.getFileName()
        };
    }

    function mapMessage(m) {
        var content = m.getContent();
        return {
            id: m.getId(),
            senderId: m.getSenderId(),
            readBy: m.getReadByList(),
            sentAt: tsToMs(m.getSentAt()),
            type: enumName(m.getType()),
            content: {
                text: content ? content.getText() : '',
                attachments: content ? content.getAttachmentsList().map(mapAttachment) : []
            }
        };
    }

    function mapChat(ch) {
        var lm = ch.getLastMessage();
        return {
            id: ch.getId(),
            title: ch.getTitle(),
            picture: ch.getPicture(),
            isGroupChat: ch.getIsGroupChat(),
            lastMessage: lm ? mapMessage(lm) : null,
            members: ch.getMembersList().map(function (m) { return { userId: m.getUserId() }; }),
            countUnread: ch.getCountUnread(),
            firstUnreadMessageId: ch.getFirstUnreadMessageId()
        };
    }

    function mapUser(u) {
        return {
            id: u.getId(),
            firstName: u.getFirstName(),
            lastName: u.getLastName(),
            username: u.getUsername(),
            profilePicture: u.getProfilePicture(),
            profilePicturePreview: u.getProfilePicturePreview(),
            bio: u.getBio(),
            registrationDate: tsToMs(u.getRegistrationDate()),
            badges: u.getBadgesList().map(function (b) {
                var badge = b.getBadge();
                return {
                    name: badge ? badge.getName() : '',
                    imageUrl: badge ? badge.getImageUrl() : '',
                    priority: b.getPriority()
                };
            })
        };
    }

    // Convert proto enum int to string name using enum object reverse mapping
    var _enumCache = {};
    function enumName(val) {
        // MessageAttachmentType / MessageContentType / StatusTypeId — all numeric
        // grpc-web returns numbers; proto enums define string keys
        if (typeof val === 'string') return val;
        // Try known enums
        var mat = sharedPb().MessageAttachmentType;
        if (mat) {
            if (!_enumCache.mat) {
                _enumCache.mat = {};
                for (var k in mat) if (typeof mat[k] === 'number') _enumCache.mat[mat[k]] = k;
            }
            if (_enumCache.mat[val]) return _enumCache.mat[val];
        }
        return String(val);
    }

    // --- API methods ---

    function listChats(offset, size) {
        var req = new (msgPb().ListChatsRequest)();
        var pg = new (sharedPb().PageRequest)();
        pg.setOffset(offset || 0);
        pg.setSize(Math.min(size || 50, 50));
        req.setPagination(pg);
        return c().authCall(messages().listChats.bind(messages()), req).then(function (resp) {
            return {
                chats: resp.getChatsList().map(mapChat),
                totalCount: resp.getTotalCount()
            };
        });
    }

    function getChatInfo(chatId) {
        var req = new (msgPb().GetChatInfoRequest)();
        req.setChatId(chatId);
        return c().authCall(messages().getChatInfo.bind(messages()), req).then(function (resp) {
            return {
                lastMessageId: resp.getLastMessageId(),
                firstUnreadMessageId: resp.getFirstUnreadMessageId(),
                title: resp.getTitle(),
                picture: resp.getPicture(),
                isGroupChat: resp.getIsGroupChat(),
                countUnread: resp.getCountUnread(),
                membersId: resp.getMembersIdList()
            };
        });
    }

    function getPersonChatId(userId) {
        var req = new (msgPb().GetPersonChatIdRequest)();
        req.setUserId(userId);
        return c().authCall(messages().getPersonChatId.bind(messages()), req).then(function (resp) {
            return { chatId: resp.getChatId() };
        });
    }

    function listMessages(chatId, fromMessageId, offsetBefore, offsetAfter) {
        var req = new (msgPb().ListMessagesRequest)();
        req.setChatId(chatId);
        req.setFromMessageId(fromMessageId || 0);
        req.setOffsetBefore(Math.min(offsetBefore || 30, 50));
        req.setOffsetAfter(Math.min(offsetAfter || 0, 50));
        return c().authCall(messages().listMessages.bind(messages()), req).then(function (resp) {
            return { messages: resp.getMessagesList().map(mapMessage) };
        });
    }

    function sendMessage(opts) {
        var req = new (msgPb().SendMessageRequest)();
        if (opts.chatId) req.setChatId(opts.chatId);
        else if (opts.userId) req.setUserId(opts.userId);

        var msg = new (msgPb().OutgoingMessage)();
        msg.setText(opts.text || '');
        if (opts.fileIds && opts.fileIds.length > 0) {
            msg.setFilesIdsList(opts.fileIds);
        }
        req.setMessage(msg);

        return c().authCall(messages().sendMessage.bind(messages()), req).then(function (resp) {
            var m = resp.getMessage();
            return { message: m ? mapMessage(m) : null };
        });
    }

    function markAsRead(messageIds) {
        var req = new (msgPb().MarkAsReadRequest)();
        req.setMessageIdsList(messageIds);
        return c().authCall(messages().markAsRead.bind(messages()), req);
    }

    function listChatAttachments(chatId, type, offset, size) {
        var req = new (msgPb().ListChatAttachmentsRequest)();
        req.setChatId(chatId);
        req.setAttachmentType(type || 0);
        req.setSortDescending(true);
        var pg = new (sharedPb().PageRequest)();
        pg.setOffset(offset || 0);
        pg.setSize(Math.min(size || 30, 50));
        req.setPagination(pg);
        return c().authCall(messages().listChatAttachments.bind(messages()), req).then(function (resp) {
            return {
                attachments: resp.getAttachmentsList().map(function (a) {
                    var att = a.getAttachment();
                    return {
                        messageId: a.getMessageId(),
                        attachmentId: a.getAttachmentId(),
                        attachment: att ? mapAttachment(att) : null,
                        sentAt: tsToMs(a.getSentAt()),
                        senderId: a.getSenderId()
                    };
                }),
                totalCount: resp.getTotalCount()
            };
        });
    }

    function searchUsers(query, offset, size) {
        var req = new (usrPb().SearchUsersRequest)();
        req.setQuery(query || '');
        var pg = new (sharedPb().PageRequest)();
        pg.setOffset(offset || 0);
        pg.setSize(Math.min(size || 20, 50));
        req.setPagination(pg);
        return c().authCall(users().searchUsers.bind(users()), req).then(function (resp) {
            return {
                users: resp.getUsersList().map(mapUser),
                totalCount: resp.getTotalCount()
            };
        });
    }

    function getUser(userId) {
        var req = new (usrPb().GetUserRequest)();
        req.setUserId(userId);
        return c().authCall(users().getUser.bind(users()), req).then(function (resp) {
            var u = resp.getUser();
            return { user: u ? mapUser(u) : null };
        });
    }

    function getUploadUrl(fileType) {
        var req = new (filePb().GetUploadUrlRequest)();
        req.setFileType(fileType || 0);
        return c().authCall(files().getUploadUrl.bind(files()), req).then(function (resp) {
            return { url: resp.getUrl(), fileId: resp.getFileId() };
        });
    }

    function getTempDownloadUrl(fileIds) {
        var req = new (filePb().GetTempDownloadUrlRequest)();
        req.setFileIdsList(fileIds);
        return c().authCall(files().getTempDownloadUrl.bind(files()), req).then(function (resp) {
            return {
                files: resp.getFileUrlsList().map(function (f) {
                    return { fileId: f.getFileId(), url: f.getUrl(), previewUrl: f.getPreviewUrl() };
                })
            };
        });
    }

    function listStickerPacks(offset, size) {
        var req = new (filePb().ListStickerPacksRequest)();
        var pg = new (sharedPb().PageRequest)();
        pg.setOffset(offset || 0);
        pg.setSize(size || 50);
        req.setPagination(pg);
        return c().authCall(files().listStickerPacks.bind(files()), req).then(function (resp) {
            return {
                packs: resp.getPacksList().map(function (p) {
                    return {
                        id: p.getId(), name: p.getName(), description: p.getDescription(),
                        coverStickerId: p.getCoverStickerId(), stickerCount: p.getStickerCount()
                    };
                }),
                totalCount: resp.getTotalCount()
            };
        });
    }

    function setOnlineStatus() {
        var req = new (onlPb().SetOnlineStatusRequest)();
        return c().authCall(onliner().setOnlineStatus.bind(onliner()), req);
    }

    function getOnlineStatus(userIds) {
        var req = new (onlPb().GetOnlineStatusRequest)();
        req.setUserIdsList(userIds);
        return c().authCall(onliner().getOnlineStatus.bind(onliner()), req).then(function (resp) {
            return {
                statuses: resp.getUsersStatusesList().map(function (s) {
                    return {
                        userId: s.getUserId(),
                        status: s.getStatus(),
                        lastSeen: tsToMs(s.getLastSeen())
                    };
                })
            };
        });
    }

    window.BF.api = {
        listChats: listChats,
        getChatInfo: getChatInfo,
        getPersonChatId: getPersonChatId,
        listMessages: listMessages,
        sendMessage: sendMessage,
        markAsRead: markAsRead,
        listChatAttachments: listChatAttachments,
        searchUsers: searchUsers,
        getUser: getUser,
        getUploadUrl: getUploadUrl,
        getTempDownloadUrl: getTempDownloadUrl,
        listStickerPacks: listStickerPacks,
        setOnlineStatus: setOnlineStatus,
        getOnlineStatus: getOnlineStatus,
        // Expose mapping helpers for realtime module
        _mapMessage: mapMessage,
        _mapUser: mapUser
    };
})();
