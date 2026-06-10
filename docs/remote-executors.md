# Deploying Remote Probe Executors and Checker Engines

By default every Mone service runs on one host via `docker compose up`. You can
also run **additional** Probe Executors or Checker Engines on separate hosts —
to probe from a different network vantage point, spread checker load, or keep
running closer to the targets being monitored.

This guide covers how a remote executor connects back to the central console,
how it identifies itself, and how you bind specific work to it.

## How a remote executor connects back

A remote executor talks to the central deployment over these channels:

| Channel | Direction | Probe | Checker | Purpose |
|---------|-----------|:-----:|:-------:|---------|
| **API** (HTTP) | executor → API | ✓ | ✓ | Self-registration and 30s heartbeat so the node shows up under **Settings → Nodes** with live health. The probe executor also pulls its assignment config here. |
| **NATS JetStream** | bidirectional | ✓ | ✓ | Receives schedule/trigger messages and publishes probe results / status changes. |
| **PostgreSQL** | executor → DB | — | ✓ | Reads assignment config and writes results. **Checker engine only** — the probe executor no longer touches Postgres. |

> **Semi-autonomous probe executor:** the probe executor needs **no Postgres**.
> It pulls fully-resolved probe specs from the API (`/api/executor-nodes/{id}/probe-assignments`)
> and caches them on its spool volume, so it keeps running on last-known config
> when the API is down. Results publish over NATS; when NATS is unreachable they
> are written to a local SQLite spool and forwarded once it recovers — a row is
> deleted locally only after a confirmed publish, so nothing is lost. The console
> persists those results to Postgres. The **checker engine still connects directly
> to Postgres** — plan network access accordingly.

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
| `ConnectionStrings__Postgres` | checker | yes | PostgreSQL/TimescaleDB connection string, reachable from the remote host. **Checker only** — the probe executor does not use Postgres. |
| `ConnectionStrings__Nats` | both | yes | NATS server URL, e.g. `nats://console.example.com:4222`. |
| `Mone__Api__BaseUrl` | both | yes¹ | Base URL of the API, e.g. `https://console.example.com`. The probe executor pulls its config here; without it, registration/heartbeat are also disabled (the executor still runs on cached config but never appears under Nodes). |
| `Mone__Node__Token` | both | recommended | Shared secret sent as the `X-Node-Token` header on register/heartbeat and the config pull. Must match the API's `Mone__Node__Token`. If unset on the API, node routes are open. |
| `Mone__Node__Name` | both | recommended | Friendly node name shown in the dashboard. |
| `Mone__Node__Id` | both | optional | Override the node GUID. Leave unset to use the stable per-machine default. |
| `Mone__Node__Address` | both | optional | Advertised IP/hostname shown in the Nodes page (informational; does not affect connectivity). |
| `Mone__Node__SpoolPath` | probe | optional | Path to the probe executor's local SQLite spool (cached config + unforwarded results). Default `/app/data/spool.db`; mount a persistent volume there so the cache and any buffered results survive restarts. |
| `ProbeExecutor__PluginDirectory` | probe | yes | Path to probe plugin DLLs (default `/app/plugins`). |
| `CheckerEngine__PluginDirectory` | checker | yes | Path to checker plugin DLLs (default `plugins`). |

¹ Technically optional — the executor process still starts without it — but the
probe executor pulls its assignment config from the API, so without
`Mone__Api__BaseUrl` it has only its cached snapshot to work from (nothing at all
on a first run). The node also never registers, so you lose health visibility and
cannot bind assignments to it. Always set it for remote nodes.

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
console. The probe executor needs no Postgres — create a `.env` next to this file
with `MONE_NODE_TOKEN` and `PROBE_NODE_NAME`, then:

```yaml
# remote-probe.compose.yml
services:
  probe-executor:
    image: mone/probe-executor:latest   # or build from src/Mone.ProbeExecutor/Dockerfile
    environment:
      ConnectionStrings__Nats: nats://console.example.com:4222
      Mone__Api__BaseUrl: https://console.example.com
      Mone__Node__Token: ${MONE_NODE_TOKEN}
      Mone__Node__Name: ${PROBE_NODE_NAME:-edge01-probe}
      Mone__Node__SpoolPath: /app/data/spool.db
      ProbeExecutor__PluginDirectory: /app/plugins
    volumes:
      - ./plugins:/app/plugins
      - probe-spool:/app/data    # persists cached config + unforwarded results
    cap_add:
      - NET_RAW          # required for ICMP ping
    restart: unless-stopped

volumes:
  probe-spool:
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
Environment=ConnectionStrings__Nats=nats://console.example.com:4222
Environment=Mone__Api__BaseUrl=https://console.example.com
Environment=Mone__Node__Token=CHANGEME
Environment=Mone__Node__Name=edge01-probe
Environment=Mone__Node__SpoolPath=/var/lib/mone/probe-spool.db
Environment=ProbeExecutor__PluginDirectory=/opt/mone/probe-executor/plugins
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

The probe executor needs no Postgres; point `Mone__Node__SpoolPath` at a
persistent, writable path (e.g. under `/var/lib/mone`) so cached config and
buffered results survive restarts. Prefer an `EnvironmentFile=` (mode `600`)
over inline `Environment=` lines so the node token is not world-readable in the
unit file. (A remote **checker engine** unit still needs
`ConnectionStrings__Postgres`.)

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
| Bound assignment never runs | Plugin DLL missing on the remote host; or (probe) the API was never reachable so no config was ever cached; or (checker) NATS/Postgres unreachable from it. |
| Probe results delayed then arrive in a burst | NATS was unreachable, so results were spooled locally and forwarded once it recovered — expected store-and-forward behaviour. Check the executor logs for "spooling result locally" / "Forwarded N spooled result(s)". |
| Two nodes for one host | A probe executor and checker engine on the same machine register separately by design — give them distinct `Mone__Node__Name` values. |
