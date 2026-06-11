# Deployment Guide

How to run Mone, from a local single-host stack to a hardened production
deployment. For the full list of environment variables see
[configuration.md](configuration.md); for adding executors on other hosts see
[remote-executors.md](remote-executors.md).

## Single-host (Docker Compose)

The bundled `docker-compose.yml` runs the whole platform — Postgres, NATS, and
all four services — on one machine. This is the fastest way to a working install
and is the recommended starting point.

```bash
git clone <repo-url> && cd Mone
docker compose up -d
```

Wait for every container to report healthy, then open
[http://localhost:8080](http://localhost:8080) and log in with the seeded admin
account:

| Field | Value |
|-------|-------|
| Email | `admin@mone.local` |
| Password | `Admin123!` |

Change this password immediately on any non-throwaway deployment.

### What the stack contains

| Container | Image | Ports | Persistent volume |
|-----------|-------|-------|-------------------|
| `postgres` | `timescale/timescaledb-ha:pg17` | 5432 | `pgdata` |
| `nats` | `nats:2.11-alpine` | 4222, 8222 | `natsdata` |
| `api` | built from `Mone.Api/Dockerfile` | 8080 | `plugins` |
| `probe-executor` | built from `Mone.ProbeExecutor/Dockerfile` | 162/udp, 514/udp | `plugins`, `probe-spool` |
| `checker-engine` | built from `Mone.CheckerEngine/Dockerfile` | — | `plugins` |
| `alert-engine` | built from `Mone.AlertEngine/Dockerfile` | — | `plugins` |

The `plugins` volume is shared so every service loads the same plugin DLLs. The
`probe-spool` volume holds the executor's cached config and any
not-yet-forwarded results — keep it persistent so nothing is lost across
restarts. The UDP ports (162 SNMP-trap, 514 syslog) and `NET_RAW` capability on
the probe executor support the passive/ICMP probes; drop them if you don't use
those plugins.

### Startup order

Compose health checks gate startup: the API waits for Postgres and NATS to be
healthy, then the executor/checker/alert services wait for the API. On first run
the API applies all EF Core migrations (with retry) and seeds the admin user
before it reports healthy.

## Production hardening

The compose defaults are tuned for local development. Before exposing Mone,
change at least these:

### 1. Secrets

| Setting | Default (dev) | Action |
|---------|---------------|--------|
| `POSTGRES_PASSWORD` | `mone_dev` | Set a strong password. It feeds both Postgres and every service's connection string. |
| `Jwt__Key` (`JWT_KEY`) | a known dev string | Set a unique random value ≥ 32 bytes. Rotating it invalidates all issued tokens. |
| `Mone__Node__Token` (`MONE_NODE_TOKEN`) | unset | Set a shared secret if you run any remote executors — see below. |
| Admin password | `Admin123!` | Change in the UI after first login. |

Provide secrets via an `.env` file (mode `600`, never committed) or your
orchestrator's secret store — not inline in the compose file.

### 2. Node channel security

If `Mone__Node__Token` is **unset on the API**, the node register/heartbeat
routes are open — fine on a trusted private network, not for an internet-facing
API. Set the same token on the API and on every executor. Full details in
[remote-executors.md](remote-executors.md#securing-the-node-channel).

### 3. TLS and reverse proxy

The API serves plain HTTP on 8080. In production put it behind a reverse proxy
(nginx, Caddy, Traefik, a cloud LB) that terminates TLS and forwards to 8080.
Use HTTPS URLs for `Mone__Api__BaseUrl` on remote nodes so their config pull and
heartbeat are encrypted.

### 4. Don't expose infrastructure ports

Postgres (5432) and NATS (4222/8222) are published to the host in the dev compose
for convenience. In production, remove those `ports:` mappings so only the
reverse proxy → API path is reachable from outside; the services reach Postgres
and NATS over the internal compose network.

## Authentication providers

Mone authenticates against its local Identity store by default and can
additionally federate to OIDC and/or LDAP. All are configured by environment
variables on the **API** (see [configuration.md](configuration.md)).

### OIDC (SSO)

```
Oidc__Enabled=true
Oidc__Authority=https://idp.example.com
Oidc__ClientId=mone
Oidc__ClientSecret=<secret>
Oidc__DisplayName=Company SSO   # label on the login button
```

### LDAP / Active Directory

```
Ldap__Enabled=true
Ldap__Host=ldap.example.com
Ldap__Port=389
Ldap__UseSsl=false
Ldap__BaseDn=dc=example,dc=com
Ldap__UserSearchFilter=(sAMAccountName={0})
Ldap__BindDn=cn=svc-mone,ou=svc,dc=example,dc=com
Ldap__BindPassword=<secret>
Ldap__EmailAttribute=mail
Ldap__DisplayNameAttribute=displayName
```

Leave a provider's `__Enabled` at `false` to keep it off; the local admin
account always works as a fallback.

## Scaling out

The pipeline is decoupled over NATS, so stages scale independently:

- **More probing capacity / different vantage points** — run extra Probe
  Executors on other hosts and bind assignments to them. They need no Postgres.
- **More checker throughput** — run extra Checker Engines; they share the NATS
  work queue. They need Postgres reachability.
- **Alerting** — typically one Alert Engine is enough; it consumes status changes
  and dispatches.

Binding work to specific nodes, plugin parity requirements, and node identity are
covered in [remote-executors.md](remote-executors.md).

## Backups

Everything durable is in Postgres and the NATS data volume:

- **Postgres (`pgdata`)** — the system of record: config, results, history,
  users. Back it up with `pg_dump` (or volume snapshots) on your normal schedule.
  This is the one you cannot lose.
- **NATS (`natsdata`)** — in-flight stream messages. Transient; a backup is not
  essential since the pipeline re-derives state, but snapshotting avoids
  redelivery gaps on restore.
- **`probe-spool`** — per-node cache and buffered results; recreated as the node
  runs. No backup needed, but keep it persistent so restarts don't drop buffered
  results.

## Upgrades

1. Back up Postgres first.
2. Pull/build the new images.
3. `docker compose up -d` — the API applies any new EF Core migrations on startup
   before becoming healthy.
4. If you run remote executors, update their images too and keep their plugin
   DLLs in sync with the central deployment.

Because migrations run automatically on API startup, roll the API as part of the
normal `up -d`; the other services have no schema of their own.

## Health and verification

- **Containers** — `docker compose ps` should show all services healthy. The
  service health checks use `pidof dotnet`; Postgres uses `pg_isready`; NATS
  exposes `/healthz` on 8222.
- **App** — the dashboard loads at `:8080` and the admin login works.
- **Pipeline** — create a host, assign a ping probe, and confirm results appear
  on the host page within a schedule interval. Then assign a checker and confirm
  a status is computed.
- **Nodes** — remote executors appear under **Settings → Nodes** within ~30s of
  starting.

## Local development (without the full stack)

Run only the infrastructure in Docker and the services from source:

```bash
docker compose up -d postgres nats

dotnet run --project src/Mone.Api            # applies migrations
dotnet run --project src/Mone.ProbeExecutor
dotnet run --project src/Mone.CheckerEngine
dotnet run --project src/Mone.AlertEngine

dotnet test Mone.slnx
```
