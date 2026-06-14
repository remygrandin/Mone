# M006 Single-Product Walkthrough

This document is the recorded walkthrough of the core operator journey for milestone
M006. It is the demo artifact required by slice S08: it confirms that Mone reads as
**one product** — every screen the operator touches and every API surface behind it
shares the same loading/empty/error primitives, the same RFC-9457 error channel, and
the same glossary terminology, with zero unresolved entries left in the UX consistency
catalog.

It is written to be self-contained for a fresh reader: you do not need prior context to
follow the journey or to check the evidence. Each step names the operator action, the
dashboard screen it happens on (with its now-cleared `UX-###` catalog rows), the API
surface(s) it hits (with their now-cleared `API-###` catalog rows), and the
cross-cutting consistency evidence.

## How this artifact is guarded

Per **D048**, under autonomous execution there is no human at a browser to perform a
live click-through, so the live human/UI walkthrough is **substituted** by two
falsifiable, git-tracked artifacts:

1. **The runtime operator-journey integration test** —
   `Mone/tests/Mone.Api.Tests/OperatorJourneyTests.cs`
   (`OperatorJourney_LoginToOverride_TraversesAsOneProduct`) — drives login → host list →
   host create → host detail → probe assignment with manifest config → group + membership
   → group probe assignment → override edit end-to-end through the **real API**, asserting
   uniform named-DTO success shapes and an RFC-9457 ProblemDetails error.
2. **This record**, guarded by `Mone/docs/m006-walkthrough.test.mjs` (node `--test`),
   which asserts that every journey step is named here, that this record cites
   `OperatorJourneyTests`, that the catalog has zero unresolved (`| FLAG |`) rows, and
   that this autonomous-mode deviation is stated.

This mirrors the D039 falsifiability pattern already used for the API route inventory
and the UX consistency catalog: the proof is a test, not a claim.

## Cross-cutting consistency conventions (apply to every step)

Every step below inherits the three foundational M006 decisions, so the per-step
"Consistency evidence" sections reference them rather than restating them:

- **Shared loading / empty / error primitives (D041 / D3).** Dashboard screens route
  async loads through the shared loading-state component and list/collection screens
  render an explicit empty-state (message + primary action) — never a bare blank table.
  The same `StateView` / `LoadingIndicator` / `ErrorAlert` primitives are reused across
  pages, which is what makes the screens read as one product rather than a dozen
  independently-built pages.
- **Single RFC-9457 ProblemDetails error channel (D1 / D037).** Every `Mone.Api`
  endpoint emits errors as RFC-9457 `application/problem+json`; the dashboard surfaces
  them through exactly one channel that parses ProblemDetails `detail`/`title` (the typed
  `ApiException`) — no raw `ex.Message`, no anonymous `{error=}`, no silent `catch{}`.
- **Glossary terminology (D2).** User-visible labels use the canonical glossary terms —
  Host, Host Group, Probe, Assignment, Override, Effective Assignment — and the retired
  variants (Machine/Server, Cluster, Sensor, Binding, Exception) do not appear.

---

## Step 1 — Login

- **(a) Operator action.** The operator opens Mone and signs in with email + password
  (SuperAdmin), receiving a usable bearer token for the rest of the session.
- **(b) Dashboard screen + cleared catalog rows.** `Login.razor` — **UX-006 (OK)**, the
  catalog's best error-handling exemplar: inline friendly messages ("Invalid email or
  password.", "Unable to reach the server."), button-spinner loading, `EditForm` with
  Required validation. (It is the one intentional, ProblemDetails-free pre-auth exception
  to the shared error channel.)
- **(c) API surface + cleared API rows.** `AuthEndpoints.cs` — **API-002 (OK)**, the
  reference row for the D1 target shape (`/register` → `ValidationProblem`, `/login` →
  `Problem(401)`). Adjacent auth surfaces are equally clean: `AuthProviderEndpoints.cs`
  **API-003 (OK)**, `LdapAuthEndpoints.cs` **API-013 (OK)**, and `OidcAuthEndpoints.cs`
  **API-017 (FIXED)** — whose provisioning-failure response was reclassified from a
  catch-all `500` to `502 Bad Gateway` (dim 10, T01, per D047) because the fault is an
  upstream-dependency failure, not an internal server bug.
- **(d) Consistency evidence.** Errors arrive as RFC-9457 ProblemDetails (D1/D037);
  "Login" / "password" labels match the glossary (D2); the friendly inline alert is the
  documented accepted exception to D41's shared channel for the pre-auth screen.
- **Runtime proof.** `OperatorJourneyTests` step 1 authenticates and obtains a working
  SuperAdmin token via `CreateAuthenticatedClientAsync`.

## Step 2 — Host list

- **(a) Operator action.** After login the operator lands on the Host list to see every
  monitored host; from here they create a new host.
- **(b) Dashboard screen + cleared catalog rows.** `HostList.razor` — **UX-003 (FIXED,
  S03)**, formerly the catalog's "worst-gap exemplar": it now has a required empty-state
  with a primary "Add Host" action, the shared loading-state, a single ProblemDetails
  error channel (the old inline+Snackbar split and raw `ex.Message` are gone), and the
  silent per-host status `catch{}` is removed. The create dialog `HostDialog.razor` —
  **UX-014 (FIXED, S04)** — now wraps its tag load so a failure reaches the shared
  channel.
- **(c) API surface + cleared API rows.** `HostEndpoints.cs` — **API-010 (FIXED)**: bare
  strings became `ValidationProblem` / `Problem(409)`, the empty `404` became
  `Problem(404)`, status codes are correct (201/204/404/409/400), and Tags/Summary/Produces
  annotations were added.
- **(d) Consistency evidence.** Shared empty/loading/error primitives (D41); create-host
  validation and conflict errors flow through the single ProblemDetails channel
  (D1/D037); "Host" is the canonical glossary term (D2).
- **Runtime proof.** `OperatorJourneyTests` steps 2–3: `GET /api/hosts` returns `200` and
  a JSON array; `POST /api/hosts` returns `201 Created` with the named `HostResponse`.

## Step 3 — Host detail

- **(a) Operator action.** The operator opens the newly created host to inspect its probe
  assignments and latest status.
- **(b) Dashboard screen + cleared catalog rows.** `HostDetail.razor` — **UX-001 (FIXED,
  S04)**: it keeps its reference-quality empty-states (no probes / no checkers / no
  numeric metrics) but now loads through the shared loading-state component, routes GET
  failures inline via `ErrorAlert` and all mutations through the typed `ApiException`
  channel, and has dropped the silent `catch{}` and the stray `Console.WriteLine`.
- **(c) API surface + cleared API rows.** The detail page composes several now-clean
  reads: `ProbeAssignmentEndpoints.cs` **API-021 (FIXED)**, `StatusEndpoints.cs`
  **API-025 (FIXED)** (history bounded via a clamped `limit`), `EffectiveAssignmentEndpoints.cs`
  **API-006 (FIXED)** (anonymous `{probes,checkers}` → named `EffectiveAssignmentsResponse`),
  and `ProbeResultEndpoints.cs` **API-022 (FIXED)** (the highest-volume time-series surface,
  now range/limit-bounded).
- **(d) Consistency evidence.** Shared loading/error primitives (D41); empty `404`s are
  now `Problem(404)` on the single channel (D1/D037); "Probe", "status", and "Effective
  Assignment" match the glossary (D2).
- **Runtime proof.** `OperatorJourneyTests` step 4: `GET /api/hosts/{id}/probes` and
  `GET /api/hosts/{id}/status/latest` both return `200` with array shapes.

## Step 4 — Manifest config form (probe assignment)

- **(a) Operator action.** On the host the operator assigns a probe ("ping") and fills its
  configuration through the manifest-driven form (timeout, retries), then saves.
- **(b) Dashboard screen + cleared catalog rows.** `ProbeAssignmentDialog.razor` —
  **UX-019 (OK)**, a strong form exemplar: required plugin/name/cron fields, submit
  disabled while invalid, manifest-driven config via the shared `ManifestFormFields.razor`
  — **UX-016 (OK)** building block — snake-case key helper, and conventional Cancel/Save
  ordering.
- **(c) API surface + cleared API rows.** `ProbeAssignmentEndpoints.cs` — **API-021
  (FIXED)**: the anonymous `{error=}` validation body became `ValidationProblem`, the empty
  `404` became `Problem(404)`, and OpenAPI annotations were added.
- **(d) Consistency evidence.** The same shared manifest form drives probe, checker, and
  override config (one form pattern, D6); validation failures return `400` ValidationProblem
  rendered against the offending field (D1/D037/D8); "Probe" / "Assignment" terminology is
  canonical (D2).
- **Runtime proof.** `OperatorJourneyTests` step 5: `POST /api/hosts/{id}/probes` with a
  `ConfigJson` payload (`{"timeout":30,"retries":3}`) returns `201` and the named
  `ProbeAssignmentResponse`.

## Step 5 — Group management

- **(a) Operator action.** The operator creates a Host Group, adds the host as a member,
  and assigns a probe at the group level (inherited by member hosts).
- **(b) Dashboard screen + cleared catalog rows.** `HostGroups.razor` — **UX-002 (FIXED,
  S04)**: load and Create/Edit/Delete are all routed through the shared `StateView` /
  `ApiException` channel, every debug `Console.WriteLine` is removed, and the raw
  `HttpRequestException.Message` surfacing is gone. The create/edit dialog
  `HostGroupDialog.razor` — **UX-015 (OK)** — conforms (required Name, submit disabled
  while blank, conventional ordering).
- **(c) API surface + cleared API rows.** `HostGroupEndpoints.cs` — **API-011 (FIXED)**,
  formerly the widest shape inconsistency in one file (mixed bare strings + anonymous +
  empty bodies), now uniformly `Problem`/`ValidationProblem`; its `/{id}/members`
  sub-resource is clean. Group-level probe assignment hits
  `GroupProbeAssignmentEndpoints.cs` — **API-009 (FIXED)**.
- **(d) Consistency evidence.** Shared empty/loading/error primitives with the "No groups
  yet" empty-state (D41); all mutations on the single ProblemDetails channel (D1/D037);
  "Host Group" and "Assignment" are canonical, with "Cluster"/"Binding" retired (D2).
- **Runtime proof.** `OperatorJourneyTests` steps 6–7: `POST /api/host-groups` → `201`
  named `HostGroupResponse`, `POST /api/host-groups/{id}/members` → `201`, and
  `POST /api/host-groups/{id}/probes` → `201` named `ProbeAssignmentResponse`.

## Step 6 — Override edit

- **(a) Operator action.** Back on the host, the operator overrides the group-inherited
  probe's config (raising the timeout to 45) so the per-host value supersedes the
  group-level setting.
- **(b) Dashboard screen + cleared catalog rows.** `OverrideEditDialog.razor` — **UX-018
  (OK)**: it composes the shared `ManifestFormFields.razor` (**UX-016 (OK)**), has a clear
  title, and uses the conventional action layout (Remove left, Cancel/Save right).
- **(c) API surface + cleared API rows.** `AssignmentOverrideEndpoints.cs` — **API-001
  (FIXED)**: bare/empty errors became `Results.Problem`/`ValidationProblem`, the anonymous
  `{probes,checkers}` success became a named response DTO, and OpenAPI annotations were
  added. The resolved result is read back through `EffectiveAssignmentEndpoints.cs` —
  **API-006 (FIXED)**.
- **(d) Consistency evidence.** The override edits through the same shared manifest form as
  the original assignment (D6); the upsert and its validation errors flow through the
  single ProblemDetails channel (D1/D037); "Override" and "Effective Assignment" are
  canonical glossary terms (D2).
- **Runtime proof.** `OperatorJourneyTests` step 7:
  `PUT /api/hosts/{id}/overrides/probes/{assignmentId}` with `{"timeout":45}` returns
  `200 OK`. Step 8 then proves the error contract is uniform: `POST /api/hosts` with an
  empty name returns RFC-9457 `application/problem+json` with status `400`.

---

## Single-product acceptance

This is the R031 acceptance statement for M006. All three pillars hold:

1. **The catalog has zero unresolved entries.** Every `UX-###` and `API-###` row in
   `Mone/docs/ux-consistency-catalog.md` carries a terminal resolution — `FIXED`,
   `ACCEPTED`-with-rationale (API-019 default-on-missing global-config read; API-020 action
   routes; UX-017 deferred manifest fields per D043; UX-010 master-detail editor variant),
   or `OK`. No `| FLAG |` row remains. This is enforced by the no-unresolved-rows invariant
   in `ux-consistency-catalog.test.mjs` and re-checked here.
2. **The runtime operator journey is green.** `OperatorJourneyTests` traverses the entire
   login → host list → host detail → manifest config → group management → override edit
   journey through the real API in one flow, asserting uniform named-DTO success shapes and
   an RFC-9457 ProblemDetails error.
3. **Per-screen dashboard consistency is proven.** Each screen's loading/empty/error
   migration to the shared primitives was implemented and verified by the S03 and S04 test
   suites, and the screen↔row mapping above ties each journey screen to its cleared catalog
   row.

Together these show Mone presents as one coherent product: one error contract, one set of
state primitives, one glossary — top to bottom of the operator's daily path.

## Autonomous-mode deviation

**Deviation (per D048):** this milestone was completed under autonomous execution with no
human operator at a browser, so the planned **live human/UI walkthrough** was not performed
interactively. It is substituted by two falsifiable, git-tracked artifacts that together
stand in for the manual click-through: the runtime **OperatorJourneyTests** integration
test (which exercises the identical journey end-to-end through the real API) and this
recorded walkthrough document (guarded by `m006-walkthrough.test.mjs`). No catalog claim in
this record is unbacked: every "FIXED/ACCEPTED" reference resolves to a row in the
test-guarded catalog, and every "runtime proof" resolves to a step in the integration test.
**R031 is validated by this clearance** — the catalog reaches zero unresolved entries and
the single-product journey is proven green.
