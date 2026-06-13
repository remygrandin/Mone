# Authorization & Access Control

Mone uses role-based access control (RBAC) with **scoped assignments**. This
document explains the model end to end: what a permission is, the resources it
applies to, how scope narrows a grant to part of your estate, how the effective
level is computed at request time, and the safeguards that keep an
administrator from locking everyone out.

For the wire-level shape of every IAM endpoint, see
[api-reference.md](api-reference.md). For day-to-day UI usage, see the
[user-guide.md](user-guide.md).

## Concepts at a glance

| Concept | What it is |
|---------|-----------|
| **User** | An identity that can authenticate (local password, LDAP, or OIDC). Users hold no permissions on their own. |
| **Role** | A named bundle of permissions. A role grants a level on each of zero or more resources. |
| **Permission** | A `(resource, level)` pair inside a role — e.g. `Hosts: Manage`. |
| **Assignment** | A link of *user → role* carrying a **scope**. This is where a role becomes effective, and where it is narrowed to a slice of the estate. |
| **Scope** | The reach of an assignment: `Global` (everything), a `Group` (the group and everything beneath it), or a `Tag` (every host carrying that tag). |

The key design choice: **scope lives on the assignment, not on the role.** The
same role (say, "Operator") can be handed to one user globally and to another
user scoped to a single group, without duplicating the role.

## Permission levels

Levels are ordered. A higher level includes everything the lower one allows.

| Level | Value | Meaning |
|-------|-------|---------|
| `None` | 0 | No access. (Not stored — the absence of a grant.) |
| `View` | 1 | Read-only. Corresponds to safe HTTP verbs (`GET`, `HEAD`, `OPTIONS`). |
| `Manage` | 2 | Read and write. Corresponds to mutating verbs (`POST`, `PUT`, `PATCH`, `DELETE`). |

When an endpoint does not declare an explicit level, the required level is
**inferred from the HTTP verb**: read verbs require `View`, everything else
requires `Manage` (`RequirePermissionFilter.RequiredLevelForMethod`).

## Resources

A resource is a coarse functional area of the product. There are ten. Resources
marked **scopeable** can be narrowed by a Group or Tag assignment; the rest are
global-only — a scoped assignment grants nothing for them.

| Resource | Scopeable | Applies to |
|----------|:---------:|-----------|
| **Hosts** | yes | Creating, viewing, editing and deleting hosts (`/api/hosts`). |
| **Monitoring** | yes | Live monitoring data: status, probe results, metrics, the dashboard summary, effective assignments, and notification history for hosts. |
| **Assignments** | yes | Probe & checker assignments: direct host assignments, group-level assignments, per-host overrides, and manual probe triggers. |
| **Groups** | yes | Creating, editing, deleting host groups and managing their membership. |
| **Tags** | no | Defining tags and applying them to hosts. Managed globally. |
| **Notifications** | no | Notification channel configuration. Managed globally. |
| **Plugins** | no | Plugin repositories, installed/loaded plugins, and global plugin configuration. Managed globally. |
| **ExecutorNodes** | no | Administration of remote executor nodes. Managed globally. |
| **System** | no | Housekeeping and data-retention operations. Managed globally. |
| **Administration** | no | Identity & access management: roles, users, and role assignments. Managed globally. |

This table is the authoritative `applies to` mapping. It is served live at
`GET /api/permissions/catalog` (the source of truth is
`PermissionEndpoints.Catalog`) and drives the role editor in the dashboard, so
the UI always reflects the deployed build.

### Why Monitoring is separate from Hosts

`Hosts` governs the inventory record (address, name, deletion). `Monitoring`
governs the *observed data* about a host (its current status, history, and
metrics). Splitting them lets you grant a read-only on-call user `Monitoring:
View` across the estate without also handing them the ability to see or edit the
host inventory, and conversely lets an inventory manager edit hosts without
implying access to every metric.

## Scope

Scope answers "which entities does this assignment cover?" There are three
kinds (`ScopeType`):

- **Global** — covers every entity. `ScopeId` must be null.
- **Group** — covers the named group, all of its descendant subgroups, and
  every host in that subtree. `ScopeId` is the group id.
- **Tag** — covers every host carrying the tag. `ScopeId` is the tag id.

Scope is evaluated by **walking upward** from the target at request time. For a
host target, an assignment covers it when:

- the assignment is `Global`, **or**
- the assignment is a `Group` scope on any group in the host's **group closure**
  (every group the host belongs to directly or transitively, walking up the
  `ParentGroupId` chain), **or**
- the assignment is a `Tag` scope on any tag the host carries.

For a group target, a `Group` assignment covers it when the scoped group is the
target group **or any of its ancestors**. (A grant high in the tree flows
downward to subgroups; a grant on a subgroup does not flow up to its parent.)

The upward walks live in `ScopeResolver`
(`GetHostGroupClosureAsync`, `GetHostTagIdsAsync`,
`GetGroupAncestorsAndSelfAsync`). The downward walks
(`GetGroupSubtreeAsync`, `GetHostsInGroupSubtreeAsync`) exist only to let the
IAM UI preview how far a grant reaches — they are not on the authorization hot
path.

## Effective level: how a request is decided

Every gated endpoint runs `RequirePermissionFilter` after authentication. The
filter:

1. Reads the caller's user id from the `NameIdentifier` claim (401 if missing).
2. Determines the **required level** — the endpoint's explicit level, or the
   verb-inferred default.
3. Determines the **scope target** from the route (see below).
4. Asks `PermissionService.GetEffectiveLevelAsync` for the caller's effective
   level against that target.
5. Allows the request if `effective >= required`; otherwise returns **403** with
   a ProblemDetails body naming the missing `resource:level`.

The effective level is the **maximum** level across *every assignment whose
scope covers the target*:

```
effective(user, resource, target) =
    MAX over assignments a of user,
            permissions p of a.role
        where p.resource == resource
          and p.level   != None
          and scopeCovers(a, target)
        of p.level
```

Concretely (`PermissionService.GetEffectiveLevelAsync`):

- Global-scoped grants always count. If any Global grant is `Manage`, that is the
  ceiling and resolution short-circuits.
- For a `Global` target (a non-scopeable resource, or a collection route with no
  id), only Global grants count — scoped grants are ignored.
- For a `Host` or `Group` target, scoped grants are checked against the covering
  groups/tags computed by `ScopeResolver`.

Because it is a `MAX`, grants are **purely additive** — there is no "deny" rule.
Removing access means removing or narrowing an assignment.

### How the scope target is read from the route

`RequirePermissionFilter.ResolveTarget` maps the resource + route values to a
target:

| Resource | Route value used | Target |
|----------|------------------|--------|
| Hosts, Monitoring | `hostId`, else `id` | that host; else Global |
| Groups | `groupId`, else `id` | that group; else Global |
| Assignments | `hostId` → host; `groupId` → group | else Global |
| all others | — | always Global |

So `GET /api/hosts/{id}` is checked against that specific host, while
`GET /api/hosts` (the collection) is a Global target. This is deliberate: **list
endpoints return all rows the query produces**, and per-item actions enforce
scope — an out-of-scope item write yields 403, not a filtered-away 404.

## Roles

A role is `{ name, description, isSystem, permissions[] }`. Creating or editing a
role is a `Manage` action on `Administration`. Rules enforced by
`RoleEndpoints`:

- Names are unique (409 on collision, create or rename).
- Permissions are normalized on save: `None` levels are dropped and duplicate
  resources are de-duplicated, so a role stores at most one row per resource.
- **System roles (`isSystem = true`) cannot be modified or deleted** (400). They
  are seeded by the platform.

### The SuperAdmin system role

On startup `RbacSeeder.EnsureSuperAdminRoleAsync` creates (and reconciles, every
boot) a system role named **SuperAdmin** holding `Manage` on *every* resource.
A **Global** assignment of SuperAdmin is unrestricted access to the whole
product. The seeded bootstrap admin user receives exactly this assignment
(`EnsureGlobalAssignmentAsync`), so a fresh install always has one working
administrator. Both seeding steps are idempotent.

## Assignments

An assignment links a user to a role at a scope. Creating one is a `Manage`
action on `Administration` (`UserAdminEndpoints`). Validation:

- The user and role must exist.
- `Global` scope must **not** carry a `ScopeId`; `Group`/`Tag` scope **must**
  carry a valid existing `ScopeId` (400 otherwise).
- The exact `(user, role, scopeType, scopeId)` tuple must be unique (409 on
  duplicate).

## Lockout protection

Revoking an assignment is a `Manage` action on `Administration`. Before
removing one, `IsLastGlobalAdminAssignmentAsync` checks whether it is the **last
remaining Global assignment that grants `Administration: Manage`** across all
users. If it is, the revoke is refused with **409** and a message telling you to
grant it to someone else first.

This guarantees there is always at least one identity able to manage IAM. Note
the guard is specifically about *Global* `Administration: Manage` grants — the
SuperAdmin role is a system role that cannot be edited or deleted, so the only
way to strand a deployment would be to revoke its last global holder, which this
check prevents.

## The executor-node token exemption

Remote executor nodes are **not** users and do not carry RBAC permissions. The
node-facing endpoints under `/api/executor-nodes` — `register`,
`{id}/heartbeat`, and `{id}/probe-assignments` — authenticate with a node token
and are intentionally **exempt** from the RBAC filter. Only the *administrative*
surface for nodes (listing, renaming, deleting via the second `/api/executor-nodes`
group) is gated by `ExecutorNodes`. Likewise, the authentication endpoints
(`/api/auth/...`) are not RBAC-gated — you cannot require a permission to log
in. `GET /api/auth/me/permissions` requires only authentication, so the
dashboard can render navigation for a brand-new user with zero grants.

## Enforcement summary

- **401 (Unauthorized)** — no valid token / no user id claim. Owned by the auth
  middleware, before the RBAC filter.
- **403 (Forbidden)** — authenticated but the effective level is below what the
  action requires. Body is ProblemDetails naming `resource:level`.
- **409 (Conflict)** — a write that would violate an invariant (duplicate role
  name, duplicate assignment, deleting an assigned role, or the last-admin
  lockout guard).

All enforcement is **server-side**. The dashboard caches a coarse per-resource
capability map (`MAX` level held anywhere, ignoring scope) purely to hide
navigation and pages the user cannot use; it is a convenience, never the
security boundary. Per-item scope is always re-checked on the API.
