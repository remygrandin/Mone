# Deploying Remote Probe Executors and Checker Engines

By default every Mone service runs on one host via `docker compose up`. You can
also run **additional** Probe Executors or Checker Engines on separate hosts —
to probe from a different network vantage point, spread checker load, or keep
running closer to the targets being monitored.

This guide covers how a remote executor connects back to the central console,
how it identifies itself, and how you bind specific work to it.

## How a remote executor connects back

A remote executor talks to the central deployment over three channels:

| Channel | Direction | Purpose |
|---------|-----------|---------|
| **API** (HTTP) | executor → API | Self-registration and 30s heartbeat so the node shows up under **Settings → Nodes** with live health. |
| **NATS JetStream** | bidirectional | Receives schedule/trigger messages and publishes probe results / status changes. |
| **PostgreSQL** | executor → DB | Reads assignment config (which probes/checkers run, their settings) and writes results. |

> **Current requirement:** remote executors still connect directly to Postgres
> for configuration and result storage. Make sure Postgres is reachable from the
> remote host (and firewalled appropriately). The semi-autonomous executor work
> (config over the API, results buffered locally and forwarded over NATS) will
> remove the direct Postgres dependency — until then, plan network access for
> all three channels.

## Node identity

Each executor resolves a stable identity on startup:

- **Id** — `Mone__Node__Id` if set (a GUID), otherwise a deterministic GUID
  derived from the machine name + role. Stable across restarts with zero config.
- **Name** — `Mone__Node__Name` if set, otherwise `<machine>-<role>`
  (e.g. `edge01-probe`). This is the human-readable label shown in the Nodes page.
- **Role** — fixed per service: Probe Executor registers as `Probe`, Checker
  Engine as `Checker`.

Set `Mone__Node__Name` explicitly on remote hosts so they are easy to recognise
in the dashboard. If you run **both** a probe executor and a checker engine on
the same host, give them distinct names — they register as separate nodes
(different roles → different deterministic Ids).

## Environment variables

| Variable | Service | Required | Description |
|----------|---------|----------|-------------|
| `ConnectionStrings__Postgres` | both | yes | PostgreSQL/TimescaleDB connection string, reachable from the remote host. |
| `ConnectionStrings__Nats` | both | yes | NATS server URL, e.g. `nats://console.example.com:4222`. |
| `Mone__Api__BaseUrl` | both | yes¹ | Base URL of the API, e.g. `https://console.example.com`. Without it, registration/heartbeat are disabled (the executor still runs but never appears under Nodes). |
| `Mone__Node__Token` | both | recommended | Shared secret sent as the `X-Node-Token` header on register/heartbeat. Must match the API's `Mone__Node__Token`. If unset on the API, node routes are open. |
| `Mone__Node__Name` | both | recommended | Friendly node name shown in the dashboard. |
| `Mone__Node__Id` | both | optional | Override the node GUID. Leave unset to use the stable per-machine default. |
| `Mone__Node__Address` | both | optional | Advertised IP/hostname shown in the Nodes page (informational; does not affect connectivity). |
| `ProbeExecutor__PluginDirectory` | probe | yes | Path to probe plugin DLLs (default `/app/plugins`). |
| `CheckerEngine__PluginDirectory` | checker | yes | Path to checker plugin DLLs (default `plugins`). |

¹ Technically optional — the executor will still run and process work without
it — but without `Mone__Api__BaseUrl` the node never registers, so you lose
health visibility and cannot bind assignments to it. Always set it for remote
nodes.

> Config keys use `:` internally (`Mone:Node:Name`); as environment variables
> the separator is `__` (double underscore): `Mone__Node__Name`.

### Securing the node channel

The API gates `/register` and `/heartbeat` behind `Mone__Node__Token`:

- If `Mone__Node__Token` is **set on the API**, executors must send the same
  value (via `Mone__Node__Token`, which becomes the `X-Node-Token` header) or
  registration returns `401 Unauthorized`.
- If it is **unset on the API**, the node routes are open — acceptable on a
  trusted private network, not for an API exposed to the internet.

Set the **same** token on the API and on every remote executor. Collect it with
your secrets workflow; never commit it.

## Plugins must match

A remote executor only runs plugins it has loaded locally. Ship the **same
plugin DLLs** to the remote host's plugin directory as the ones the assignment
expects, or the assignment will have nothing to execute. The bundled images
include the built-in plugins; for custom plugins, build them and mount/copy them
into the plugin directory.

## Binding work to a node

Node binding controls *where* an assignment runs:

- **Unbound (default):** the assignment runs on **every** executor of the right
  role. This preserves the single-host behaviour — nothing changes until you
  bind.
- **Bound:** in **Host → Settings → Probe Assignments** (or **Checker
  Assignments**), set **Run on node** to a specific node. The assignment then
  runs **only** on that node; all other executors skip it.

Schedulers and stream consumers enforce this: an executor skips any assignment
whose `ExecutorNodeId` is set to a different node, and runs everything else.

Bind a node only after it has registered (so it appears in the **Run on node**
dropdown). If a bound node is later deleted, its assignments revert to unbound
(`ON DELETE SET NULL`) and resume running everywhere.

## Example: docker-compose on the remote host

Run just a probe executor on a separate host, pointing back at the central
console. Create a `.env` next to this file with `MONE_NODE_TOKEN`,
`PROBE_NODE_NAME`, and `POSTGRES_PASSWORD`, then:

```yaml
# remote-probe.compose.yml
services:
  probe-executor:
    image: mone/probe-executor:latest   # or build from src/Mone.ProbeExecutor/Dockerfile
    environment:
      ConnectionStrings__Postgres: Host=console.example.com;Port=5432;Database=mone;Username=mone;Password=${POSTGRES_PASSWORD}
      ConnectionStrings__Nats: nats://console.example.com:4222
      Mone__Api__BaseUrl: https://console.example.com
      Mone__Node__Token: ${MONE_NODE_TOKEN}
      Mone__Node__Name: ${PROBE_NODE_NAME:-edge01-probe}
      ProbeExecutor__PluginDirectory: /app/plugins
    volumes:
      - ./plugins:/app/plugins
    cap_add:
      - NET_RAW          # required for ICMP ping
    restart: unless-stopped
```

```bash
docker compose -f remote-probe.compose.yml up -d
```

For a checker engine, swap the image/Dockerfile, drop `NET_RAW` and the UDP
ports, and use `CheckerEngine__PluginDirectory`.

## Example: systemd (no Docker)

```ini
# /etc/systemd/system/mone-probe.service
[Unit]
Description=Mone Probe Executor
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=/opt/mone/probe-executor/Mone.ProbeExecutor
WorkingDirectory=/opt/mone/probe-executor
Environment=ConnectionStrings__Postgres=Host=console.example.com;Port=5432;Database=mone;Username=mone;Password=CHANGEME
Environment=ConnectionStrings__Nats=nats://console.example.com:4222
Environment=Mone__Api__BaseUrl=https://console.example.com
Environment=Mone__Node__Token=CHANGEME
Environment=Mone__Node__Name=edge01-probe
Environment=ProbeExecutor__PluginDirectory=/opt/mone/probe-executor/plugins
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Prefer an `EnvironmentFile=` (mode `600`) over inline `Environment=` lines so
the Postgres password and node token are not world-readable in the unit file.

## Verifying the deployment

1. **It registers.** Start the executor and open **Settings → Nodes** in the
   dashboard. The new node appears with health **Online** within ~30s.
2. **Health transitions.** Stop the executor; the node goes **Stale** after 90s
   and **Offline** after 5 minutes. Restart it and it returns to **Online**.
3. **Binding routes correctly.** Bind a test assignment to the remote node and
   confirm results arrive (probe results / status changes) while other nodes no
   longer run that assignment.

## Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| Node never appears under Nodes | `Mone__Api__BaseUrl` unset or unreachable; check executor logs for the "registration disabled" / "registration failed" warning. |
| Node appears then goes Offline | Heartbeat can't reach the API (network/firewall), or `Mone__Node__Token` mismatch causing `401`. |
| Registration logs `401` | Token mismatch between API and executor `Mone__Node__Token`. |
| Bound assignment never runs | Plugin DLL missing on the remote host, or NATS/Postgres unreachable from it. |
| Two nodes for one host | A probe executor and checker engine on the same machine register separately by design — give them distinct `Mone__Node__Name` values. |
