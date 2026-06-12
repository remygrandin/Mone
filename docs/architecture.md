# Architecture

Mone is a plugin-first infrastructure monitoring platform built on .NET 10, NATS
JetStream, and TimescaleDB/PostgreSQL. This document describes how the pieces fit
together: the services, how data flows between them, the messaging contracts, and
the persistence model.

For deploying these components see [deployment.md](deployment.md); for running
extra executors on other hosts see [remote-executors.md](remote-executors.md).

## Design goals

- **Plugin-first.** Probes, checkers, and notifications are all plugins loaded
  from a directory at startup. The core ships no monitoring logic of its own — it
  schedules, routes, persists, and presents.
- **Decoupled via messaging.** Services communicate over NATS JetStream subjects,
  not direct calls. Each stage (probe → check → alert) can scale, restart, or run
  remotely without the others knowing.
- **Semi-autonomous edge.** A Probe Executor needs no database. It pulls resolved
  config from the API, caches it locally, and spools results when the network is
  down — so an edge node keeps working through a console outage.
- **Time-series native.** Probe results and status history are stored in
  TimescaleDB so they can be queried and charted over time.

## Components

```
                         +-----------+
                         | Dashboard |
                         | (Blazor)  |
                         +-----+-----+
                               |
                         +-----+-----+       +----------+
    http://localhost:8080 |    API    |------>| Postgres |
                         | (ASP.NET) |       | Timescale|
                         +-----+-----+       +----------+
                               |
                         +-----+-----+
                         |   NATS    |
                         | JetStream |
                         +-----+-----+
                        /      |      \
            +-----------+ +----+------+ +-----------+
            |   Probe   | |  Checker  | |   Alert   |
            |  Executor | |  Engine   | |  Engine   |
            +-----------+ +-----------+ +-----------+
```

| Component | Project | Responsibility |
|-----------|---------|----------------|
| **API** | `Mone.Api` | REST API (minimal-API endpoints) + serves the Blazor WASM dashboard. Owns the database: runs EF Core migrations on startup, seeds the admin user, authenticates users (JWT / OIDC / LDAP), and is the source of truth for assignment config. Probe Executors pull their config from here. |
| **Dashboard** | `Mone.Dashboard` | Blazor WebAssembly client, served as static assets by the API. All UI. |
| **Probe Executor** | `Mone.ProbeExecutor` | Runs active probe plugins on a Quartz.NET schedule and hosts passive probe plugins (webhook HTTP, syslog/SNMP-trap UDP), which own their own listeners — the executor only arbitrates port collisions and hands each plugin its assignments plus the spooling result sink. Publishes results to NATS. Stateless w.r.t. Postgres — caches config and spools results to local SQLite. |
| **Checker Engine** | `Mone.CheckerEngine` | Consumes probe results from NATS, evaluates them with checker plugins, and publishes status changes. Reads/writes Postgres directly. |
| **Alert Engine** | `Mone.AlertEngine` | Consumes status changes from NATS and dispatches notifications via notification plugins. Reads Postgres for notification config, writes a delivery audit. |

Supporting libraries:

| Project | Role |
|---------|------|
| `Mone.Contracts` | Plugin interfaces (`IProbePlugin`, `ICheckerPlugin`, `INotificationPlugin`) and shared domain models (`ProbeResult`, `StatusChange`, `MonitoringStatus`, `ConfigManifest`). The only thing a plugin needs to reference. |
| `Mone.Infrastructure` | EF Core `MoneDbContext`, entities, migrations, and shared services (node identity, inheritance resolution). |
| `Mone.Messaging` | NATS JetStream abstraction: stream definitions (`MoneStreams`) and message contracts. |
| `Mone.PluginEngine` | Discovers and loads plugin DLLs from disk into a registry. |

## Data flow

The core loop is a one-way pipeline joined by NATS:

1. **Probe.** The Probe Executor runs a probe plugin — on its Quartz schedule, on
   a manual trigger, or in response to passive input — and produces a
   `ProbeResult` (`Status`, `Summary`, `Duration`, `Metadata`).
2. **Publish result.** It publishes a `ProbeResultMessage` to
   `probes.results.<…>` on the **PROBE_RESULTS** stream.
3. **Check.** The Checker Engine consumes the result and runs the checker plugins
   assigned to that target. A checker compares the result (and optionally recent
   metric history) against its thresholds and returns a `MonitoringStatus`.
4. **Publish status change.** When the status differs from the last known status,
   the engine writes `status_history` and publishes a `StatusChangeMessage` to
   `status.changes.<…>` on the **STATUS_CHANGES** stream.
5. **Alert.** The Alert Engine consumes the status change, resolves the
   notification configs for that target, and dispatches via notification plugins,
   recording each attempt in `notification_audit`.

Two side channels feed the executor:

- **PROBE_SCHEDULE** — the API broadcasts schedule changes (`probe.schedule.changed`)
  so executors pick up assignment edits without a restart.
- **PROBE_TRIGGERS** — a manual "run now" from the dashboard publishes to
  `probe.trigger.<…>`, which the executor consumes to run a probe immediately.

## Monitoring status model

Every checker resolves a target to one of five states (`MonitoringStatus`):

| Status | Meaning |
|--------|---------|
| `Unknown` | No data yet, or status not yet evaluated. |
| `Healthy` | Within thresholds. |
| `Degraded` | Warning band — working but outside the healthy range. |
| `Unhealthy` | Failing the configured threshold. |
| `Unreachable` | The probe itself could not complete (timeout, connection refused, etc.). |

## Messaging contracts

NATS JetStream streams are defined in `Mone.Messaging/MoneStreams.cs`:

| Stream | Subject prefix | Producer → Consumer | Payload |
|--------|----------------|---------------------|---------|
| `PROBE_RESULTS` | `probes.results.>` | Probe Executor → Checker Engine | `ProbeResultMessage` (target, probe, result) |
| `STATUS_CHANGES` | `status.changes.>` | Checker Engine → Alert Engine | `StatusChangeMessage` (checker, previous/current status, latest result) |
| `PROBE_TRIGGERS` | `probe.trigger.>` | API → Probe Executor | `ProbeTriggerMessage` (host, plugin, assignment, optional target override) |
| `PROBE_SCHEDULE` | `probe.schedule.>` | API → Probe Executor | schedule updates; `probe.schedule.changed` broadcasts a refresh |

There is also a lightweight `mone.plugins.reload` subject that tells running
services to reload plugins from disk.

Using JetStream (not core NATS) means results and status changes are persisted by
the broker and redelivered if a consumer is down — the pipeline tolerates a
Checker or Alert engine restart without dropping events.

## Persistence model

All relational state lives in one PostgreSQL database (TimescaleDB extension), via
`MoneDbContext` in `Mone.Infrastructure`. The main entities:

**Targets and grouping**

- `hosts` — monitored targets (name, address, enabled).
- `tags` + `host_tags` — free-form tags for grouping and filtering.
- `host_groups` + `host_group_memberships` — named groups for bulk assignment.

**Assignments (what runs against a target)**

- `probe_assignments` — a probe plugin bound to a host or group, with a cron
  schedule, merged config JSON, optional target-address override, and an optional
  executor-node binding.
- `checker_assignments` — a checker plugin bound to a host or group, with config
  JSON and an optional node binding.
- `probe_assignment_overrides` / `checker_assignment_overrides` — temporary
  per-target tweaks layered on top of a group assignment.
- `probe_assignment_metrics` — the metrics a probe assignment declares it emits
  (used to drive charts and checker metric selection).

**Time series (results)**

- `probe_results` — every probe execution outcome (status, summary, duration,
  metadata), keyed by `(timestamp, targetId, probeId)`.
- `status_history` — status transitions, keyed by `(timestamp, targetId,
  checkerId)`, with previous and current status.

**Notifications**

- `notification_configs` — configured notification plugin instances (SMTP
  settings, webhook URLs, …).
- `notification_audit` — delivery log: which alert went to which channel and
  whether it succeeded.

**Plugins**

- `plugin_repositories` — external plugin sources (e.g. GitHub repos) to sync.
- `plugin_manifests` — cached metadata for plugins discovered in those repos.
- `plugin_global_configs` — plugin-wide settings, distinct from per-assignment
  config.

**Nodes and identity**

- `executor_nodes` — registered remote Probe Executors / Checker Engines with
  name, role, and last heartbeat.
- ASP.NET Identity tables — users, roles, and claims for authentication.

### Assignment inheritance

Assignments can be made on a **group** and inherited by its member hosts, or made
directly on a **host**. An override entity lets a single host adjust an inherited
group assignment without detaching from the group. The "effective" assignment for
a host — group assignment + overrides + direct assignments, resolved — is computed
by the inheritance resolver in `Mone.Infrastructure` and exposed through the
*effective assignments* API.

## Plugin architecture

All plugins implement `IPlugin` (`Name`, `Version`, `Description`,
`InitializeAsync`) plus one kind-specific interface:

- **Probe** — `IProbePlugin` (active polling) or `IPassiveProbePlugin` (passive
  ingress, e.g. webhook HTTP, syslog/SNMP-trap UDP). A passive probe owns its own
  listener: it declares only a `Protocol` (TCP/UDP) and `Port` to the executor and
  hosts whatever responder, protocol decoding, and auth it needs.
- **Checker** — `ICheckerPlugin`, evaluating a `CheckerEvaluationContext` to a
  `MonitoringStatus`.
- **Notification** — `INotificationPlugin`, dispatching on a `StatusChange`.

A plugin that needs configuration also implements `IConfigurablePlugin`, exposing
a `ConfigManifest` of typed `ConfigField`s (string/int/double/bool/choice/secret,
with required/global flags, defaults, choices, and validation rules). The
dashboard renders this manifest as a form, so adding a config field needs no UI
changes.

Each service loads only the plugins relevant to it from its configured plugin
directory. A remote executor must have the **same** plugin DLLs as the
assignments it is expected to run — see [remote-executors.md](remote-executors.md).

For the catalogue of built-in plugins and their parameters, see the
**Mone-Plugins** repository's `docs/` set.

## Technology choices

| Concern | Choice | Why |
|---------|--------|-----|
| Runtime | .NET 10 | Single stack across API, services, and plugins. |
| UI | Blazor WebAssembly (MudBlazor) | Share C# models with the backend; no separate JS build. |
| Messaging | NATS JetStream | Lightweight, persistent streams that decouple the pipeline stages. |
| Database | PostgreSQL + TimescaleDB | Relational config plus first-class time-series for results/history. |
| Scheduling | Quartz.NET | Cron-style probe scheduling in the executor. |
| Edge buffering | SQLite | Local spool for config cache and store-and-forward results. |
