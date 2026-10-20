# Service logs and trace context

Hosts using `AddBarkFluffSerilog` configure Console, Seq, and Lognara as independent sinks.
The root-level flags default to Console on, Seq on, and Lognara off. The 15 Settings-backed
services read the flags for their own `ServiceId`; there is no shared/global flag inheritance.
Changes take effect at process startup, so restart the affected host after changing them.

## Configure sinks

The Settings catalog exposes these root-level flags in each of the 15 service scopes:

| Key | Default | Meaning |
| --- | --- | --- |
| `ConsoleOutput` | `true` | Write Information-and-higher events to the console. |
| `SeqEnable` | `true` | Send events to Seq. |
| `LognaraEnable` | `false` | Send events to the Lognara HTTP agent. |

The flags are independent rather than a single backend selector, so any combination can be
configured. For example, `SeqEnable=true` and `LognaraEnable=true` select both remote backends.
Change them through
the existing Settings/AdminPanel configuration API for Settings-backed hosts. Precedence for
logging and agent configuration is command line, environment, matching Settings values, then local
configuration; this applies only to logging flags and Lognara agent URL/executable keys. Container
environment variables use the same root flag names, for example `SeqEnable=false`. `Seq:ServerUrl` continues
to default to `http://seq:5341`.

The 15 Settings-backed scopes are Identity, Users, Beacon, Notifications, Files, Messages,
FastAuth, Updates, Onliner, CloudMessaging, Web, Developers, Calls, Bots, and Federation.
Settings, Setup, Navigator, ClientStorage, and WebServer use local configuration; AdminPanel also
uses local flags while loading `ServiceId.Unknown`. For these local-startup hosts, command-line
and environment overrides still take precedence. The legacy `Logging:Backend` value is no longer
used to choose sinks; its existing row and history are retained.

Settings initializes its logger before the Settings database is ready, so startup events use its
local flags; saved Settings values cannot retroactively change those events.

Each .NET container can run an embedded HTTP-only Lognara agent beside the application. The
application sends events to `http://127.0.0.1:7400/v1/logs`; configure `Lognara:AgentExecutable`
with the executable path to enable it. When unset, the application uses an externally managed agent.
Compose explicitly sets this path to `/usr/local/bin/lognara-agent`. The agent gets its relay URL
and relay token from native `LOGNARA_*` environment variables; Lognara core credentials stay with
the relay. The application does not capture its own stdout. Agent diagnostics go to the container
console only when `ConsoleOutput` is on, and are not sent back through the application's logger.
The global `Lognara:AgentUrl` defaults to the loopback URL above and can be overridden locally or
through CLI/environment configuration. When `Lognara:AgentExecutable` is set, this URL must remain
the loopback endpoint; URL overrides apply when using an external agent.

## Docker Compose deployment

The nightly Compose overlays use one `lognara-relay` for the installation and attach the service
bundles to the external `barkfluff-network`. Create that network once if it does not exist:

```sh
docker network create barkfluff-network
```

Start Lognara Core/Dock first, create a Relay connection there, then create a separate agent
registration for every source and bind each agent to that relay. Put the Core URL and relay-to-Core
token in the private Compose environment. Put each agent-to-relay token in the matching `*_LOGNARA_RELAY_TOKEN`
entry from [`Docker/Lognara/lognara.env.example`](../Docker/Lognara/lognara.env.example). The
template contains blank placeholders; keep real tokens in a private env file and do not commit it.
Do not reuse agent tokens between sources. Compose does not create Core/Dock registrations.

The relay requires `LOGNARA_CORE_URL` and `LOGNARA_CORE_TOKEN`. `LOGNARA_SERVER` is required for
the captured sources. `LOGNARA_BACKEND` defaults to `barkfluff`, `LOGNARA_RELAY_URL` defaults to
`http://lognara-relay:7401/v1/batches`, and `LOGNARA_ENVIRONMENT` is optional. Ensure the relay
container can reach Core at the configured URL. The application agent receives the relay URL and
its own source token; Core credentials are only configured on the relay. The app-to-agent HTTP
endpoint and relay port are not published on the host: the app endpoint listens on loopback, and
the relay is reachable to other bundles over `barkfluff-network`.

Add the Lognara variables from the template to the existing private `.env` for each bundle, or pass
that file with Compose's `--env-file` option. Run each command from its listed directory. For a
first-time installation, from `Docker/nightly/barkfluff` start only the shared relay first:

```sh
docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d lognara-relay
```

Then run the Setup overlay when needed. After the existing Settings setup flow completes, start the
full main bundle. On an already configured installation, start the full main bundle directly. The
other bundles can then use its relay over `barkfluff-network`.

| Bundle directory | Command |
| --- | --- |
| `Docker/nightly/barkfluff` | `docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d --build` |
| `Docker/nightly/barkfluff` (Setup) | `docker compose -f docker-compose.setup.yml -f docker-compose.setup.lognara.yml up -d --build` |
| `Docker/nightly/clientstorage` | `docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d` |
| `Docker/nightly/website` | `docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d` |
| `Docker/nightly/navigator/nginx` | `docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d --build` |
| `Docker/nightly/nginx` | `docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d --build` |
| `Docker/nightly/proxy` | `docker compose -f docker-compose.yml -f docker-compose.lognara.yml up -d --build` |

The main overlay starts the relay and captures PostgreSQL/bootstrap, Redis, RabbitMQ, LiveKit, and
Seq. MinIO capture is optional; start it from the main bundle directory with:

```sh
docker compose --profile lognara-minio -f docker-compose.yml -f docker-compose.lognara.yml up -d --build
```

The Setup overlay includes separate `setup`, `settings-setup`, `postgres-setup`, and
`postgres-bootstrap-setup` sources and expects the main relay to be running. Each bundle has
separate named volumes for the .NET sink buffer at `/app/logs` and the agent spool at
`/var/lib/lognara-agent/spool`; the relay has its own spool volume. These volumes are separate from
business data volumes. Preserve them across container replacement if queued events must survive.
The base Setup Compose file does not include Seq but defaults `SETUP_SEQ_ENABLE` and
`SETUP_SETTINGS_SEQ_ENABLE` to `true`; when running without the Lognara overlay, make Seq reachable
externally or set the corresponding variable to `false`, otherwise events stay in the bounded
100 MiB Seq buffers while Seq is unreachable.
The slim .NET images remain non-root (UID 1654) and seed both writable paths with that owner. Their
build installs static agent release `agent-20261018-041923-6db0c80dd298` and checks the published
SHA-256 for amd64 and arm64. Infrastructure images run the agent around the original container
entrypoint and command, preserving their arguments. The app, agent, and relay named volumes need no
host-side permission changes or init container.

Compose overrides each .NET service's flags to Lognara on, Seq off, and Console on. Set the
service-prefixed variables in the private env file to change them, for example
`BEACON_LOGNARA_ENABLE=false`, `BEACON_SEQ_ENABLE=true`, and `BEACON_CONSOLE_OUTPUT=true` to roll
Beacon back to Seq. Apply the same three variables with the relevant service prefix, then rerun
that bundle's `up -d` command to recreate its container. The default application flags outside the
Lognara overlays remain Lognara off, Seq on, and Console on. Infrastructure capture in the main
overlay remains enabled while that overlay is active.

When Lognara is enabled with an embedded agent, its relay token must be configured; a missing
token or agent readiness failure prevents the .NET host from starting. If the embedded agent exits
unexpectedly, the host exits nonzero even when Seq or Console is enabled, and Docker restarts it
under the service's existing restart policy. A relay/Core outage keeps events in the agent/relay
spools for retries. During host shutdown, the other workers stop, Serilog flushes, and then the
agent receives SIGTERM. Agent diagnostics appear directly in the Docker console only when
`ConsoleOutput` is enabled; they do not re-enter the application logger.

## Event format and buffering

The shared logger uses `Serilog.Sinks.Lognara` 0.1.2 rather than a project-specific HTTP formatter.
The sink sends the message, level, timestamp, exception, structured properties, and trace/span IDs
when present. Its JSON events put canonical `trace_id` and `span_id` at the top level; parent span,
request ID, and business correlation ID remain under `attributes`. Metrics remain under
`attributes.Metrics` in schema v2. The exception is sent as a top-level string and stored under
attributes by the relay. The existing activity enricher supplies W3C trace IDs when available.

The sink batches events in the background (up to 100 events and a 1 MiB target payload) and retries
delivery errors. BarkFluff configures a disk queue for the application sink at `logs/lognara-buffer`:
up to four files of about 16 MiB each, plus bookkeeping files. The last event may slightly exceed
the file size; when the file limit is reached, the oldest files are removed. Events larger than
256 KiB UTF-8 are dropped. Without a disk path, the in-memory queue is limited to 16 MiB of
serialized events and drops new events when full. The application queue is separate from the agent
spool at `/var/lib/lognara-agent/spool`; the Seq sink retains its existing 100 MiB disk buffer.
These are bounded delivery queues, not unlimited audit storage. Switching a sink off does not drain
its queue.

Version 0.1.2 fixes the LF/CRLF blank-line framing issue for new disk-buffer writes. Updating the
package does not repair queue files already written by 0.1.1. If such files still contain unsent
records, stop the application and preserve the complete buffer directory, including bookkeeping
files, for separate recovery; do not expect the update to drain those old files.

The AdminPanel's log browser, compression and metric rollups still read Seq. With Lognara-only
selected, use Lognara Dock for new service logs and metrics; the panel is not a Lognara query client.

## Trace propagation

An HTTP middleware registered through `IStartupFilter` encloses HTTP endpoints and all gRPC
shapes, including unary exception handling and streaming cancellation. It preserves the current
ASP.NET Activity, scopes RequestId and an incoming `X-Correlation-ID`, and creates a fallback
Activity only when needed. Startup and periodic metric summaries may have no request trace.

A hosted ActivityListener enables propagation for ASP.NET Core, HttpClient, gRPC client and
MassTransit with `PropagationData` sampling. It collects no span payload and installs no exporter.
Outbound HTTP/gRPC and messaging use their existing library propagation mechanisms, including
MassTransit 8.5.9's own diagnostic headers. XAuth and public proto/event payloads are unchanged.
Business X-Correlation-ID is scoped locally; it is not a new cross-service wire contract.

Messages and Federation persist `TraceParent`, `TraceState` and optional business `CorrelationId`
beside their outbox payloads. Dispatch/retry restores that parent with a new span; old rows without
context start independent traces. Federation batches only rows with the same persisted context
in one RPC, so mixed contexts can increase the number of calls. Existing claim, lease and per-chat
ordering remain intact. Signed federation payloads remain unchanged.

The additive `20261019211114_AddOutboxTraceContext` migrations for Messages and Federation add
three nullable text columns. Their existing service startup applies pending migrations: plan the
schema upgrade before restarting these services. The development task generates and validates
migrations without applying them to a working database. Existing binaries tolerate the added
columns; legacy outbox rows remain deliverable.
