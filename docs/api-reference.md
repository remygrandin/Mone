# API Reference

Complete reference for the Mone HTTP API. All routes are served by the
**Mone.Api** service under the host's base address.

For how the permission gates in the **Auth** column are evaluated — scope, the
effective-level `MAX`, and the verb→level mapping — read
[authorization.md](authorization.md).

## Conventions

- **Base path** — every route below is relative to the API origin (e.g.
  `https://mone.example.com`).
- **Auth column** — what the endpoint requires:
  - `anonymous` — no token needed.
  - `authenticated` — a valid bearer token; no specific permission.
  - `node token` — the executor-node identity token (not a user); RBAC-exempt.
  - `Resource:Level` — the RBAC gate. When shown as `Resource (verb)`, the
    required level is inferred from the HTTP verb (`GET`/`HEAD`/`OPTIONS` →
    `View`; mutating verbs → `Manage`).
- **Scope** — for scopeable resources the check targets the entity named by the
  route id (host/group); collection routes with no id target `Global`. A request
  that passes the gate at the wrong scope receives **403**.
- **Auth scheme** — JWT bearer. Obtain a token from `POST /api/auth/login` (or an
  SSO/LDAP flow) and send it as `Authorization: Bearer <token>`.
- **Content type** — `application/json` for all request and response bodies.

### Common status codes

| Code | Meaning |
|------|---------|
| 200 / 201 / 204 | Success (OK / Created / No Content). |
| 400 | Malformed request or violated precondition (e.g. missing scope id). |
| 401 | Missing/invalid token. |
| 403 | Authenticated but insufficient effective permission for the target. |
| 404 | Entity not found. |
| 409 | Conflict — duplicate, or a protected invariant (assigned role, last-admin lockout). |

---

## Authentication

These endpoints are never RBAC-gated — you cannot require a permission to sign in.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/register` | anonymous | Create a local user. Body `{ email, password }`. Returns `{ id, email }` (201) or a validation problem. |
| POST | `/api/auth/login` | anonymous | Exchange `{ email, password }` for a JWT. Returns a token response, or 401 on bad credentials. |
| GET | `/api/auth/me` | authenticated | The current user `{ id, email }` from the token claims. |
| GET | `/api/auth/me/permissions` | authenticated | The caller's coarse per-resource capability map (`MAX` level held anywhere, ignoring scope). Returns `{ userId, capabilities: { Resource: Level } }`. Drives dashboard navigation. |
| GET | `/api/auth/providers` | anonymous | Enabled external auth providers (OIDC/LDAP), each `{ name, displayName, loginUrl }`. |
| POST | `/api/auth/ldap/login` | anonymous | LDAP credential login (when `Ldap:Enabled`). Returns a JWT. |
| GET | `/api/auth/oidc/login` | anonymous | Begins the OIDC redirect flow (when `Oidc:Enabled`). |
| GET | `/api/auth/oidc/callback` | anonymous | OIDC redirect callback; completes login and issues a JWT. |

---

## Identity & Access (IAM)

All IAM endpoints require **`Administration: Manage`** (the catalog is `View`),
which is a global-only resource — scoped assignments grant nothing here.

### Permission catalog

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/permissions/catalog` | `Administration:View` | Static description of every resource (`resource`, `description`, `appliesTo`, `scopeable`), plus the level and scope-type vocabularies. Source of truth for the role editor. |

### Roles

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/roles` | `Administration` (GET→View) | List roles with their permissions and assignment counts. |
| GET | `/api/roles/{id}` | `Administration` | One role by id, or 404. |
| POST | `/api/roles` | `Administration` (Manage) | Create a role. Body `{ name, description?, permissions: [{ resource, level }] }`. `None` levels and duplicate resources are dropped. 409 on duplicate name. |
| PUT | `/api/roles/{id}` | `Administration` (Manage) | Replace a role's name/description/permissions. 400 if the role is a system role; 409 on duplicate name. |
| DELETE | `/api/roles/{id}` | `Administration` (Manage) | Delete a role. 400 if system role; 409 if it still has assignments. |

### Users & assignments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/users` | `Administration` | List users with their role assignments (each `{ id, roleId, roleName, scopeType, scopeId, scopeName, createdAt }`). |
| POST | `/api/users/{id}/roles` | `Administration` (Manage) | Assign a role to a user. Body `{ roleId, scopeType, scopeId? }`. `Global` must omit `scopeId`; `Group`/`Tag` must supply a valid one. 409 on duplicate tuple. |
| DELETE | `/api/users/{id}/roles/{assignmentId}` | `Administration` (Manage) | Revoke an assignment. 409 if it is the last Global `Administration:Manage` grant (lockout guard). |

---

## Hosts

Gated by **`Hosts`**, scopeable. Item routes target the host id; the collection
targets `Global`.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/hosts` | `Hosts` (View) | List hosts. Optional `?tags=` filter. |
| GET | `/api/hosts/{id}` | `Hosts` (View) | One host. |
| POST | `/api/hosts` | `Hosts` (Manage) | Create a host. |
| PUT | `/api/hosts/{id}` | `Hosts` (Manage) | Update a host. |
| DELETE | `/api/hosts/{id}` | `Hosts` (Manage) | Delete a host. |

---

## Host groups

Gated by **`Groups`**, scopeable. Item routes target the group id.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/host-groups` | `Groups` (View) | List groups (with member and child counts). |
| GET | `/api/host-groups/{id}` | `Groups` (View) | One group with members and subgroups. |
| POST | `/api/host-groups` | `Groups` (Manage) | Create a group. |
| PUT | `/api/host-groups/{id}` | `Groups` (Manage) | Update a group (name, description, parent). |
| DELETE | `/api/host-groups/{id}` | `Groups` (Manage) | Delete a group. |
| POST | `/api/host-groups/{id}/members` | `Groups` (Manage) | Add a host to the group. |
| DELETE | `/api/host-groups/{id}/members/{hostId}` | `Groups` (Manage) | Remove a host from the group. |

---

## Tags

Gated by **`Tags`**, global-only.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/tags` | `Tags` (View) | List tags with host counts. |
| POST | `/api/tags` | `Tags` (Manage) | Create a tag. |
| DELETE | `/api/tags/{id}` | `Tags` (Manage) | Delete a tag. |

---

## Assignments (probes & checkers)

Gated by **`Assignments`**, scopeable. Routes under `/api/hosts/{hostId}` target
that host; routes under `/api/host-groups/{groupId}` target that group.

### Per-host probe assignments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/hosts/{hostId}/probes` | `Assignments` (View) | List a host's probe assignments. |
| POST | `/api/hosts/{hostId}/probes` | `Assignments` (Manage) | Add a probe assignment. |
| PUT | `/api/hosts/{hostId}/probes/{id}` | `Assignments` (Manage) | Update a probe assignment. |
| DELETE | `/api/hosts/{hostId}/probes/{id}` | `Assignments` (Manage) | Remove a probe assignment. |

### Per-host checker assignments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/hosts/{hostId}/checkers` | `Assignments` (View) | List a host's checker assignments. |
| POST | `/api/hosts/{hostId}/checkers` | `Assignments` (Manage) | Add a checker assignment. |
| PUT | `/api/hosts/{hostId}/checkers/{id}` | `Assignments` (Manage) | Update a checker assignment. |
| DELETE | `/api/hosts/{hostId}/checkers/{id}` | `Assignments` (Manage) | Remove a checker assignment. |

### Group-level assignments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/host-groups/{groupId}/probes` | `Assignments` (View) | List a group's probe assignments. |
| POST | `/api/host-groups/{groupId}/probes` | `Assignments` (Manage) | Add a group probe assignment. |
| PUT | `/api/host-groups/{groupId}/probes/{id}` | `Assignments` (Manage) | Update a group probe assignment. |
| DELETE | `/api/host-groups/{groupId}/probes/{id}` | `Assignments` (Manage) | Remove a group probe assignment. |
| GET | `/api/host-groups/{groupId}/checkers` | `Assignments` (View) | List a group's checker assignments. |
| POST | `/api/host-groups/{groupId}/checkers` | `Assignments` (Manage) | Add a group checker assignment. |
| PUT | `/api/host-groups/{groupId}/checkers/{id}` | `Assignments` (Manage) | Update a group checker assignment. |
| DELETE | `/api/host-groups/{groupId}/checkers/{id}` | `Assignments` (Manage) | Remove a group checker assignment. |

### Per-host overrides & manual triggers

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/hosts/{hostId}/overrides` | `Assignments` (View) | List a host's inherited-assignment overrides. |
| PUT | `/api/hosts/{hostId}/overrides/probes/{assignmentId}` | `Assignments` (Manage) | Override an inherited probe assignment for this host. |
| DELETE | `/api/hosts/{hostId}/overrides/probes/{assignmentId}` | `Assignments` (Manage) | Clear a probe override. |
| PUT | `/api/hosts/{hostId}/overrides/checkers/{assignmentId}` | `Assignments` (Manage) | Override an inherited checker assignment for this host. |
| DELETE | `/api/hosts/{hostId}/overrides/checkers/{assignmentId}` | `Assignments` (Manage) | Clear a checker override. |
| POST | `/api/hosts/{hostId}/trigger-probe` | `Assignments` (Manage) | Manually trigger a probe run for the host. |

---

## Monitoring data

Gated by **`Monitoring`**, scopeable. Routes under `/api/hosts/{hostId}` target
that host; the dashboard and global notification history target `Global`.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/dashboard/summary` | `Monitoring` (View) | Estate-wide rollup for the dashboard home. |
| GET | `/api/hosts/{hostId}/status/latest` | `Monitoring` (View) | The host's latest computed status. |
| GET | `/api/hosts/{hostId}/status/history` | `Monitoring` (View) | Status history. Optional `?from=&to=`. |
| GET | `/api/hosts/{hostId}/results` | `Monitoring` (View) | Probe results. Optional `?from=&to=&probeId=`. |
| GET | `/api/hosts/{hostId}/results/metric-keys` | `Monitoring` (View) | Distinct metric keys seen for the host. |
| GET | `/api/hosts/{hostId}/results/declared-metrics` | `Monitoring` (View) | Metrics declared by the host's effective probes (with display metadata). |
| GET | `/api/hosts/{hostId}/results/metrics/series` | `Monitoring` (View) | A downsampled metric series. `?key=` required; optional `?points=&from=&to=`. |
| GET | `/api/hosts/{hostId}/results/latest-per-probe` | `Monitoring` (View) | The latest result for each probe on the host. |
| GET | `/api/hosts/{hostId}/effective-assignments` | `Monitoring` (View) | The host's resolved (inherited + overridden) probe/checker set. |
| GET | `/api/notifications` | `Monitoring` (View) | Global notification dispatch history. |
| GET | `/api/hosts/{hostId}/notifications` | `Monitoring` (View) | Notification history for one host. |

> Note: `effective-assignments` and notification history are **read views** of
> assignment/notification data and are therefore gated by `Monitoring` (read),
> not `Assignments`/`Notifications` (which govern configuration).

---

## Notification configuration

Gated by **`Notifications`**, global-only.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/notifications/configs` | `Notifications` (View) | List notification channel configs. |
| GET | `/api/notifications/configs/{id}` | `Notifications` (View) | One channel config. |
| POST | `/api/notifications/configs` | `Notifications` (Manage) | Create a channel config. |
| PUT | `/api/notifications/configs/{id}` | `Notifications` (Manage) | Update a channel config. |
| DELETE | `/api/notifications/configs/{id}` | `Notifications` (Manage) | Delete a channel config. |

---

## Plugins

Gated by **`Plugins`**, global-only.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/plugin-repos` | `Plugins` (View) | List plugin repositories. |
| GET | `/api/plugin-repos/{id}` | `Plugins` (View) | One repository. |
| POST | `/api/plugin-repos` | `Plugins` (Manage) | Add a repository. |
| DELETE | `/api/plugin-repos/{id}` | `Plugins` (Manage) | Remove a repository. |
| POST | `/api/plugin-repos/{id}/sync` | `Plugins` (Manage) | Sync one repository's catalog. |
| POST | `/api/plugin-repos/sync-all` | `Plugins` (Manage) | Sync all repositories. |
| GET | `/api/plugins` | `Plugins` (View) | List available/installed plugins. |
| POST | `/api/plugins/install` | `Plugins` (Manage) | Install a plugin. |
| POST | `/api/plugins/uninstall` | `Plugins` (Manage) | Uninstall a plugin. |
| POST | `/api/plugins/reload` | `Plugins` (Manage) | Reload the plugin engine. |
| GET | `/api/plugins/{pluginId}/global-config` | `Plugins` (View) | Read a plugin's global configuration. |
| PUT | `/api/plugins/{pluginId}/global-config` | `Plugins` (Manage) | Upsert a plugin's global configuration. |
| GET | `/api/loaded-plugins` | `Plugins` (View) | List plugins currently loaded by the engine. |

---

## Executor nodes

The **node-facing** routes authenticate with a node token and are **RBAC-exempt**
(see [authorization.md](authorization.md#the-executor-node-token-exemption)). The
**administrative** routes are gated by **`ExecutorNodes`** (global-only).

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/executor-nodes/register` | node token | A node registers itself. |
| POST | `/api/executor-nodes/{id}/heartbeat` | node token | A node reports liveness. |
| GET | `/api/executor-nodes/{id}/probe-assignments` | node token | A node pulls the work bound to it. |
| GET | `/api/executor-nodes` | `ExecutorNodes` (View) | List registered nodes (admin view). |
| PUT | `/api/executor-nodes/{id}` | `ExecutorNodes` (Manage) | Rename a node. |
| DELETE | `/api/executor-nodes/{id}` | `ExecutorNodes` (Manage) | Remove a node. |

---

## System / housekeeping

Gated by **`System`**, global-only.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/housekeeping/db-size` | `System` (View) | Current database size breakdown. |
| POST | `/api/housekeeping/assess` | `System` (Manage) | Assess what a retention cleanup would remove. |
| POST | `/api/housekeeping/cleanup` | `System` (Manage) | Run a data-retention cleanup. |
