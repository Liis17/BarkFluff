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

    // Chat/Users/Messages встраивают в ответы уже готовые ссылки на Files (picture,
    // profilePicture, previewUrl…), в обход BF.files.getFileUrls/getUploadUrl — подменяем
    // хост на files_media_endpoint ноды тем же способом, здесь же, в момент маппинга.
    var mediaUrl = function (url) { return (BF.files && BF.files.mediaUrl) ? BF.files.mediaUrl(url) : url; };

    var sharedPb = function () { return window.proto.barkfluff.shared; };
    var msgPb = function () { return window.proto.barkfluff.messages; };
    var usrPb = function () { return window.proto.barkfluff.users; };
    var filePb = function () { return window.proto.barkfluff.files; };
    var onlPb = function () { return window.proto.barkfluff.onliner; };
    var identPb = function () { return window.proto.barkfluff.identity; };

    // --- Helpers to convert protobuf → plain JS ---

    function tsToMs(ts) {
        if (!ts) return null;
        return ts.toDate().getTime();
    }

    function mapForwarded(fm) {
        if (!fm) return null;
        return {
            authorName: fm.getAuthorName ? fm.getAuthorName() : '',
            originalMessageId: fm.getOriginalMessageId ? fm.getOriginalMessageId() : 0,
            text: fm.getText ? fm.getText() : '',
            attachments: fm.getAttachmentsList ? fm.getAttachmentsList().map(mapAttachment) : [],
            originalChatId: fm.getOriginalChatId ? fm.getOriginalChatId() : '',
            originalSenderId: fm.getOriginalSenderId ? fm.getOriginalSenderId() : 0,
            originalSentAt: fm.getOriginalSentAt ? tsToMs(fm.getOriginalSentAt()) : null,
            order: fm.getOrder ? fm.getOrder() : 0
        };
    }

    // Цитата ответа. В отличие от пересылки это не снапшот: сервер резолвит её из оригинала
    // на каждой выдаче, поэтому текст всегда актуальный, а у удалённого он пуст.
    function mapReplyTo(r) {
        if (!r) return null;
        return {
            messageId: r.getMessageId ? r.getMessageId() : 0,
            senderId: r.getSenderId ? r.getSenderId() : 0,
            senderName: r.getSenderName ? r.getSenderName() : '',
            textPreview: r.getTextPreview ? r.getTextPreview() : '',
            firstAttachmentType: r.getFirstAttachmentType ? enumName(r.getFirstAttachmentType()) : null,
            isDeleted: r.getIsDeleted ? r.getIsDeleted() : false
        };
    }

    function mapAttachment(a) {
        return {
            id: a.getId(),
            type: enumName(a.getType()),
            fileId: a.getFileId(),
            previewUrl: mediaUrl(a.getPreviewUrl()),
            attachmentSize: a.getAttachmentSize(),
            previewFileId: a.getPreviewFileId(),
            fileName: a.getFileName(),
            imageWidth: a.getImageWidth ? a.getImageWidth() : 0,
            imageHeight: a.getImageHeight ? a.getImageHeight() : 0,
            forwardedMessage: a.getForwardedMessage ? mapForwarded(a.getForwardedMessage()) : null
        };
    }

    function mapMessage(m) {
        var content = m.getContent();
        return {
            id: m.getId(),
            senderId: m.getSenderId(),
            readBy: m.getReadByList(),
            sentAt: tsToMs(m.getSentAt()),
            // Тип сообщения — MessageContentType (0=UNKNOWN, 1=GENERIC, 2=SYSTEM).
            // Хранится числом; не пропускаем через enumName, т.к. enumName смотрит
            // в MessageAttachmentType, где 2=VIDEO — это поломает определение system.
            type: m.getType(),
            forwardedMessageId: m.getForwardedMessageId ? m.getForwardedMessageId() : 0,
            replyTo: m.getReplyTo ? mapReplyTo(m.getReplyTo()) : null,
            isEdited: m.getIsEdited ? m.getIsEdited() : false,
            editedAt: m.getEditedAt ? tsToMs(m.getEditedAt()) : null,
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
            picture: mediaUrl(ch.getPicture()),
            isGroupChat: ch.getIsGroupChat(),
            lastMessage: lm ? mapMessage(lm) : null,
            members: ch.getMembersList().map(function (m) { return { userId: m.getUserId() }; }),
            countUnread: ch.getCountUnread(),
            firstUnreadMessageId: ch.getFirstUnreadMessageId(),
            // Приватные чаты (ChatType: 0=REGULAR, 1=PRIVATE, 2=SECRET)
            chatType: ch.getChatType ? ch.getChatType() : 0,
            kdfSalt: ch.getKdfSalt_asU8 ? ch.getKdfSalt_asU8() : new Uint8Array(0),
            passphraseVerifier: ch.getPassphraseVerifier_asU8 ? ch.getPassphraseVerifier_asU8() : new Uint8Array(0),
            lastActivityAt: ch.getLastActivityAt ? tsToMs(ch.getLastActivityAt()) : null,
            // PrivateChatInviteState: 0=PENDING, 1=ACCEPTED, 2=REJECTED
            privateInviteState: ch.getPrivateInviteState ? ch.getPrivateInviteState() : 0,
            privateInviterUserId: ch.getPrivateInviterUserId ? ch.getPrivateInviterUserId() : 0,
            hasDraft: ch.getHasDraft ? ch.getHasDraft() : false
        };
    }

    // Шифрованное сообщение приватного чата — сервер отдаёт только шифротекст,
    // расшифровка на клиенте (BF.privateChat).
    function mapEncryptedMessage(m) {
        return {
            id: m.getId(),
            chatId: m.getChatId(),
            senderId: m.getSenderId(),
            sentAt: tsToMs(m.getSentAt()),
            ciphertext: m.getCiphertext_asU8(),
            nonce: m.getNonce_asU8(),
            associatedData: m.getAssociatedData_asU8(),
            isEdited: m.getIsEdited(),
            editedAt: tsToMs(m.getEditedAt()),
            isDeleted: m.getIsDeleted()
        };
    }

    function mapUser(u) {
        return {
            id: u.getId(),
            firstName: u.getFirstName(),
            lastName: u.getLastName(),
            username: u.getUsername(),
            profilePicture: mediaUrl(u.getProfilePicture()),
            profilePicturePreview: mediaUrl(u.getProfilePicturePreview()),
            profilePosterFileId: u.getProfilePosterFileId ? u.getProfilePosterFileId() : '',
            isBot: u.getIsBot ? u.getIsBot() : false,
            bio: u.getBio(),
            registrationDate: tsToMs(u.getRegistrationDate()),
            badges: u.getBadgesList().map(function (b) {
                var badge = b.getBadge();
                return {
                    name: badge ? badge.getName() : '',
                    imageUrl: badge ? mediaUrl(badge.getImageUrl()) : '',
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
                picture: mediaUrl(resp.getPicture()),
                isGroupChat: resp.getIsGroupChat(),
                countUnread: resp.getCountUnread(),
                membersId: resp.getMembersIdList()
            };
        });
    }

    function mapChatDraft(draft) {
        if (!draft) return null;
        return {
            text: draft.getText(),
            replyToMessageId: draft.getReplyToMessageId(),
            revision: draft.getRevision(),
            updatedAt: draft.getUpdatedAt ? tsToMs(draft.getUpdatedAt()) : null
        };
    }

    function getChatDraft(chatId) {
        var req = new (msgPb().GetChatDraftRequest)();
        req.setChatId(chatId);
        return c().authCall(messages().getChatDraft.bind(messages()), req).then(function (resp) {
            return { draft: mapChatDraft(resp.getDraft()) };
        });
    }

    function upsertChatDraft(chatId, text, replyToMessageId) {
        var req = new (msgPb().UpsertChatDraftRequest)();
        req.setChatId(chatId);
        req.setText(text || '');
        req.setReplyToMessageId(replyToMessageId || 0);
        return c().authCall(messages().upsertChatDraft.bind(messages()), req).then(function (resp) {
            return { draft: mapChatDraft(resp.getDraft()) };
        });
    }

    function deleteChatDraft(chatId, revision) {
        var req = new (msgPb().DeleteChatDraftRequest)();
        req.setChatId(chatId);
        req.setExpectedRevision(revision || '');
        return c().authCall(messages().deleteChatDraft.bind(messages()), req).then(function (resp) {
            return { deleted: resp.getDeleted() };
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
        // Ответ и пересылка — разные поля. Раньше оба ехали forwarded_message_id, и клиент
        // потом гадал, что из этого что.
        if (opts.replyToMessageId && msg.setReplyToMessageId) {
            msg.setReplyToMessageId(opts.replyToMessageId);
        }
        if (opts.forwardedMessageIds && opts.forwardedMessageIds.length > 0 && msg.setForwardedMessageIdsList) {
            msg.setForwardedMessageIdsList(opts.forwardedMessageIds);
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

    function editMessage(messageId, text, fileIds) {
        var req = new (msgPb().EditMessageRequest)();
        req.setMessageId(messageId);
        req.setText(text || '');
        if (fileIds && fileIds.length > 0) req.setFilesIdsList(fileIds);
        return c().authCall(messages().editMessage.bind(messages()), req).then(function (resp) {
            var m = resp.getMessage();
            return { message: m ? mapMessage(m) : null };
        });
    }

    function deleteMessage(messageId) {
        var req = new (msgPb().DeleteMessageRequest)();
        req.setMessageId(messageId);
        return c().authCall(messages().deleteMessage.bind(messages()), req);
    }

    function listChatAttachments(chatId, type, offset, size, fileNameQuery) {
        var req = new (msgPb().ListChatAttachmentsRequest)();
        req.setChatId(chatId);
        req.setAttachmentType(type || 0);
        req.setSortDescending(true);
        req.setFileNameQuery(fileNameQuery || '');
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

    function listChatMembers(chatId, offset, size) {
        var req = new (msgPb().ListChatMembersRequest)();
        req.setChatId(chatId);
        var pg = new (sharedPb().PageRequest)();
        pg.setOffset(offset || 0);
        pg.setSize(Math.min(size || 50, 50));
        req.setPagination(pg);
        return c().authCall(messages().listChatMembers.bind(messages()), req).then(function (resp) {
            return {
                members: resp.getChatMembersList().map(function (m) {
                    var gi = m.getGeneralInfo();
                    return {
                        userId: gi ? gi.getUserId() : 0,
                        firstName: m.getFirstName(),
                        lastName: m.getLastName(),
                        joinedAt: gi && gi.getJoinedAt ? tsToMs(gi.getJoinedAt()) : null
                    };
                }),
                totalCount: resp.getTotalCount()
            };
        });
    }

    function addUser(chatId, userId) {
        var req = new (msgPb().AddUserRequest)();
        req.setChatId(chatId);
        req.setUserId(userId);
        return c().authCall(messages().addUser.bind(messages()), req);
    }

    function kickUser(chatId, userId) {
        var req = new (msgPb().KickUserRequest)();
        req.setChatId(chatId);
        req.setUserId(userId);
        return c().authCall(messages().kickUser.bind(messages()), req);
    }

    function updateGroupChat(chatId, title, pictureFileId) {
        var req = new (msgPb().UpdateGroupChatRequest)();
        req.setChatId(chatId);
        req.setTitle(title || '');
        req.setPictureFileId(pictureFileId || '');
        return c().authCall(messages().updateGroupChat.bind(messages()), req).then(function (resp) {
            var ch = resp.getChat();
            return { chat: ch ? { id: ch.getId(), title: ch.getTitle(), picture: mediaUrl(ch.getPicture()) } : null };
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

    function checkFileHash(fileHash) {
        var req = new (filePb().CheckFileHashRequest)();
        req.setFileHash(fileHash);
        return c().authCall(files().checkFileHash.bind(files()), req).then(function (resp) {
            return { fileId: resp.getFileId() };
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

    function getStickerPack(packId) {
        var req = new (filePb().GetStickerPackRequest)();
        req.setPackId(packId);
        return c().authCall(files().getStickerPack.bind(files()), req).then(function (resp) {
            var pack = resp.getPack();
            return {
                pack: pack ? { id: pack.getId(), name: pack.getName(), description: pack.getDescription() } : null,
                stickers: resp.getStickersList().map(function (s) {
                    return {
                        id: s.getId(),
                        fileId: s.getFileId(),
                        previewFileId: s.getPreviewFileId(),
                        emoji: s.getEmoji()
                    };
                })
            };
        });
    }

    // --- Users API ---

    function changeName(firstName, lastName) {
        var req = new (usrPb().ChangeNameRequest)();
        req.setFirstName(firstName || '');
        req.setLastName(lastName || '');
        return c().authCall(users().changeName.bind(users()), req);
    }

    function changeUsername(username) {
        var req = new (usrPb().ChangeUsernameRequest)();
        req.setUsername(username);
        return c().authCall(users().changeUsername.bind(users()), req);
    }

    function changeBio(bio) {
        var req = new (usrPb().ChangeBioRequest)();
        req.setBio(bio || '');
        return c().authCall(users().changeBio.bind(users()), req);
    }

    function setProfilePicture(fileId) {
        var req = new (usrPb().SetProfilePictureRequest)();
        req.setFileId(fileId);
        return c().authCall(users().setProfilePicture.bind(users()), req);
    }

    function checkExistUsername(username) {
        var req = new (usrPb().CheckExistUsernameRequest)();
        req.setUsername(username);
        return c().authCall(users().checkExistUsername.bind(users()), req).then(function (resp) {
            return { exist: resp.getExist() };
        });
    }

    // --- Identity API ---

    function getActiveSessions() {
        var req = new (identPb().GetActiveSessionsRequest)();
        return c().authCall(identity().getActiveSessions.bind(identity()), req).then(function (resp) {
            return {
                sessions: resp.getSessionsList().map(function (s) {
                    return {
                        id: s.getId(),
                        deviceId: s.getDeviceId(),
                        originalName: s.getOriginalName(),
                        customName: s.getCustomName(),
                        appName: s.getAppName(),
                        operationSystem: s.getOperationSystem(),
                        location: s.getLocation(),
                        createdAt: tsToMs(s.getCreatedAt()),
                        expirationAt: tsToMs(s.getExpirationAt())
                    };
                })
            };
        });
    }

    function removeActiveSession(deviceId) {
        var req = new (identPb().RemoveActiveSessionRequest)();
        req.setDeviceId(deviceId);
        return c().authCall(identity().removeActiveSession.bind(identity()), req);
    }

    function listOtpVerification() {
        var req = new (identPb().ListOtpVerificationRequest)();
        return c().authCall(identity().listOtpVerification.bind(identity()), req).then(function (resp) {
            return { authenticatorEnabled: resp.getAuthenticatorEnabled(), emailEnabled: resp.getEmailEnabled() };
        });
    }

    function enableOtpVerification(otpType) {
        var req = new (identPb().EnableOtpVerificationRequest)();
        req.setOtpType(otpType);
        return c().authCall(identity().enableOtpVerification.bind(identity()), req).then(function (resp) {
            return { otpQr: resp.getOtpQr(), otpCode: resp.getOtpCode() };
        });
    }

    function confirmOtpVerification(otpCode) {
        var req = new (identPb().ConfirmOtpVerificationRequest)();
        req.setOtpCode(otpCode);
        return c().authCall(identity().confirmOtpVerification.bind(identity()), req);
    }

    function disableOtpVerification(otpType, otpCode) {
        var req = new (identPb().DisableOtpVerificationRequest)();
        req.setOtpType(otpType);
        if (otpCode) req.setOtpCode(otpCode);
        return c().authCall(identity().disableOtpVerification.bind(identity()), req);
    }

    function setPassword(password, oldPassword) {
        var req = new (identPb().SetPasswordRequest)();
        req.setPassword(password);
        if (oldPassword) req.setOldPassword(oldPassword);
        return c().authCall(identity().setPassword.bind(identity()), req);
    }

    // --- User devices / notifications (UsersApi) ---

    function renameDevice(deviceId, customName) {
        var req = new (usrPb().RenameDeviceRequest)();
        req.setDeviceId(deviceId);
        req.setCustomName(customName || '');
        return c().authCall(users().renameDevice.bind(users()), req);
    }

    function setNotificationsEnabled(enabled) {
        var req = new (usrPb().SetNotificationsEnabledRequest)();
        req.setEnabled(!!enabled);
        return c().authCall(users().setNotificationsEnabled.bind(users()), req);
    }

    function setFirebaseToken(token, pushPlatform) {
        var req = new (usrPb().SetFirebaseTokenRequest)();
        req.setFirebaseToken(token);
        req.setPushPlatform(pushPlatform || 2);
        return c().authCall(users().setFirebaseToken.bind(users()), req);
    }

    function clearFirebaseToken() {
        var req = new (usrPb().ClearFirebaseTokenRequest)();
        return c().authCall(users().clearFirebaseToken.bind(users()), req);
    }

    // --- Privacy (UsersApi) ---

    function mapPrivacySettings(s) {
        if (!s) return null;
        return {
            profileVisibleOnSite: s.getProfileVisibleOnSite(),
            avatarVisibility: s.getAvatarVisibility(),
            bioVisibility: s.getBioVisibility(),
            emailVisibility: s.getEmailVisibility(),
            searchVisible: s.getSearchVisible(),
            onlineVisibility: s.getOnlineVisibility()
        };
    }

    function getPrivacySettings() {
        var req = new (usrPb().GetPrivacySettingsRequest)();
        return c().authCall(users().getPrivacySettings.bind(users()), req).then(function (resp) {
            return { settings: mapPrivacySettings(resp.getSettings()) };
        });
    }

    function updatePrivacySettings(settings) {
        var req = new (usrPb().UpdatePrivacySettingsRequest)();
        var s = new (usrPb().PrivacySettings)();
        s.setProfileVisibleOnSite(!!settings.profileVisibleOnSite);
        s.setAvatarVisibility(settings.avatarVisibility || 0);
        s.setBioVisibility(settings.bioVisibility || 0);
        s.setEmailVisibility(settings.emailVisibility || 0);
        s.setSearchVisible(!!settings.searchVisible);
        s.setOnlineVisibility(settings.onlineVisibility || 0);
        req.setSettings(s);
        return c().authCall(users().updatePrivacySettings.bind(users()), req);
    }

    // --- Personalization (UsersApi) ---

    function mapPersonalization(p) {
        if (!p) return null;
        return {
            profilePosterFileId: p.getProfilePosterFileId(),
            chatBackgroundFileIds: p.getChatBackgroundFileIdsList()
        };
    }

    function getPersonalization() {
        var req = new (usrPb().GetPersonalizationRequest)();
        return c().authCall(users().getPersonalization.bind(users()), req).then(function (resp) {
            return { personalization: mapPersonalization(resp.getPersonalization()) };
        });
    }

    function updatePersonalization(personalization) {
        var req = new (usrPb().UpdatePersonalizationRequest)();
        var p = new (usrPb().UserPersonalizationData)();
        p.setProfilePosterFileId(personalization.profilePosterFileId || '');
        p.setChatBackgroundFileIdsList(personalization.chatBackgroundFileIds || []);
        req.setPersonalization(p);
        return c().authCall(users().updatePersonalization.bind(users()), req);
    }

    function setProfilePoster(fileId) {
        var req = new (usrPb().SetProfilePosterRequest)();
        req.setProfilePosterFileId(fileId || '');
        return c().authCall(users().setProfilePoster.bind(users()), req);
    }

    // --- Synced chat backgrounds (UsersApi) ---

    function mapUserSettings(settings) {
        if (!settings) return { globalChatBackgroundFileId: '', chatBackgrounds: [] };
        return {
            globalChatBackgroundFileId: settings.getGlobalChatBackgroundFileId(),
            chatBackgrounds: settings.getChatBackgroundsList().map(function (item) {
                return {
                    chatId: item.getChatId(),
                    chatBackgroundFileId: item.getChatBackgroundFileId()
                };
            })
        };
    }

    function getUserSettings() {
        var req = new (usrPb().GetUserSettingsRequest)();
        return c().authCall(users().getUserSettings.bind(users()), req).then(function (resp) {
            return { settings: mapUserSettings(resp.getSettings()) };
        });
    }

    function setGlobalChatBackground(fileId) {
        var req = new (usrPb().SetGlobalChatBackgroundRequest)();
        req.setChatBackgroundFileId(fileId || '');
        return c().authCall(users().setGlobalChatBackground.bind(users()), req);
    }

    function setChatBackground(chatId, fileId) {
        var req = new (usrPb().SetChatBackgroundRequest)();
        req.setChatId(chatId);
        req.setChatBackgroundFileId(fileId || '');
        return c().authCall(users().setChatBackground.bind(users()), req);
    }

    // --- Chat Folders (UsersApi) ---

    function mapChatFolder(f) {
        return {
            folderId: f.getFolderId(),
            folderName: f.getFolderName(),
            folderIcon: f.getFolderIcon(),
            chatList: f.getChatListList(),
            sortOrder: f.getSortOrder()
        };
    }

    function getChatFolders() {
        var req = new (usrPb().GetChatFoldersRequest)();
        return c().authCall(users().getChatFolders.bind(users()), req).then(function (resp) {
            return { folders: resp.getFoldersList().map(mapChatFolder) };
        });
    }

    function createChatFolder(folderName, folderIcon) {
        var req = new (usrPb().CreateChatFolderRequest)();
        req.setFolderName(folderName || '');
        req.setFolderIcon(folderIcon || '');
        return c().authCall(users().createChatFolder.bind(users()), req).then(function (resp) {
            var f = resp.getFolder();
            return { folder: f ? mapChatFolder(f) : null };
        });
    }

    function updateChatFolder(folderId, opts) {
        opts = opts || {};
        var req = new (usrPb().UpdateChatFolderRequest)();
        req.setFolderId(folderId);
        if (opts.folderName !== undefined) req.setFolderName(opts.folderName);
        if (opts.folderIcon !== undefined) req.setFolderIcon(opts.folderIcon);
        if (opts.hasChatListUpdate) {
            req.setHasChatListUpdate(true);
            req.setChatListList(opts.chatList || []);
        }
        return c().authCall(users().updateChatFolder.bind(users()), req).then(function (resp) {
            var f = resp.getFolder();
            return { folder: f ? mapChatFolder(f) : null };
        });
    }

    function deleteChatFolder(folderId) {
        var req = new (usrPb().DeleteChatFolderRequest)();
        req.setFolderId(folderId);
        return c().authCall(users().deleteChatFolder.bind(users()), req);
    }

    function addChatToFolder(folderId, chatId) {
        var req = new (usrPb().AddChatToFolderRequest)();
        req.setFolderId(folderId);
        req.setChatId(chatId);
        return c().authCall(users().addChatToFolder.bind(users()), req).then(function (resp) {
            var f = resp.getFolder();
            return { folder: f ? mapChatFolder(f) : null };
        });
    }

    function removeChatFromFolder(folderId, chatId) {
        var req = new (usrPb().RemoveChatFromFolderRequest)();
        req.setFolderId(folderId);
        req.setChatId(chatId);
        return c().authCall(users().removeChatFromFolder.bind(users()), req).then(function (resp) {
            var f = resp.getFolder();
            return { folder: f ? mapChatFolder(f) : null };
        });
    }

    function reorderChatFolders(orders) {
        var req = new (usrPb().ReorderChatFoldersRequest)();
        var list = (orders || []).map(function (o) {
            var item = new (usrPb().ChatFolderOrder)();
            item.setFolderId(o.folderId);
            item.setSortOrder(o.sortOrder);
            return item;
        });
        req.setOrdersList(list);
        return c().authCall(users().reorderChatFolders.bind(users()), req);
    }

    // --- Pinned Messages (MessagesApi) ---

    function mapPinnedInfo(p) {
        var m = p.getMessage();
        return {
            message: m ? mapMessage(m) : null,
            pinnerUserId: p.getPinnerUserId(),
            pinnedAt: tsToMs(p.getPinnedAt())
        };
    }

    function pinMessage(chatId, messageId) {
        var req = new (msgPb().PinMessageRequest)();
        req.setChatId(chatId);
        req.setMessageId(messageId);
        return c().authCall(messages().pinMessage.bind(messages()), req).then(function (resp) {
            var p = resp.getPinned();
            return { pinned: p ? mapPinnedInfo(p) : null };
        });
    }

    function unpinMessage(chatId, messageId) {
        var req = new (msgPb().UnpinMessageRequest)();
        req.setChatId(chatId);
        req.setMessageId(messageId);
        return c().authCall(messages().unpinMessage.bind(messages()), req);
    }

    function listPinnedMessages(chatId, offset, size) {
        var req = new (msgPb().ListPinnedMessagesRequest)();
        req.setChatId(chatId);
        var pg = new (sharedPb().PageRequest)();
        pg.setOffset(offset || 0);
        pg.setSize(Math.min(size || 50, 50));
        req.setPagination(pg);
        return c().authCall(messages().listPinnedMessages.bind(messages()), req).then(function (resp) {
            return {
                pinned: resp.getPinnedList().map(mapPinnedInfo),
                totalCount: resp.getTotalCount()
            };
        });
    }

    function unpinAll(chatId) {
        var req = new (msgPb().UnpinAllRequest)();
        req.setChatId(chatId);
        return c().authCall(messages().unpinAll.bind(messages()), req).then(function (resp) {
            return { unpinnedCount: resp.getUnpinnedCount() };
        });
    }

    function createGroupChat(userIds, title, pictureFileId) {
        var req = new (msgPb().CreateGroupChatRequest)();
        req.setUserIdsList(userIds);
        req.setTitle(title || '');
        req.setPictureFileId(pictureFileId || '');
        return c().authCall(messages().createGroupChat.bind(messages()), req).then(function (resp) {
            var ch = resp.getCreatedChat();
            return { chat: ch ? mapChat(ch) : null };
        });
    }

    // --- Приватные чаты (E2E через passphrase) ---

    function createPrivateChat(peerUserId, kdfSalt, passphraseVerifier) {
        var req = new (msgPb().CreatePrivateChatRequest)();
        req.setPeerUserId(peerUserId);
        req.setKdfSalt(kdfSalt);
        req.setPassphraseVerifier(passphraseVerifier);
        return c().authCall(messages().createPrivateChat.bind(messages()), req).then(function (resp) {
            var ch = resp.getChat();
            return { chat: ch ? mapChat(ch) : null, created: resp.getCreated() };
        });
    }

    function acceptPrivateChat(chatId) {
        var req = new (msgPb().AcceptPrivateChatRequest)();
        req.setChatId(chatId);
        return c().authCall(messages().acceptPrivateChat.bind(messages()), req).then(function (resp) {
            var ch = resp.getChat();
            return { chat: ch ? mapChat(ch) : null };
        });
    }

    function rejectPrivateChat(chatId) {
        var req = new (msgPb().RejectPrivateChatRequest)();
        req.setChatId(chatId);
        return c().authCall(messages().rejectPrivateChat.bind(messages()), req);
    }

    function listPrivateMessages(chatId, fromMessageId, offsetBefore, offsetAfter) {
        var req = new (msgPb().ListPrivateMessagesRequest)();
        req.setChatId(chatId);
        req.setFromMessageId(fromMessageId || 0);
        req.setOffsetBefore(Math.min(offsetBefore || 30, 50));
        req.setOffsetAfter(Math.min(offsetAfter || 0, 50));
        return c().authCall(messages().listPrivateMessages.bind(messages()), req).then(function (resp) {
            return { messages: resp.getMessagesList().map(mapEncryptedMessage) };
        });
    }

    function sendPrivateMessage(chatId, ciphertext, nonce, associatedData) {
        var req = new (msgPb().SendPrivateMessageRequest)();
        req.setChatId(chatId);
        req.setCiphertext(ciphertext);
        req.setNonce(nonce);
        req.setAssociatedData(associatedData);
        return c().authCall(messages().sendPrivateMessage.bind(messages()), req).then(function (resp) {
            var m = resp.getMessage();
            return { message: m ? mapEncryptedMessage(m) : null };
        });
    }

    function markPrivateMessagesAsRead(chatId, lastReadMessageId) {
        var req = new (msgPb().MarkPrivateMessagesAsReadRequest)();
        req.setChatId(chatId);
        req.setLastReadMessageId(lastReadMessageId);
        return c().authCall(messages().markPrivateMessagesAsRead.bind(messages()), req);
    }

    function setOnlineStatus() {
        var req = new (onlPb().SetOnlineStatusRequest)();
        return c().authCall(onliner().setOnlineStatus.bind(onliner()), req);
    }

    function setTypingStatus(chatId, typing) {
        var req = new (onlPb().SetTypingStatusRequest)();
        req.setChatId(chatId);
        req.setAction(typing ? 1 : 2);
        return c().authCall(onliner().setTypingStatus.bind(onliner()), req);
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
        getChatDraft: getChatDraft,
        upsertChatDraft: upsertChatDraft,
        deleteChatDraft: deleteChatDraft,
        getPersonChatId: getPersonChatId,
        listMessages: listMessages,
        sendMessage: sendMessage,
        markAsRead: markAsRead,
        editMessage: editMessage,
        deleteMessage: deleteMessage,
        listChatAttachments: listChatAttachments,
        listChatMembers: listChatMembers,
        addUser: addUser,
        kickUser: kickUser,
        updateGroupChat: updateGroupChat,
        searchUsers: searchUsers,
        getUser: getUser,
        getUploadUrl: getUploadUrl,
        checkFileHash: checkFileHash,
        getTempDownloadUrl: getTempDownloadUrl,
        listStickerPacks: listStickerPacks,
        getStickerPack: getStickerPack,
        setOnlineStatus: setOnlineStatus,
        getOnlineStatus: getOnlineStatus,
        setTypingStatus: setTypingStatus,
        // Users
        changeName: changeName,
        changeUsername: changeUsername,
        changeBio: changeBio,
        setProfilePicture: setProfilePicture,
        checkExistUsername: checkExistUsername,
        // Identity
        getActiveSessions: getActiveSessions,
        removeActiveSession: removeActiveSession,
        listOtpVerification: listOtpVerification,
        enableOtpVerification: enableOtpVerification,
        confirmOtpVerification: confirmOtpVerification,
        disableOtpVerification: disableOtpVerification,
        setPassword: setPassword,
        // User devices / notifications / privacy / personalization
        renameDevice: renameDevice,
        setNotificationsEnabled: setNotificationsEnabled,
        setFirebaseToken: setFirebaseToken,
        clearFirebaseToken: clearFirebaseToken,
        getPrivacySettings: getPrivacySettings,
        updatePrivacySettings: updatePrivacySettings,
        getPersonalization: getPersonalization,
        updatePersonalization: updatePersonalization,
        setProfilePoster: setProfilePoster,
        getUserSettings: getUserSettings,
        setGlobalChatBackground: setGlobalChatBackground,
        setChatBackground: setChatBackground,
        // Chat Folders
        getChatFolders: getChatFolders,
        createChatFolder: createChatFolder,
        updateChatFolder: updateChatFolder,
        deleteChatFolder: deleteChatFolder,
        addChatToFolder: addChatToFolder,
        removeChatFromFolder: removeChatFromFolder,
        reorderChatFolders: reorderChatFolders,
        // Pinned Messages
        pinMessage: pinMessage,
        unpinMessage: unpinMessage,
        listPinnedMessages: listPinnedMessages,
        unpinAll: unpinAll,
        createGroupChat: createGroupChat,
        // Private chats
        createPrivateChat: createPrivateChat,
        acceptPrivateChat: acceptPrivateChat,
        rejectPrivateChat: rejectPrivateChat,
        listPrivateMessages: listPrivateMessages,
        sendPrivateMessage: sendPrivateMessage,
        markPrivateMessagesAsRead: markPrivateMessagesAsRead,
        // Expose mapping helpers for realtime module
        _mapMessage: mapMessage,
        _mapUser: mapUser,
        _mapEncryptedMessage: mapEncryptedMessage
    };
})();
