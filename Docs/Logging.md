# Service logs and trace context

Services using `AddBarkFluffSerilog` write to Console and one selected remote backend.
Seq is the default. The selection is read at startup; restart consumers after changing it.

## Select the backend

The Settings global catalog exposes:

| Key | Default | Meaning |
| --- | --- | --- |
| `Logging:Backend` | `Seq` | `Seq` or `Lognara`, case-insensitive. Unknown values fail startup. |
| `Lognara:AgentUrl` | `http://127.0.0.1:7400/v1/logs` | HTTP(S) ingest URL of a separately deployed Lognara agent. |

Change these keys through the existing Settings/AdminPanel configuration API. The insert-only
seeder adds missing keys without changing saved values. Set `Logging:Backend` back to `Seq`
and restart services to switch back. `Seq:ServerUrl` continues to work, defaulting to
`http://seq:5341`. No real settings or deployments are changed by the code change.

Settings itself initializes logging before its database is ready, so configure its logger
through local configuration or environment (`Logging__Backend`, `Lognara__AgentUrl`).
AdminPanel uses the shared logger after loading Settings and applying its environment overrides.
The separate bootstrap Setup application retains its existing console logging and does not
use the shared logging helper. Failures before shared logger initialization are outside its sinks.

Lognara-agent must be running at the configured URL. It forwards to relay/core using its own
configuration and credentials; the application does not receive relay/core tokens. Configure
`LOGNARA_SERVICE`, `LOGNARA_SERVER`, `LOGNARA_BACKEND`, and optional environment/instance identity
on each agent. The default loopback URL assumes the agent runs in the application's container;
use an explicit reachable agent URL for another topology. No agent installation or Docker
topology change is included. Configure the agent to avoid also collecting the same application
console events when using HTTP ingestion, otherwise those events can appear twice.

## Event formats and buffering

Seq keeps native trace IDs and existing `TraceId`, `SpanId`, `RequestId`, `CorrelationId` properties.
Existing AdminPanel filters remain compatible. Lognara receives JSON arrays, with `timestamp`,
`level`, `message`, `trace_id`, `span_id`, optional `parent_span_id`, and `request_id` at the top level.
Additional properties are under `attributes`; `attributes.Metrics` retains the schema-v2 object
required by Lognara's metric extractor. Exception type and stack trace use
`attributes["exception.type"]` and `attributes["exception.stacktrace"]`.

The formatter reads IDs captured on the event, rather than the shipping thread's Activity.
Trace IDs are W3C 32-hex values and span IDs are 16 hex. A business CorrelationId remains a
separate attribute and never replaces TraceId.

Both backends use disk buffering and batches of up to 100 events with a two-second shipping
period. Seq retains its existing 100 MiB buffer. Lognara uses approximately ten 10 MiB rolled
files under `logs/lognara-buffer`, a 1 MiB event limit and a 1.5 MiB batch payload limit, below
the agent's 2 MiB HTTP body limit. Buffer limits can drop events on prolonged outages; oversized
Lognara events are dropped. These are bounded delivery buffers, not an unlimited audit store.
Preserve the `logs/` directory across container replacement if unsent events must survive it.
Changing backends does not drain the inactive backend's buffer; restore that backend to replay it.

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

## Verification

Targeted tests cover real loopback HTTP delivery to Seq/Lognara-compatible receivers, structured
metric and exception serialization, real HTTP/2 gRPC unary/server/client/duplex calls, cancellation
and unary exception logs, HTTP propagation, MassTransit in-memory publish/consume, outbox
persistence/retry and mixed-trace federation delivery. Migration tests compare model snapshots
and generate PostgreSQL SQL without connecting to a database.

Actual RabbitMQ delivery and Lognara agent → relay → core → Dock search require a separately
configured integration environment. A compatible HTTP receiver does not replace that deployment
check. No client platform code or public protobuf contracts are changed.
