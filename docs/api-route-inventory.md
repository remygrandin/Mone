# Mone.Api Route Inventory

This document is the canonical, git-tracked inventory of every HTTP route exposed
by `Mone.Api` and which `Mone.Dashboard` `ApiClient` method (if any) consumes it.
It is the falsifiable artifact behind the M006/S05 claim that resource
naming/pluralization/verb patterns are consistent and that the dashboard maps
cleanly onto the API. The paired test `api-route-inventory.test.mjs` keeps this
file honest: it parses the real route strings from
`Mone/src/Mone.Api/Endpoints/*.cs` and `Mone/src/Mone.Dashboard/Services/ApiClient.cs`
and fails if a `.cs` endpoint family is missing here, if a dashboard call has no
matching server route, or if a route marked `action` is not listed in the
accepted convention below.

Ground truth: route strings come from `MapGroup`/`MapGet`/`MapPost`/`MapPut`/`MapDelete`
declarations in the endpoint files and the `api/...` route literals in `ApiClient.cs`.

## Route Inventory

| Method | Path | File | Style | ApiClient consumer |
|--------|------|------|-------|--------------------|
| PUT | /api/hosts/{hostId:guid}/overrides/probes/{assignmentId:guid} | AssignmentOverrideEndpoints.cs | resource | UpsertProbeOverrideAsync |
| DELETE | /api/hosts/{hostId:guid}/overrides/probes/{assignmentId:guid} | AssignmentOverrideEndpoints.cs | resource | DeleteProbeOverrideAsync |
| PUT | /api/hosts/{hostId:guid}/overrides/checkers/{assignmentId:guid} | AssignmentOverrideEndpoints.cs | resource | UpsertCheckerOverrideAsync |
| DELETE | /api/hosts/{hostId:guid}/overrides/checkers/{assignmentId:guid} | AssignmentOverrideEndpoints.cs | resource | DeleteCheckerOverrideAsync |
| GET | /api/hosts/{hostId:guid}/overrides | AssignmentOverrideEndpoints.cs | resource | GetOverridesAsync |
| POST | /api/auth/register | AuthEndpoints.cs | action | - |
| POST | /api/auth/login | AuthEndpoints.cs | action | - |
| GET | /api/auth/me | AuthEndpoints.cs | resource | - |
| GET | /api/auth/providers | AuthProviderEndpoints.cs | resource | - |
| GET | /api/hosts/{hostId:guid}/checkers | CheckerAssignmentEndpoints.cs | resource | GetCheckerAssignmentsAsync |
| POST | /api/hosts/{hostId:guid}/checkers | CheckerAssignmentEndpoints.cs | resource | CreateCheckerAssignmentAsync |
| PUT | /api/hosts/{hostId:guid}/checkers/{id:guid} | CheckerAssignmentEndpoints.cs | resource | UpdateCheckerAssignmentAsync |
| DELETE | /api/hosts/{hostId:guid}/checkers/{id:guid} | CheckerAssignmentEndpoints.cs | resource | DeleteCheckerAssignmentAsync |
| GET | /api/dashboard/summary | DashboardEndpoints.cs | resource | GetDashboardSummaryAsync |
| GET | /api/hosts/{hostId:guid}/effective-assignments | EffectiveAssignmentEndpoints.cs | resource | GetEffectiveAssignmentsAsync |
| POST | /api/executor-nodes/register | ExecutorNodeEndpoints.cs | action | - |
| POST | /api/executor-nodes/{id:guid}/heartbeat | ExecutorNodeEndpoints.cs | action | - |
| GET | /api/executor-nodes/{id:guid}/probe-assignments | ExecutorNodeEndpoints.cs | resource | - |
| GET | /api/executor-nodes | ExecutorNodeEndpoints.cs | resource | GetExecutorNodesAsync |
| PUT | /api/executor-nodes/{id:guid} | ExecutorNodeEndpoints.cs | resource | RenameExecutorNodeAsync |
| DELETE | /api/executor-nodes/{id:guid} | ExecutorNodeEndpoints.cs | resource | DeleteExecutorNodeAsync |
| GET | /api/host-groups/{groupId:guid}/checkers | GroupCheckerAssignmentEndpoints.cs | resource | - |
| POST | /api/host-groups/{groupId:guid}/checkers | GroupCheckerAssignmentEndpoints.cs | resource | - |
| PUT | /api/host-groups/{groupId:guid}/checkers/{id:guid} | GroupCheckerAssignmentEndpoints.cs | resource | - |
| DELETE | /api/host-groups/{groupId:guid}/checkers/{id:guid} | GroupCheckerAssignmentEndpoints.cs | resource | - |
| GET | /api/host-groups/{groupId:guid}/probes | GroupProbeAssignmentEndpoints.cs | resource | - |
| POST | /api/host-groups/{groupId:guid}/probes | GroupProbeAssignmentEndpoints.cs | resource | - |
| PUT | /api/host-groups/{groupId:guid}/probes/{id:guid} | GroupProbeAssignmentEndpoints.cs | resource | - |
| DELETE | /api/host-groups/{groupId:guid}/probes/{id:guid} | GroupProbeAssignmentEndpoints.cs | resource | - |
| GET | /api/hosts | HostEndpoints.cs | resource | GetHostsAsync |
| GET | /api/hosts/{id:guid} | HostEndpoints.cs | resource | GetHostAsync |
| POST | /api/hosts | HostEndpoints.cs | resource | CreateHostAsync |
| PUT | /api/hosts/{id:guid} | HostEndpoints.cs | resource | UpdateHostAsync |
| DELETE | /api/hosts/{id:guid} | HostEndpoints.cs | resource | DeleteHostAsync |
| GET | /api/host-groups | HostGroupEndpoints.cs | resource | GetHostGroupsAsync |
| GET | /api/host-groups/{id:guid} | HostGroupEndpoints.cs | resource | GetHostGroupAsync |
| POST | /api/host-groups | HostGroupEndpoints.cs | resource | CreateHostGroupAsync |
| PUT | /api/host-groups/{id:guid} | HostGroupEndpoints.cs | resource | UpdateHostGroupAsync |
| DELETE | /api/host-groups/{id:guid} | HostGroupEndpoints.cs | resource | DeleteHostGroupAsync |
| POST | /api/host-groups/{id:guid}/members | HostGroupEndpoints.cs | resource | AddGroupMemberAsync |
| DELETE | /api/host-groups/{id:guid}/members/{hostId:guid} | HostGroupEndpoints.cs | resource | RemoveGroupMemberAsync |
| GET | /api/housekeeping/db-size | HousekeepingEndpoints.cs | action | GetDbSizeAsync |
| POST | /api/housekeeping/assess | HousekeepingEndpoints.cs | action | AssessHousekeepingAsync |
| POST | /api/housekeeping/cleanup | HousekeepingEndpoints.cs | action | CleanupAsync |
| POST | /api/auth/ldap/login | LdapAuthEndpoints.cs | action | - |
| GET | /api/loaded-plugins | LoadedPluginEndpoints.cs | resource | GetLoadedPluginsAsync |
| GET | /api/notifications | NotificationAuditEndpoints.cs | resource | - |
| GET | /api/hosts/{hostId:guid}/notifications | NotificationAuditEndpoints.cs | resource | - |
| GET | /api/notifications/configs | NotificationConfigEndpoints.cs | resource | GetNotificationConfigsAsync |
| GET | /api/notifications/configs/{id:guid} | NotificationConfigEndpoints.cs | resource | - |
| POST | /api/notifications/configs | NotificationConfigEndpoints.cs | resource | CreateNotificationConfigAsync |
| PUT | /api/notifications/configs/{id:guid} | NotificationConfigEndpoints.cs | resource | UpdateNotificationConfigAsync |
| DELETE | /api/notifications/configs/{id:guid} | NotificationConfigEndpoints.cs | resource | DeleteNotificationConfigAsync |
| GET | /api/auth/oidc/login | OidcAuthEndpoints.cs | action | - |
| GET | /api/auth/oidc/callback | OidcAuthEndpoints.cs | action | - |
| GET | /api/auth/me/permissions | PermissionEndpoints.cs | resource | GetMyPermissionsAsync |
| GET | /api/permissions/catalog | PermissionEndpoints.cs | resource | GetPermissionCatalogAsync |
| GET | /api/plugins/{pluginId}/global-config | PluginGlobalConfigEndpoints.cs | resource | - |
| PUT | /api/plugins/{pluginId}/global-config | PluginGlobalConfigEndpoints.cs | resource | - |
| POST | /api/plugin-repos | PluginRepositoryEndpoints.cs | resource | AddPluginRepositoryAsync |
| GET | /api/plugin-repos | PluginRepositoryEndpoints.cs | resource | GetPluginRepositoriesAsync |
| GET | /api/plugin-repos/{id:guid} | PluginRepositoryEndpoints.cs | resource | - |
| DELETE | /api/plugin-repos/{id:guid} | PluginRepositoryEndpoints.cs | resource | DeletePluginRepositoryAsync |
| POST | /api/plugin-repos/{id:guid}/sync | PluginRepositoryEndpoints.cs | action | SyncPluginRepositoryAsync |
| POST | /api/plugin-repos/sync-all | PluginRepositoryEndpoints.cs | action | SyncAllPluginRepositoriesAsync |
| GET | /api/plugins | PluginRepositoryEndpoints.cs | resource | GetPluginCatalogAsync |
| POST | /api/plugins/install | PluginRepositoryEndpoints.cs | action | InstallPluginAsync |
| POST | /api/plugins/uninstall | PluginRepositoryEndpoints.cs | action | UninstallPluginAsync |
| POST | /api/plugins/reload | PluginRepositoryEndpoints.cs | action | ReloadPluginsAsync |
| GET | /api/hosts/{hostId:guid}/probes | ProbeAssignmentEndpoints.cs | resource | GetProbeAssignmentsAsync |
| POST | /api/hosts/{hostId:guid}/probes | ProbeAssignmentEndpoints.cs | resource | CreateProbeAssignmentAsync |
| PUT | /api/hosts/{hostId:guid}/probes/{id:guid} | ProbeAssignmentEndpoints.cs | resource | UpdateProbeAssignmentAsync |
| DELETE | /api/hosts/{hostId:guid}/probes/{id:guid} | ProbeAssignmentEndpoints.cs | resource | DeleteProbeAssignmentAsync |
| GET | /api/hosts/{hostId:guid}/results | ProbeResultEndpoints.cs | resource | GetProbeResultsAsync |
| GET | /api/hosts/{hostId:guid}/results/metric-keys | ProbeResultEndpoints.cs | resource | GetMetricKeysAsync |
| GET | /api/hosts/{hostId:guid}/results/declared-metrics | ProbeResultEndpoints.cs | resource | GetDeclaredMetricsAsync |
| GET | /api/hosts/{hostId:guid}/results/metrics/series | ProbeResultEndpoints.cs | resource | GetMetricSeriesAsync |
| GET | /api/hosts/{hostId:guid}/results/latest-per-probe | ProbeResultEndpoints.cs | resource | GetLatestProbeResultsPerProbeAsync |
| POST | /api/hosts/{hostId:guid}/trigger-probe | ProbeTriggerEndpoints.cs | action | TriggerProbeAsync |
| GET | /api/roles | RoleEndpoints.cs | resource | GetRolesAsync |
| GET | /api/roles/{id:guid} | RoleEndpoints.cs | resource | - |
| POST | /api/roles | RoleEndpoints.cs | resource | CreateRoleAsync |
| PUT | /api/roles/{id:guid} | RoleEndpoints.cs | resource | UpdateRoleAsync |
| DELETE | /api/roles/{id:guid} | RoleEndpoints.cs | resource | DeleteRoleAsync |
| GET | /api/hosts/{hostId:guid}/status/latest | StatusEndpoints.cs | resource | GetLatestStatusAsync |
| GET | /api/hosts/{hostId:guid}/status/history | StatusEndpoints.cs | resource | GetStatusHistoryAsync |
| GET | /api/tags | TagEndpoints.cs | resource | GetTagsAsync |
| POST | /api/tags | TagEndpoints.cs | resource | - |
| DELETE | /api/tags/{id:guid} | TagEndpoints.cs | resource | - |
| GET | /api/users | UserAdminEndpoints.cs | resource | GetUsersAsync |
| POST | /api/users/{id}/roles | UserAdminEndpoints.cs | resource | AssignRoleAsync |
| DELETE | /api/users/{id}/roles/{assignmentId:guid} | UserAdminEndpoints.cs | resource | RevokeRoleAsync |

## Accepted Action-Endpoint Convention (D044)

Mone deliberately models non-CRUD operations as verb-in-path RPC routes rather
than contorting them into pseudo-resources. These are stateful commands ("do X
now") or auth handshakes, not addressable nouns, so a trailing verb segment is
the clearest, most honest URL. This convention is formally accepted as D044; the
routes below are the complete set of action-style endpoints and the parity test
asserts that no route marked `Style=action` exists outside this list.

| Action token | Route(s) | Rationale |
|--------------|----------|-----------|
| register | POST /api/auth/register (local signup), POST /api/executor-nodes/register | Create-with-side-effects handshakes (password hashing / node enrolment + token mint), not a plain resource POST |
| login | POST /api/auth/login, POST /api/auth/ldap/login, GET /api/auth/oidc/login | Credential exchange / SSO redirect; produces a session, addresses no resource |
| callback | GET /api/auth/oidc/callback | OIDC redirect landing that completes the SSO handshake |
| heartbeat | POST /api/executor-nodes/{id}/heartbeat | Periodic liveness ping from a node; a command, not a sub-resource |
| trigger-probe | POST /api/hosts/{hostId}/trigger-probe | Imperative "run this probe now" command against a host |
| sync | POST /api/plugin-repos/{id}/sync | Imperative "refresh this repo's catalog now" command |
| sync-all | POST /api/plugin-repos/sync-all | Collection-wide refresh command across all repos |
| install | POST /api/plugins/install | Imperative install of a plugin version |
| uninstall | POST /api/plugins/uninstall | Imperative removal of an installed plugin |
| reload | POST /api/plugins/reload | Imperative hot-reload of the plugin engine |
| db-size | GET /api/housekeeping/db-size | Computed maintenance report, not a stored entity |
| assess | POST /api/housekeeping/assess | Runs a retention assessment and returns a report |
| cleanup | POST /api/housekeeping/cleanup | Executes a retention cleanup pass |

## Pluralization / Verb Summary

The S01 UX-consistency catalog scored rubric dimension 9 (API resource
naming / pluralization / verb consistency) with **zero FLAG rows** across all 27
endpoint families. Concretely:

- **Pluralization:** every collection resource uses a plural noun segment —
  `hosts`, `host-groups`, `tags`, `roles`, `users`, `plugins`, `plugin-repos`,
  `executor-nodes`, `notifications`, `loaded-plugins`, `permissions`. Sub-resources
  follow the same rule (`probes`, `checkers`, `overrides`, `members`, `results`,
  `configs`). No singular/plural drift was found.
- **Verbs:** CRUD is expressed purely through HTTP methods on resource paths
  (GET/POST/PUT/DELETE); the only verb-in-path routes are the deliberately
  accepted action endpoints documented above (D044).
- **Casing:** all multi-word segments use kebab-case
  (`host-groups`, `plugin-repos`, `executor-nodes`, `effective-assignments`,
  `metric-keys`, `declared-metrics`, `latest-per-probe`, `trigger-probe`,
  `db-size`, `sync-all`).

Because dimension 9 produced no FLAG rows, **no renames were performed** in S05;
this slice confirms and documents the existing consistency rather than changing it.

## Dashboard ↔ API Parity

Every `api/...` route literal in `Mone.Dashboard`'s `ApiClient.cs` resolves to a
server route above after normalizing path parameters (`{id}`, `{hostId}`,
`{groupId}`, `{assignmentId}`, `{id:guid}`, … are all treated as a single
wildcard segment) and stripping query strings. There are no orphan client routes.
Server routes with an ApiClient consumer of `-` are reached by the executor-node
agent, auth/SSO browser redirects, or admin surfaces not yet wired into the
Blazor client; they are intentionally server-only and are not parity violations.
