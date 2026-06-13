# User Guide

This guide walks through using the Mone dashboard day to day: signing in,
registering things to monitor, assigning probes and checkers, and wiring up
notifications. It assumes a running deployment (see [deployment.md](deployment.md)).

## Concepts in one minute

- **Host** — something you monitor (a server, a URL, a device), identified by a
  name and an address.
- **Group** — a named collection of hosts. Assignments made on a group are
  inherited by every member, so you configure once and apply to many.
- **Probe** — a plugin that checks a host and produces a result with a status and
  metrics (e.g. ping latency, HTTPS cert days remaining). Probes run on a
  schedule or receive data passively.
- **Checker** — a plugin that evaluates probe results against thresholds and
  decides the host's status (Healthy / Degraded / Unhealthy / Unreachable).
- **Notification** — a plugin that sends an alert (email, Slack, …) when a
  checker's status changes.
- **Assignment** — attaching a probe or checker (with its config) to a host or a
  group. An **override** tweaks an inherited group assignment for one host.

The flow you set up is: **probe collects → checker judges → notification alerts.**

## Signing in

Open the dashboard (e.g. `http://localhost:8080`) and log in. On a fresh install
use the seeded admin account `admin@mone.local` / `Admin123!` and change the
password right away. If your deployment has SSO or LDAP configured, use the
provider button on the login screen instead.

## Navigation

The left sidebar is the whole app:

| Menu | What it's for |
|------|---------------|
| **Home** | At-a-glance overview of monitored hosts and their current status. |
| **Hosts** | Manage hosts and open a host's detail page. |
| **Groups** | Manage host groups and group-level assignments. |
| **Notifications** | Configure notification channels (email, Slack, …). |
| **Nodes** | View registered executors (local and remote) and their health. |
| **Plugins** | See loaded plugins and manage external plugin repositories. |
| **Housekeeping** | Maintenance and data-retention tasks. |
| **Roles / Users** | Access control (admins only): define roles and assign them to users at a scope. See [authorization.md](authorization.md). |

Menu items only appear for resources you can access — the sidebar reflects your
permissions, so a read-only or scoped user sees a smaller menu.

## Adding a host

1. Go to **Hosts → add a host**.
2. Give it a **name** and an **address** (hostname or IP). The address is what
   probes target unless an assignment overrides it.
3. Save. The host appears in the list; open it to configure monitoring.

Add **tags** to a host to group and filter it. Tags are free-form labels.

## The host detail page

Opening a host shows three tabs:

- **Dashboard** — the current status plus charts of recent probe metrics over
  time. This is where you watch a host.
- **Analysis** — historical results and checker state changes, for digging into
  *when* and *why* a status changed. Checker state changes are labelled by the
  checker's assignment name.
- **Settings** — where you assign and edit probes and checkers for this host.

## Assigning a probe

From a host's **Settings** tab (or a group's page for bulk application):

1. **Add a probe assignment** and pick a probe plugin (e.g. Ping, HTTPS).
2. Fill in the config form — the fields are defined by the plugin, with
   sensible defaults and validation. For example, Ping exposes a timeout; HTTPS
   exposes the URL and a certificate-expiry warning window.
3. Set the **schedule** (how often the probe runs).
4. Optionally set **Run on node** to pin execution to a specific executor (see
   [remote-executors.md](remote-executors.md)); leave it unset to run on every
   executor of the right kind.
5. Save. Results begin arriving on the next schedule tick; use **Refresh now** /
   the manual trigger to run it immediately.

Passive probes (e.g. Webhook, Syslog) don't poll — instead they receive data
pushed to the executor. Their assignment configures how incoming data is matched
to this host rather than a schedule.

## Assigning a checker

A probe only collects data; a **checker** turns it into a status.

1. In the host's **Settings**, **add a checker assignment** and choose a checker
   plugin (e.g. Threshold Checker).
2. Configure the thresholds — which metric to watch and the bands that map to
   Degraded / Unhealthy. The form is driven by the plugin's config manifest.
3. Save. The Checker Engine now evaluates incoming probe results and the host's
   status reflects the checker's verdict. Status changes are recorded in the
   host's **Analysis** tab.

## Groups and inheritance

To monitor many hosts the same way:

1. Go to **Groups**, create a group, and add hosts to it.
2. Assign probes and checkers **on the group**. Every member inherits them.
3. On an individual host that needs a tweak, add an **override** to the inherited
   assignment — the host stays in the group but uses adjusted config.

This keeps fleet-wide monitoring defined in one place while still allowing
per-host exceptions.

## Notifications

To be alerted when status changes:

1. Go to **Notifications** and add a notification config for a channel (Email,
   Slack, Teams, Webhook). Fill in the channel settings — SMTP server and
   recipients for email, a webhook URL for Slack/Teams, etc.
2. Notification configs hold credentials; secret fields are stored as secrets.
3. When a checker's status changes, the Alert Engine dispatches through the
   matching notification configs. Delivery attempts (success or failure) are
   recorded in the audit log so you can confirm an alert actually went out.

## Nodes

**Nodes** lists every executor that has registered — the local probe executor and
checker engine plus any remote ones. Each shows its role and health (Online /
Stale / Offline based on heartbeats). Bind assignments to a node from the
assignment's **Run on node** field. Deploying remote nodes is covered in
[remote-executors.md](remote-executors.md).

## Plugins

**Plugins** shows what each service has loaded and lets you register external
**plugin repositories** to pull additional plugins from. Built-in plugins (Ping,
HTTPS, Webhook, Threshold Checker, Email, …) are loaded automatically. For each
plugin's parameters and behaviour, see the **Mone-Plugins** repository's `docs/`.

## Housekeeping

**Housekeeping** holds maintenance actions such as pruning old time-series data.
Use it to keep the results and history tables bounded over time.

## A typical first setup

1. Log in, change the admin password.
2. Add a host for a server you care about.
3. Assign a **Ping** probe on a 1-minute schedule.
4. Assign a **Threshold Checker** that flags high latency as Degraded/Unhealthy.
5. Add an **Email** notification config with your address.
6. Watch the host's **Dashboard** tab; trigger a failure and confirm the status
   changes and an email arrives.
