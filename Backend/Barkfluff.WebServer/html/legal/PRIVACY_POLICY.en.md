# BarkFluff Privacy Policy

**Effective from:** January 24, 2026  
**Last updated:** November 19, 2026

> This translation is provided for convenience. In case of any discrepancy, the Russian version prevails.

## 1. Who is responsible for your data

BarkFluff is a federated network of independent servers ("nodes"). Each node is deployed and operated by its administrator; nodes talk to each other to connect users of different nodes.

For the public barkfluff.com node, the controller is an individual operator: BarkFluff's lead developer, based in Armenia. The node's main server is in Germany. BarkFluff is an open-source project and is not registered as a legal entity. The operator's contact addresses are listed at the end of this document.

The BarkFluff developers publish the software and the protocol specification. They do **not** host other people's nodes, do **not** have access to their data, keys, traffic or backups, **cannot** disclose, modify or delete data on someone else's node, and are **not** the controller of your data.

This document describes the behaviour of the software in its default configuration and applies to the **barkfluff.com** node. The administrator of any other node publishes their own version and may change the configuration or the source code — in that case their terms apply. If your account was not created on barkfluff.com, contact your own node's administrator.

## 2. What the node administrator can technically see

- Regular private and group messages are stored on the node in plaintext. The node administrator has technical access to the database, file storage and logs and **can read their content**.
- Private chats store only ciphertext on the server; the key is derived on your device from the passphrase and is never sent to the node.
- Secret chats are only relayed by the node between devices and are not kept as history.
- If you are not prepared to trust a particular node's administrator, use private or secret chats, or run your own node.

## 3. What data is processed

### 3.1 Account and profile

| Data | Where used | Storage |
| --- | --- | --- |
| Email | Registration, login, confirmations, access recovery, notifications, including account blocking and deletion emails | Users / Identity; restriction reason and date are kept with the account |
| Username | Login, search, public profile page, display in chats | Users |
| Password hash | Password verification; the plaintext password is not stored | Identity |
| First name, last name, bio | User profile and public profile data | Users |
| Avatar and profile poster | Profile and public page styling | Files / Minio + Users metadata |
| Privacy and personalization settings | Display of profile data, client appearance, permission for federated chats | Users |

### 3.2 Devices and sessions

| Data | Where used | Storage |
| --- | --- | --- |
| Device ID | Refresh token binding, active sessions list, session revocation | Identity.RefreshTokens, Users.UserDevices |
| Device name | Device display and renaming | Users.UserDevices |
| OS, app name | Client compatibility and device list | Users.UserDevices |
| Location | Device display in the sessions list | Users.UserDevices |
| Firebase token | Push notifications via Firebase Cloud Messaging | Users.UserDevices |
| Refresh token | Session renewal and sign-out from device | Identity.RefreshTokens |

The IP address is passed in gRPC service metadata headers and is used to process current requests. This data does not leave your node and is not sent to other nodes.

### 3.3 Messages, chats and files

| Data | Where used | Storage |
| --- | --- | --- |
| Regular messages | Private and group chats, sync, read receipts, editing and deletion | Messages / PostgreSQL |
| Private encrypted chats | 1-to-1 chats with client-side encryption, within a single node only | Messages stores ciphertext and chat metadata |
| Secret chats | 1-to-1 chats between specific devices, within a single node only | The node relays the envelope and buffers it temporarily |
| Attachments and previews | Transfer of files, images, video, documents, audio and avatars | Files / Minio + PostgreSQL metadata |
| Read statuses | Display of read messages | Messages |

### 3.4 Online statuses and updates

Onliner stores and serves the online status and last activity time. Updates delivers realtime events to clients via gRPC streaming.

### 3.5 Website and support

WebServer serves the public site, profile pages, legal documents and the public profile REST API. On barkfluff.com, website support messages are held in WebServer process memory and forwarded to the support Telegram chat. The operator reports that the Telegram support chat is automatically cleared once a week; this is periodic chat cleanup, not a per-message retention period. The local web support chat history is also held in WebServer process memory, and no cleanup schedule for it has been confirmed.

Requests sent to the operator's service email addresses may be kept without an established maximum retention period. The operator reports that service-generated emails, including codes, are not retained in the operator's own mailbox; this does not describe processing by Identity or storage in the recipient's mailbox.

On clicking "Message in browser" on a public profile page, WebServer creates a short-lived cookie `bf_open_chat` so the web client opens a chat with the selected user.

### 3.6 Calls, bots and push notifications

Calls stores technical call-session details in the node database, including participants, call type, status, and start, answer and end times. Call media is relayed through LiveKit. The public-node operator reports that the server does not record call media. This statement does not cover call metadata, data on participants' devices or other nodes; LiveKit hosting and data-retention details have not been confirmed.

If you send a message or file to a bot, it is delivered to that bot through the API. The bot operator may process or store what it receives under its own rules. A device push token is stored in Users; backend code sends notifications through Firebase Cloud Messaging when enabled. Whether APNs or other platform delivery services are used has not been confirmed.

## 4. Federated data exchange

Federation is disabled by default (`Federation:Enabled = false`) — the node administrator turns it on. When it is enabled and you chat with a user of another node, some data crosses your node's boundary.

| What leaves for another node | When | How |
| --- | --- | --- |
| Identifier, username, name and profile avatar | When a user of another node finds you or opens your profile card | Inter-node profile request, subject to your privacy settings |
| Message text, edits, deletions, read receipts | 1-to-1 conversation with a user of another node | Signed events; delivery is handled by a queue with retries |
| Files and attachments | When your correspondent opens a file | Streamed on request from their node; no copy is made in advance |
| Online status and last activity time | While a federated chat is active | Live inter-node stream; your privacy settings apply |
| "Typing…" | While you are typing a message | A one-off notification that is not stored |

What matters here:

- Federated chats are 1-to-1 only. Group chats between nodes are not supported.
- **Federated messages are not end-to-end encrypted.** Their content is available both to your node and to your correspondent's node. Private and secret chats work within a single node only.
- The receiving node becomes an **independent controller** of its copy. Neither your node nor the developers control how long or in what way it keeps that data. The model is the same as with email.
- For a federated message, BarkFluff sends a deletion event to participating remote nodes, which validate and apply it after receipt. Processing is not synchronous and depends on event delivery. This does not remove files already downloaded by correspondents or screenshots. Deleting an account on barkfluff.com does not send an account-deletion event or erase data already delivered to other nodes.
- You can refuse incoming federated private chats in your privacy settings — an attempt to start a chat with you from another node will then be rejected.
- Your node's administrator can block an entire node — data exchange with it will stop.

**Technical guarantees and their limits.** Inter-node events are signed with Ed25519, connections use TLS with server key fingerprint verification, and requests are signed within a limited time window. This proves **which node** the data came from and that it was not tampered with in transit, but gives no guarantee whatsoever about how that node's administrator handles the received copy.

## 5. What the data is used for

- registration, login, 2FA, password recovery and session management;
- profiles, user search, public pages and privacy settings;
- delivery of messages, files, read receipts, realtime updates and push notifications;
- messaging with users of other nodes when federation is enabled;
- user data export via `Users.ExportData`;
- user support through the channels configured by the node administrator; processing account restriction and deletion requests, sending related notices to the account email, and storing the reason and date of a restriction with the account. The restriction reason is removed when the account is permanently deleted;
- logging of errors, metrics and security events.

## 6. Data protection

### 6.1 Data transfer

- Clients and microservices use gRPC/HTTP/2 and HTTPS/TLS on the external perimeter.
- gRPC request authorization uses XAuth metadata: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Internal asynchronous events go through RabbitMQ / MassTransit.
- Inter-node traffic uses a separate secured channel — see "Encryption".

### 6.2 Passwords, tokens and 2FA

- Passwords are stored as hashes.
- Access token — a JWT in the `x-auth-token` header; its lifetime is set by the Identity configuration on your node.
- Refresh token is bound to the Device ID and stored in Identity.
- 2FA supports a TOTP authenticator and email codes.

### 6.3 Encrypted chats

- **Private chats:** the node stores the `EncryptedMessage` ciphertext, nonce and AAD; the key is derived on the client from the passphrase and chat salt.
- **Secret chats:** the node works with an opaque envelope bound to devices, relays it to the recipient and buffers it with a 24-hour TTL.
- **Regular chats:** messages are stored in your node's Messages service and are accessible both to server logic and to the node administrator.

## 7. Sharing with third parties

Personal data is not sold. Depending on the feature and public-node configuration, data may be sent to:

- other federation nodes — see section 4; each receiving node is responsible for its own copy;
- the configured SMTP service for account emails; the email delivery provider has not been confirmed;
- Firebase Cloud Messaging for push notifications when enabled;
- the Telegram Bot API to forward website support messages;
- the configured LiveKit server for calls; its hosting, operator and handling of metadata have not been confirmed;
- The checked main page and privacy policy responses contained the server: cloudflare and Network Error Logging (NEL) headers; the browser loads Google Fonts from fonts.googleapis.com and fonts.gstatic.com.

External providers may receive technical network data needed to deliver a request or report. Their configuration, processing locations and retention periods have not been confirmed, except for the node operator facts stated in this notice. Provider practices should not be inferred from the node's storage practices.


## 8. User rights

Send requests about your data to your node's administrator — only they can act on them.

- **Access and export:** export returns the JSON files `profile.json`, `messages.json` and `files.json`.
- **Correction:** profile, bio, avatar, poster, privacy, devices and password can be changed through client features where available.
- **Deletion:** after the request is processed, the account enters pending deletion for 7 days; background cleanup starts after that and may take longer if it must be retried after an error. See the [Account Deletion Policy](/legal/account-deletion).
- **Session revocation:** an active session can be terminated via device management features.
- **Limiting federation:** incoming federated private chats can be refused in the privacy settings.

## 9. Deletion of messages and data

- Regular messages support deletion via the Messages API.
- Private encrypted messages are marked as deleted on deletion, and the ciphertext is cleared.
- Secret messages are removed from the temporary buffer after delivery confirmation or TTL expiry.
- A federated message deletion is sent to participating nodes and applied after receipt and validation; delivery and application are not synchronous. It does not remove files already downloaded by correspondents or screenshots. Account deletion on barkfluff.com does not propagate to other nodes or erase data already delivered to them.
- For deletion of an account and data not covered by client features, contact the node operator. After the request is processed, the account remains pending deletion for 7 days; background cleanup then runs and may be delayed if it must be retried after an error. See the [Account Deletion Policy](/legal/account-deletion).

### Retention on the public node

According to the operator, no maximum retention period has been set for the public node's logs and backups. Data that may have entered them earlier may remain after account deletion. This does not mean that any particular data is necessarily included in a copy or that copies always exist or are retained; this statement does not describe third-party providers' retention. No confirmed operational retention periods for other categories of data have been provided.

## 10. Cookies and local storage

- The site uses the barkfluff_chat_id cookie (support chat identifier, up to 1 year), bf_open_chat (transition from a public profile page to the web client, up to 5 minutes) and bf_cookie_notice (notice acknowledgement, up to 1 year).
- The site uses localStorage bf_lang for the selected language and bf_domain_notice_v1 to remember dismissal of the domain-history notice; no separate expiry is set for these entries.
- The web client uses the bf_theme (theme), bf_lang (language) and bf_legal_accepted (accepted terms and policy revision) cookies, set for up to 1 year. Access tokens are stored in browser localStorage or sessionStorage.
- Native clients use local storage for tokens, settings and caches; the Windows client may protect GlobalParam.json with a PIN, and macOS uses Keychain.
- The public site loads fonts from fonts.googleapis.com and fonts.gstatic.com. The operator confirmed Cloudflare response headers and Network Error Logging (NEL) on the main page and privacy policy; the browser may send network-error reports to the endpoint configured for the site.
- BarkFluff does not deliberately set cookies for analytics or advertising. External site requests may send technical data to providers; see section 7.


## 11. Children

The public barkfluff.com node is for users aged 16 and older. Other nodes set their own age rules. If you believe a user under 16 has registered on barkfluff.com, contact privacy@barkfluff.com.

## 12. Policy changes

When this policy changes, the "Updated" date is updated. Significant changes may be communicated via the app or email. A node administrator publishes changes to their own version themselves.

## 13. Contacts

**Administrator of the barkfluff.com node** — for your data, export, deletion, support and complaints:

- Data and privacy: privacy@barkfluff.com
- Support: support@barkfluff.com
- Legal matters and content complaints: legal@barkfluff.com

**Developer of the BarkFluff software** — for code vulnerabilities and protocol questions:

- Security and protocol: security@barkfluff.com

The developer has no access to data on other nodes and cannot process a request to delete or disclose it. If your account was created on another node, look for its administrator's contacts on that node's website.
