# BFT Proxy

Parent: [[Architecture]]. BFT Proxy is a standalone TCP service for clients that need an alternate route to BarkFluff HTTPS/gRPC origins. Exit terminates the BFT tunnel and forwards requests to configured HTTPS upstreams; Relay forwards the encrypted stream to Exit. The wire format is defined only in `Docs/BFTv1.md`; shared protocol code is documented in [[Shared/Bft]].

## Deployment and entry points

- Host and CLI entry point: `Backend/BarkFluff.BftProxy/Program.cs` and `BftProxyCli.cs`.
- Exit: `BftProxyHost.cs` accepts direct client connections on `BFT_LISTEN` and, when configured, Relay connections on `BFT_RELAY_LISTEN`.
- Relay: `BftRelayHost.cs` accepts clients on `BFT_LISTEN` and connects to the configured `BFT_NEXT_HOP`.
- `Backend/BarkFluff.BftProxy/Dockerfile` packages `dotnet publish` output on `aspnet:10.0-noble-chiseled` and runs as the base image's non-root application UID. Deployment examples are in `Docker/nightly/bft-proxy/README.md`; Exit and Relay compose files are independent and are not part of the node compose.
- The service does not use Settings, XAuth, RabbitMQ, or a database. It has no HTTP health endpoint; the image has no Docker healthcheck.

## Modes and configuration

| Variable | Mode | Meaning and constraints |
|---|---|---|
| `BFT_MODE` | Both | Required: `Exit` or `Relay`. |
| `BFT_LISTEN` | Both | Required `host:port`; use a wildcard/address and non-zero port. In deployment compose, container port is `8443`. |
| `BFT_STATIC_PRIVATE_KEY` | Exit | Required 32-byte unpadded base64url key; `genkey` prints it and its public key. Keep the private value on Exit only. |
| `BFT_CLIENT_KEYS_FILE` | Exit | Required path to the client-key file. Exit requires the file to exist at startup. The list is reread every 2 seconds. |
| `BFT_PROXY_ID` | Exit | Required 1–32 ASCII letters, digits, `.`, `_`, or `-`; used in the BFT HELLO and origin header. |
| `BFT_ORIGIN_SECRET` | Exit | Required even-length hex, at least 64 characters; normalized to lowercase. Must match the origin Nginx secret map. |
| `BFT_ACCESS_TOKENS` | Exit | Optional comma-separated tokens, each 1–256 UTF-8 bytes. |
| `BFT_RELAY_LISTEN` | Exit | Optional second listener. When set, `BFT_RELAY_KEY` and at least one `BFT_TRUSTED_RELAYS` IP are required. |
| `BFT_TRUSTED_RELAYS` | Exit | Comma-separated IP literals allowed to connect to the Relay listener; use the Relay's source IP as seen by Exit. |
| `BFT_RELAY_KEY` | Exit and Relay | 32-byte unpadded base64url key. The same value is required on both ends when Relay is enabled. |
| `BFT_NEXT_HOP` | Relay | Required Exit `host:port`. |
| `BFT_RELAY_ID` | Relay | Required 1–16 printable ASCII characters; authenticated in the Relay preamble. |
| `BFT_RELAY_MAX_CONNECTIONS_PER_IP` | Relay | Default 64; range 1–4096. |
| `BFT_RELAY_MAX_CONNECTIONS` | Relay | Default 4096; range 1–65536. |
| `BFT_RELAY_CONNECT_TIMEOUT_SECONDS` | Relay | Default 15 seconds; range 1–120. |
| `BFT_UPSTREAM_BEACON` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_IDENTITY` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_USERS` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_MESSAGES` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_FILES` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_FILES_MEDIA` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_UPDATES` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_ONLINER` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_FASTAUTH` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_CALLS` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_BOTS` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_UPSTREAM_STORAGE` | Exit | Optional absolute HTTPS URL without credentials, query, or fragment. |
| `BFT_MAX_TUNNELS_PER_KEY` | Exit | Default 16; range 1–4096 established tunnels per client-key label. |
| `BFT_MAX_TUNNELS` | Exit | Default 4096; range 1–65536 established tunnels in total. |

Pending handshakes have separate limits: 16 per source IP and 1024 total. Relay's limits apply to active connections. Exit creates an ephemeral TLS certificate and advertises its SPKI pin in HELLO; the client pins that key for the tunnel. Upstream HTTPS certificates are validated normally. The upstream connection pool lifetime is 10 minutes, with a 15-second connect timeout.

## Client keys and CLI

`BftProxyCli.RunAsync` dispatches `genkey`, `add-client`, and `remove-client`; with no command it starts the configured server. Run all commands through the Exit compose service so they receive `exit.env` and the same writable `client-keys` volume as the server:

```sh
docker compose -f docker-compose.exit.yml run --rm --no-deps exit genkey
docker compose -f docker-compose.exit.yml run --rm --no-deps exit add-client --label phone --name "Phone" --host relay-ru.barkfluff.com --port 443
docker compose -f docker-compose.exit.yml run --rm --no-deps exit remove-client --label phone
```

`add-client` prints the encoded link `bft://<base64url(body)>` (`BftConnectionString.FormatEncoded`, see `Docs/BFTv1.md` §3). `--host` and `--port` identify the public client-facing endpoint: the Exit host/port for direct access or Relay host/port when routed through Relay. Key-file changes are picked up without restart; a removed key revokes its active tunnels on the next reload. The volume must be writable by UID 1654 so the CLI can append/remove entries.

## Trust boundaries and forwarding

- Relay sees the client's network source IP, connection timing/volume, and its configured Exit next hop. It forwards the BFT bytes and does not hold client obfuscation keys, terminate the Exit TLS session, or read HTTP/gRPC request contents.
- Exit authenticates/decrypts the BFT tunnel, sees the client IP, matched key label, service ID, and decrypted HTTP/1.1 or HTTP/2 request, then forwards each stream to its configured HTTPS upstream.
- Exit removes client-supplied `X-BFT-*`, `X-Forwarded-*`, `X-Real-IP`, `Forwarded`, `CF-Connecting-IP`, and `True-Client-IP`. It sets `X-BFT-Client-IP`, `X-BFT-Proxy-Id`, and `X-BFT-Proxy-Secret` on the HTTPS request to origin.
- Origin Nginx trusts a request only when the real-ip-adjusted Exit source IP, secret, and client IP are valid. It uses the client IP for `X-Real-IP`, `X-Forwarded-For`, and existing rate-limit keys; it strips `X-BFT-Client-IP` and `X-BFT-Proxy-Secret` before apps and forwards the trusted `X-BFT-Proxy-Id`. `RequestContextInterceptor` maps ProxyId only for unary gRPC calls.
- Relay and Exit are separate trust domains. If Relay is used, restrict Exit's Relay listener to Relay source IPs at both host firewall and `BFT_TRUSTED_RELAYS`. Keep the Relay↔Exit key and Exit↔origin secret private.
- LiveKit WebSocket/WebRTC and media traffic bypass BFT Proxy. Only configured service IDs in the BFT upstream map traverse Exit.

Exit logs accepted tunnels with key label and client IP; Relay logs accepted connections with client IP and relay ID. Forwarding failures are logged per request/stream and do not necessarily close the whole tunnel.
