# BarkFluff Privacy Policy

**Effective from:** January 24, 2026  
**Last updated:** June 17, 2026

> This translation is provided for convenience. In case of any discrepancy, the Russian version prevails.

## 1. Introduction

This Policy describes the data processed in the current BarkFluff platform: account, profile, devices, messages, files, client versions, public profile pages, and the website support chat.

## 2. What data is processed

### 2.1 Account and profile

| Data | Where used | Storage |
| --- | --- | --- |
| Email | Registration, login, confirmations, access recovery, notifications | Users / Identity |
| Username | Login, search, public profile page, display in chats | Users |
| Password hash | Password verification; the plaintext password is not stored | Identity |
| First name, last name, bio | User profile and public profile data | Users |
| Avatar and profile poster | Profile and public page styling | Files / Minio + Users metadata |
| Privacy and personalization settings | Display of profile data and client appearance | Users |

### 2.2 Devices and sessions

| Data | Where used | Storage |
| --- | --- | --- |
| Device ID | Refresh token binding, active sessions list, session revocation | Identity.RefreshTokens, Users.UserDevices |
| Device name | Device display and renaming | Users.UserDevices |
| OS, app name | Client compatibility and device list | Users.UserDevices |
| Location | Device display in the sessions list | Users.UserDevices |
| Firebase token | Push notifications via Firebase Cloud Messaging | Users.UserDevices |
| Refresh token | Session renewal and sign-out from device | Identity.RefreshTokens |

The IP address is passed in gRPC service metadata headers and is used to process current requests.

### 2.3 Messages, chats and files

| Data | Where used | Storage |
| --- | --- | --- |
| Regular messages | Private and group chats, sync, read receipts, editing and deletion | Messages / PostgreSQL |
| Private encrypted chats | 1-to-1 chats with client-side encryption | Messages stores ciphertext and chat metadata |
| Secret chats | 1-to-1 chats between specific devices | The server relays the envelope and buffers it temporarily |
| Attachments and previews | Transfer of files, images, video, documents, audio and avatars | Files / Minio + PostgreSQL metadata |
| Read statuses | Display of read messages | Messages |

### 2.4 Online statuses and updates

Onliner stores and serves the online status and last activity time. Updates delivers realtime events to clients via gRPC streaming.

### 2.5 Website and support

WebServer serves the main page, public profile pages, legal pages, install scripts, client versions and the public profile REST API. Messages from the website support form are stored in the WebServer process memory and forwarded to the administrator via the Telegram Bot API.

On clicking "Message in browser" on a public profile page, WebServer creates a short-lived cookie `bf_open_chat` so the web client opens a chat with the selected user.

## 3. What the data is used for

- registration, login, 2FA, password recovery and session management;
- profiles, user search, public pages and privacy settings;
- delivery of messages, files, read receipts, realtime updates and push notifications;
- user data export via `Users.ExportData`;
- user support via the website, email and the Telegram bot;
- logging of errors, metrics and security events.

## 4. Data protection

### 4.1 Data transfer

- Clients and microservices use gRPC/HTTP/2 and HTTPS/TLS on the external perimeter.
- gRPC request authorization uses XAuth metadata: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Internal asynchronous events go through RabbitMQ / MassTransit.

### 4.2 Passwords, tokens and 2FA

- Passwords are stored as hashes.
- Access token — a JWT in the `x-auth-token` header; its lifetime is set by the Identity configuration.
- Refresh token is bound to the Device ID and stored in Identity.
- 2FA supports a TOTP authenticator and email codes.

### 4.3 Encrypted chats

- **Private chats:** the server stores the `EncryptedMessage` ciphertext, nonce and AAD; the key is derived on the client from the passphrase and chat salt.
- **Secret chats:** the server works with an opaque envelope bound to devices, relays it to the recipient and buffers it with a 24-hour TTL.
- **Regular chats:** messages are stored in the Messages service and accessible to server logic, which provides sync, export and chat features.

## 5. Sharing with third parties

We do not sell personal data. Data is shared with external providers only to operate current features:

- **SMTP:** email notifications, confirmation codes, access recovery.
- **Firebase Cloud Messaging:** push notifications to devices with a Firebase token.
- **Telegram Bot API:** handling messages from the website support chat.
- **Hosting and infrastructure:** hosting of services, databases, file storage, RabbitMQ, Redis and Seq.

## 6. User rights

- **Access and export:** export returns the JSON files `profile.json`, `messages.json` and `files.json`.
- **Correction:** profile, bio, avatar, poster, privacy, devices and password can be changed through client features where available.
- **Deletion:** a request to delete your account and associated personal data can be sent to privacy@barkfluff.com.
- **Session revocation:** an active session can be terminated via device management features.

## 7. Deletion of messages and data

- Regular messages support deletion via the Messages API.
- Private encrypted messages are marked as deleted on deletion, and the ciphertext is cleared.
- Secret messages are removed from the temporary buffer after delivery confirmation or TTL expiry.
- For deletion of an account and data not covered by client features, use a support request.

## 8. Cookies and local storage

- Native clients use local storage for tokens, settings and caches.
- The Windows client stores `GlobalParam.json` and can protect it with a PIN.
- The macOS client uses the Keychain for tokens.
- WebServer uses the `bf_open_chat` cookie only for the transition from a public profile page to the web client.

## 9. Children

BarkFluff is not intended for users under 13. If you believe a child under 13 has created an account, contact us: privacy@barkfluff.com.

## 10. Policy changes

When this policy changes, the "Updated" date at the top of the document is updated. Significant changes may be communicated via the app or email.

## 11. Contacts

- Privacy: privacy@barkfluff.com
- Support: support@barkfluff.com
- Security: security@barkfluff.com
- Website: https://barkfluff.com
