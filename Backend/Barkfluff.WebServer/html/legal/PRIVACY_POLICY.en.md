# BarkFluff Privacy Policy

**Effective from:** January 24, 2026  
**Last updated:** July 29, 2026

> This translation is provided for convenience. In case of any discrepancy, the Russian version prevails.

## 1. Who is responsible for your data

BarkFluff is a federated network of independent servers ("nodes"). Each node is deployed and operated by its administrator; nodes talk to each other to connect users of different nodes.

The controller of your personal data is **the administrator of the node where your account was created**. They host the services, own the database, file storage, backups and logs, decide the retention periods, answer your requests and are responsible for compliance with the law applicable to them.

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
| Email | Registration, login, confirmations, access recovery, notifications | Users / Identity |
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

WebServer serves the main page, public profile pages, legal pages, install scripts, client versions and the public profile REST API. Messages from the website support form are stored in the WebServer process memory and forwarded to the node administrator via the Telegram Bot API, if they configured such forwarding.

On clicking "Message in browser" on a public profile page, WebServer creates a short-lived cookie `bf_open_chat` so the web client opens a chat with the selected user.

## 4. Federated data exchange

Federation is disabled by default (`Federation:Enabled = false`) — the node administrator turns it on. When it is enabled and you chat with a user of another node, some data crosses your node's boundary.

| What leaves for another node | When | How |
| --- | --- | --- |
| Identifier, username, name and profile avatar | When a user of another node finds you or opens your profile card | Inter-node profile request, subject to your privacy settings |
| Message text, edits, deletions, read receipts | 1-to-1 conversation with a user of another node | Signed events with guaranteed delivery and retries |
| Files and attachments | When your correspondent opens a file | Streamed on request from their node; no copy is made in advance |
| Online status and last activity time | While a federated chat is active | Live inter-node stream; your privacy settings apply |
| "Typing…" | While you are typing a message | A one-off notification that is not stored |

What matters here:

- Federated chats are 1-to-1 only. Group chats between nodes are not supported.
- **Federated messages are not end-to-end encrypted.** Their content is available both to your node and to your correspondent's node. Private and secret chats work within a single node only.
- The receiving node becomes an **independent controller** of its copy. Neither your node nor the developers control how long or in what way it keeps that data. The model is the same as with email.
- Deleting a message or an account on your node **does not delete** copies already delivered to other nodes. There is currently no automatic propagation of deletion across the federation.
- You can refuse incoming federated private chats in your privacy settings — an attempt to start a chat with you from another node will then be rejected.
- Your node's administrator can block an entire node — data exchange with it will stop.

**Technical guarantees and their limits.** Inter-node events are signed with Ed25519, connections use TLS with server key fingerprint verification, and requests are signed within a limited time window. This proves **which node** the data came from and that it was not tampered with in transit, but gives no guarantee whatsoever about how that node's administrator handles the received copy.

## 5. What the data is used for

- registration, login, 2FA, password recovery and session management;
- profiles, user search, public pages and privacy settings;
- delivery of messages, files, read receipts, realtime updates and push notifications;
- messaging with users of other nodes when federation is enabled;
- user data export via `Users.ExportData`;
- user support through the channels configured by the node administrator;
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

Personal data is not sold. Data is shared with external providers only to operate the features the node administrator has enabled:

- **Other federation nodes:** see section 4.
- **SMTP:** email notifications, confirmation codes, access recovery.
- **Firebase Cloud Messaging:** push notifications to devices with a Firebase token.
- **Telegram Bot API:** handling messages from the website support chat.
- **Hosting and infrastructure:** hosting of services, databases, file storage, RabbitMQ, Redis and Seq.

The specific set of providers is chosen by the node administrator: they may disable push, use a different SMTP service, or do without Telegram.

## 8. User rights

Send requests about your data to your node's administrator — only they can act on them.

- **Access and export:** export returns the JSON files `profile.json`, `messages.json` and `files.json`.
- **Correction:** profile, bio, avatar, poster, privacy, devices and password can be changed through client features where available.
- **Deletion:** see the "Account Deletion Policy".
- **Session revocation:** an active session can be terminated via device management features.
- **Limiting federation:** incoming federated private chats can be refused in the privacy settings.

## 9. Deletion of messages and data

- Regular messages support deletion via the Messages API.
- Private encrypted messages are marked as deleted on deletion, and the ciphertext is cleared.
- Secret messages are removed from the temporary buffer after delivery confirmation or TTL expiry.
- Deletion applies within your node. Copies already delivered to another node can only be deleted by that node's administrator.
- For deletion of an account and data not covered by client features, contact the node administrator.

## 10. Cookies and local storage

- Native clients use local storage for tokens, settings and caches.
- The Windows client stores `GlobalParam.json` and can protect it with a PIN.
- The macOS client uses the Keychain for tokens.
- WebServer uses the `bf_open_chat` cookie only for the transition from a public profile page to the web client.

## 11. Children

BarkFluff is not intended for users under 13. A node administrator may set a higher age limit. If you believe a child under 13 has created an account, notify the administrator of the node in question.

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
