[← Documentation hub](README.md)

<p align="center">
  <img src="../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Bots</h1>

<p align="center">
  <strong>Build external integrations against the BarkFluff Bot API.</strong>
</p>

<p align="center">
  <a href="../README.md">Overview</a> ·
  <a href="#create-a-bot">Create a bot</a> ·
  <a href="#authentication">Authentication</a> ·
  <a href="#capabilities">Capabilities</a> ·
  <a href="#rest-endpoints">REST endpoints</a> ·
  <a href="#limits">Limits</a>
</p>

---

BarkFluff ships a Telegram-style Bot API. A bot is a platform user (`is_bot = true`) controlled by an external program over HTTP or gRPC instead of a human typing into a client. The `BarkFluff.Bots` service (gRPC **7027**, HTTP **7028**) owns bot accounts, tokens, and message delivery.

## Create a bot

Message **@botfather** from any BarkFluff client — bot creation has no admin UI or client-side flow of its own, it's entirely a chat with BotFather:

| Command | Effect |
|---|---|
| `/newbot` | Walks through name → username, then issues the bot's token (shown once) |
| `/mybots` | Lists bots you own |
| `/token` | Reissues a bot's token (invalidates the previous one instantly) |
| `/setname`, `/setdescription`, `/setuserpic` | Edit a bot's profile |
| `/deletebot` | Deletes a bot and frees its username |
| `/cancel` | Cancels the current BotFather dialog |

Each account may own up to **10 bots**.

## Authentication

Every request — HTTP or gRPC — carries the bot's token in the **`x-auth-token`** header (never in the URL, so it can't leak into proxy access logs). The token is a long-lived JWT scoped to the bot; it authenticates *only* the Bot API, not the rest of the platform. Reissuing a token (`/token` in BotFather) revokes the previous one instantly, even for already-open connections.

## Capabilities

- **Send messages** — text (≤4096 characters), photos, and documents into chats the bot already belongs to.
- **A bot never starts a conversation** — a user must message the bot first; the exception is a small set of first-party system bots.
- **Edit and delete its own messages.**
- **Receive incoming messages** via long-polling `getUpdates` (HTTP) or a server-streamed `SubscribeUpdates` (gRPC) — only one active poll/stream per bot at a time.
- **Resolve attachments** it received to a temporary download link (`getFile`).
- **Look up public profiles** by user ID or username (`getUserInfo`).
- **Publish a command menu** for clients to show (`setMyCommands` / `getMyCommands`).

Not available yet: webhook delivery (polling/streaming only), `chat_member`/`edited_message` update types, and client-side command autocomplete UI.

## REST endpoints

All routes are under `/bot/` on the HTTP port and return `{"ok":true,"result":…}` / `{"ok":false,"error_code":…,"description":…}`.

| Endpoint | Method | Input |
|---|---|---|
| `getMe` | GET | — |
| `sendMessage` | POST (JSON) | `chat_id`/`user_id`, `text` |
| `sendPhoto`, `sendDocument` | POST (multipart) | `file`, `chat_id`/`user_id`, `caption?` |
| `editMessage` | POST (JSON) | `message_id`, `text`, `file_ids?` |
| `deleteMessage` | POST (JSON) | `message_id` |
| `getFile` | GET | `file_id` |
| `getUserInfo` | GET | `user_id` / `username` |
| `setMyCommands` | POST (JSON) | `commands: [{command, description}]` |
| `getMyCommands` | GET | — |
| `getUpdates` | GET | `offset?`, `limit?`, `timeout?` (long-poll) |

The same operations are available over gRPC (`BotsExternalApi`, port 7027) for integrations that prefer it over REST.

## Limits

- **30 requests/second** per bot, enforced across all service instances.
- **One concurrent `getUpdates`/`SubscribeUpdates`** per bot — a second poller is rejected until the first disconnects.
- **1 GB attachment storage quota** per bot.
- **10 bots per owning account.**

## Reference

Full architecture — token issuance, update delivery, database schema, rate-limit implementation — lives in [Bots in the knowledge base](../Obsidian/ClaudeVault/Backend/Bots.md).
