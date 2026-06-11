# Configuration Reference

All Mone services are configured through environment variables (ASP.NET Core
configuration). Config keys use `:` internally (`Mone:Node:Name`); as environment
variables the separator is `__` (double underscore): `Mone__Node__Name`.

The defaults in `docker-compose.yml` are tuned for local development. The
`${VAR:-default}` entries in that file are the compose-level knobs you set in an
`.env`; the `Foo__Bar` entries are the actual app settings they map to.

## Shared (all services)

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__Postgres` | `Host=postgres;Port=5432;Database=mone;Username=postgres;Password=mone_dev` | PostgreSQL/TimescaleDB connection string. Required by API, Checker Engine, Alert Engine. **Not used by the Probe Executor.** |
| `ConnectionStrings__Nats` | `nats://nats:4222` | NATS server URL. Required by every service. |

## API (`Mone.Api`)

Binds HTTP on **8080**. Owns the database (runs migrations, seeds admin) and
authentication.

| Variable | Default | Description |
|----------|---------|-------------|
| `Jwt__Key` | dev key (≥32 bytes) | JWT signing key. **Set a unique random value in production.** Changing it invalidates all issued tokens. |
| `Jwt__Issuer` | `Mone` | JWT issuer claim. |
| `Jwt__Audience` | `Mone` | JWT audience claim. |
| `Api__PluginDirectory` | `/app/plugins` | Path the API loads plugin metadata from. |
| `Mone__Node__Token` | unset | Shared secret gating node register/heartbeat. If unset, node routes are open. Set it (and match on executors) for any internet-facing API. |

### OIDC (optional SSO)

| Variable | Default | Description |
|----------|---------|-------------|
| `Oidc__Enabled` | `false` | Enable OIDC login. |
| `Oidc__Authority` | — | Identity provider authority URL. |
| `Oidc__ClientId` | — | OIDC client id. |
| `Oidc__ClientSecret` | — | OIDC client secret. |
| `Oidc__DisplayName` | `SSO` | Label on the login button. |

### LDAP (optional)

| Variable | Default | Description |
|----------|---------|-------------|
| `Ldap__Enabled` | `false` | Enable LDAP login. |
| `Ldap__Host` | — | LDAP server host. |
| `Ldap__Port` | `389` | LDAP port. |
| `Ldap__UseSsl` | `false` | Use LDAPS. |
| `Ldap__BaseDn` | — | Base DN for user search. |
| `Ldap__UserSearchFilter` | — | User search filter, e.g. `(sAMAccountName={0})`. |
| `Ldap__BindDn` | — | Service-account DN for binding. |
| `Ldap__BindPassword` | — | Service-account password. |
| `Ldap__EmailAttribute` | `mail` | Attribute mapped to email. |
| `Ldap__DisplayNameAttribute` | `displayName` | Attribute mapped to display name. |

## Probe Executor (`Mone.ProbeExecutor`)

No Postgres. Pulls config from the API, caches it locally, and spools results to
SQLite when NATS is down. UDP 162 (SNMP-trap) and 514 (syslog) for passive
probes; needs `NET_RAW` for ICMP ping.

| Variable | Default | Description |
|----------|---------|-------------|
| `ProbeExecutor__PluginDirectory` | `/app/plugins` | Path to probe plugin DLLs. |
| `Mone__Api__BaseUrl` | — | API base URL. Used to pull config and register/heartbeat. Without it the node never appears under **Nodes** and runs only on cached config. Always set for remote nodes. |
| `Mone__Node__Name` | `<machine>-probe` | Friendly node name shown in the dashboard. |
| `Mone__Node__Token` | unset | Shared secret; must match the API's `Mone__Node__Token`. |
| `Mone__Node__Id` | stable per-machine GUID | Override the node GUID. Leave unset for the deterministic default. |
| `Mone__Node__Address` | — | Advertised IP/hostname shown in **Nodes** (informational). |
| `Mone__Node__SpoolPath` | `/app/data/spool.db` | Local SQLite spool (cached config + unforwarded results). Mount a persistent volume here. |

## Checker Engine (`Mone.CheckerEngine`)

Background worker. Consumes probe results, evaluates checkers, publishes status
changes. Reads/writes Postgres directly.

| Variable | Default | Description |
|----------|---------|-------------|
| `CheckerEngine__PluginDirectory` | `/app/plugins` | Path to checker plugin DLLs. |
| `Mone__Api__BaseUrl` | — | API base URL for registration/heartbeat (so it appears under **Nodes**). |
| `Mone__Node__Name` | `<machine>-checker` | Friendly node name. |
| `Mone__Node__Token` | unset | Shared secret; must match the API. |
| `Mone__Node__Id` | stable per-machine GUID | Override the node GUID. |
| `Mone__Node__Address` | — | Advertised address shown in **Nodes**. |

## Alert Engine (`Mone.AlertEngine`)

Background worker. Consumes status changes and dispatches notifications. Reads
Postgres for notification config.

| Variable | Default | Description |
|----------|---------|-------------|
| `AlertEngine__PluginDirectory` | `/app/plugins` | Path to notification plugin DLLs. |

## Infrastructure containers

| Variable | Service | Default | Description |
|----------|---------|---------|-------------|
| `POSTGRES_PASSWORD` | postgres | `mone_dev` | Database password. Feeds both the Postgres container and every service's `ConnectionStrings__Postgres`. **Change in production.** |
| `POSTGRES_DB` | postgres | `mone` | Database name. |

## Compose `.env` knobs

The dev `docker-compose.yml` reads these from an adjacent `.env`:

| `.env` variable | Maps to | Notes |
|-----------------|---------|-------|
| `POSTGRES_PASSWORD` | DB password + all connection strings | Set a strong value. |
| `JWT_KEY` | `Jwt__Key` (API) | Random, ≥32 bytes. |
| `MONE_NODE_TOKEN` | `Mone__Node__Token` (executors) | Shared node secret. |
| `PROBE_NODE_NAME` | `Mone__Node__Name` (probe executor) | Friendly name. |
| `CHECKER_NODE_NAME` | `Mone__Node__Name` (checker engine) | Friendly name. |
| `OIDC_*`, `LDAP_*` | corresponding `Oidc__*` / `Ldap__*` | Auth provider settings. |

Store the `.env` with mode `600` and never commit it.

## Plugin configuration

Per-assignment plugin settings (ping timeout, HTTPS URL, SMTP server, threshold
bands, …) are **not** environment variables — they are configured per assignment
in the dashboard, driven by each plugin's `ConfigManifest`. See the user guide
([user-guide.md](user-guide.md)) for assigning them, and the **Mone-Plugins**
repository's `docs/` for each plugin's fields.
