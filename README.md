# Mone

A plugin-first infrastructure monitoring platform built with .NET 10, NATS JetStream, and TimescaleDB.

## Architecture

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

  Data flow:
  Probes execute on schedule (or receive webhooks)
    -> publish to NATS "probes.results.*"
      -> Checker Engine evaluates results against thresholds
        -> publishes to NATS "status.changes.*"
          -> Alert Engine dispatches notifications (email, webhook, etc.)
```

**Services:**

| Service | Description | Plugins |
|---------|-------------|---------|
| **API** | REST API + Blazor WASM dashboard. Runs EF Core migrations on startup. | None |
| **Probe Executor** | Schedules and runs probe plugins on a per-host cadence via Quartz.NET. Also exposes a webhook endpoint for passive probes. | Ping, HTTPS, Webhook |
| **Checker Engine** | Consumes probe results from NATS and evaluates them against configured checker plugins. Publishes status changes. | ThresholdChecker |
| **Alert Engine** | Consumes status changes from NATS and dispatches notifications through notification plugins. | Email |

## Quick Start

**Prerequisites:** Docker and Docker Compose.

```bash
git clone <repo-url> && cd mone
docker compose up -d
```

Wait for all containers to become healthy, then open [http://localhost:8080](http://localhost:8080).

**Default credentials** (seeded on first startup):

| Field | Value |
|-------|-------|
| Email | `admin@mone.local` |
| Password | `Admin123!` |

> **Change the default password** after first login in a production deployment.

1. Log in with the default admin credentials.
2. Create a host to monitor.
3. Assign probes (ping, HTTPS) to the host.
4. Assign checkers and notification rules.

## Configuration

All configuration is via environment variables. The defaults in `docker-compose.yml` work for local development.

| Variable | Service | Default | Description |
|----------|---------|---------|-------------|
| `ConnectionStrings__Postgres` | All | `Host=postgres;...Password=mone_dev` | PostgreSQL/TimescaleDB connection string |
| `ConnectionStrings__Nats` | All | `nats://nats:4222` | NATS server URL |
| `Jwt__Key` | API | `ThisIsADevelopmentKeyThatIsAtLeast32BytesLong!` | JWT signing key (change in production) |
| `Jwt__Issuer` | API | `Mone` | JWT issuer claim |
| `Jwt__Audience` | API | `Mone` | JWT audience claim |
| `ProbeExecutor__PluginDirectory` | Probe Executor | `/app/plugins` | Path to probe plugin DLLs |
| `CheckerEngine__PluginDirectory` | Checker Engine | `/app/plugins` | Path to checker plugin DLLs |
| `AlertEngine__PluginDirectory` | Alert Engine | `/app/plugins` | Path to notification plugin DLLs |
| `POSTGRES_PASSWORD` | Postgres | `mone_dev` | Database password |

For the Email notification plugin, configure SMTP via checker/alert assignment config in the dashboard.

### Remote executors

You can run additional Probe Executors or Checker Engines on separate hosts and
bind specific assignments to them. See
[docs/remote-executors.md](docs/remote-executors.md) for node identity,
connection environment variables, the shared-token security model, and
docker-compose / systemd examples.

## Documentation

Full documentation lives in [docs/](docs/):

- [Architecture](docs/architecture.md) — services, data flow, messaging, and data model.
- [Deployment guide](docs/deployment.md) — running, hardening, scaling, backups.
- [Configuration reference](docs/configuration.md) — all environment variables.
- [User guide](docs/user-guide.md) — using the dashboard day to day.
- [Remote executors](docs/remote-executors.md) — running executors on other hosts.

## API Documentation

Interactive API docs are available at [http://localhost:8080/scalar/v1](http://localhost:8080/scalar/v1) when the API is running.

## Plugin System

Mone uses a plugin architecture with three plugin kinds:

- **Probe** (`IProbePlugin` / `IPassiveProbePlugin`) -- execute checks against hosts (active polling or passive webhook receivers).
- **Checker** (`ICheckerPlugin`) -- evaluate probe results and determine host status.
- **Notification** (`INotificationPlugin`) -- dispatch alerts when status changes (email, webhook, etc.).

Plugins are .NET class libraries that reference `Mone.Contracts` and are compiled to a `plugins/` directory. Each service loads plugins from its configured plugin directory at startup.

### Built-in Plugins

| Plugin | Kind | Description |
|--------|------|-------------|
| `Mone.Plugins.Ping` | Probe | ICMP ping check |
| `Mone.Plugins.Https` | Probe | HTTPS endpoint check with certificate validation |
| `Mone.Plugins.Webhook` | Probe (passive) | Receives external webhook payloads |
| `Mone.Plugins.ThresholdChecker` | Checker | Evaluates probe results against configurable thresholds |
| `Mone.Plugins.Email` | Notification | Sends alert emails via SMTP |

### Writing a Custom Plugin

1. Create a .NET class library targeting `net10.0`.
2. Reference the `Mone.Contracts` project.
3. Implement `IProbePlugin`, `ICheckerPlugin`, or `INotificationPlugin`.
4. Build and place the output DLL in the service's plugin directory.

## Development

**Prerequisites:** .NET 10 SDK, Docker (for Postgres and NATS).

```bash
# Start infrastructure
docker compose up -d postgres nats

# Run the API (applies migrations automatically)
dotnet run --project src/Mone.Api

# Run other services
dotnet run --project src/Mone.ProbeExecutor
dotnet run --project src/Mone.CheckerEngine
dotnet run --project src/Mone.AlertEngine

# Run tests
dotnet test Mone.slnx
```

The solution file is `Mone.slnx` (XML solution format, .NET 10 default).

## License

MIT
