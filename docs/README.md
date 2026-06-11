# Mone Documentation

Documentation for the Mone monitoring platform. Start with the
[project README](../README.md) for a quick start, then dig in here.

| Doc | What it covers |
|-----|----------------|
| [architecture.md](architecture.md) | Design overview — services, data flow, NATS streams, the persistence model, and technology choices. Read this first to understand how Mone works. |
| [deployment.md](deployment.md) | Running Mone: single-host Docker Compose, production hardening, auth providers, scaling, backups, and upgrades. |
| [configuration.md](configuration.md) | Full environment-variable reference for every service. |
| [user-guide.md](user-guide.md) | Using the dashboard: hosts, groups, probe/checker assignments, notifications, and inheritance. |
| [remote-executors.md](remote-executors.md) | Deploying additional Probe Executors / Checker Engines on other hosts, node identity, and binding work to nodes. |

For the catalogue of bundled plugins and their parameters, see the
**Mone-Plugins** repository's `docs/` set.
