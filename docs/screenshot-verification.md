# Screenshot Verification

Headless-browser capture of every restyled dashboard screen in **Light** and **Dark**
themes. These screenshots are the rendered evidence for the M007 milestone's human
**deuteranope (red-green colour-blind) sign-off**: a reviewer inspects them to confirm
the consistent visual identity and that every status is **dual-encoded** (colour *and*
shape/icon/text), so meaning never depends on hue alone.

## What gets captured

A full-page PNG per screen × theme is written to `Mone/artifacts/screenshots/` as
`<Screen>-<Theme>.png`. The screen set is defined authoritatively in
`tests/Mone.ScreenshotHarness/ScreenManifest.cs` (the browserless `ScreenManifestTests`
locks the set). Current screens:

| Screen | Route |
|--------|-------|
| Login | `/login` |
| Index | `/` |
| HostList | `/hosts` |
| HostGroups | `/groups` |
| HostDetail | `/hosts/{id}` |
| StatusHistory | `/hosts/{id}/status` |
| Nodes | `/nodes` |
| NotificationConfigs | `/notifications` |
| Plugins | `/plugins` |
| Roles | `/admin/roles` |
| Users | `/admin/users` |
| Housekeeping | `/housekeeping` |

That is 12 screens × 2 themes = **24 PNGs**.

## Prerequisites

- **Docker** + Docker Compose — the `Mone/docker-compose.yml` stack serves the dashboard
  (the API hosts the WASM app at <http://localhost:8080>).
- **.NET 10 SDK** — installed at `$HOME/.dotnet`; the scripts prepend it to `PATH`.
- **Playwright Chromium browser** — installed by the capture script. The Microsoft.Playwright
  NuGet package emits a `playwright.ps1` driver into the build output; if **pwsh
  (PowerShell 7+)** is on `PATH` the script uses it. When pwsh is absent, the script
  falls back to the package's bundled Node driver (`.playwright/package/cli.js`, driven by
  `.playwright/node/linux-x64/node` or any `node` on `PATH`) and runs both `install chromium`
  and `install-deps chromium` — no pwsh required. If you prefer the standalone CLI:

  ```bash
  dotnet tool install --global Microsoft.Playwright.CLI
  playwright install chromium
  ```

- **Unsupported-OS override** — on a newer Linux than the pinned Playwright knows about
  (e.g. Ubuntu 26.04, where install fails with `does not support chromium on ubuntu26.04-x64`),
  export the nearest supported platform before running. It is honoured by both the browser
  install and the headless launch:

  ```bash
  export PLAYWRIGHT_HOST_PLATFORM_OVERRIDE=ubuntu24.04-x64
  ```

## Capture

```bash
Mone/scripts/capture-screenshots.sh
```

This will:

1. `docker compose up -d --build` from the `Mone/` directory.
2. Poll <http://localhost:8080> until the dashboard responds (bounded timeout, default
   300s; override with `SCREENSHOT_READY_TIMEOUT`).
3. Build the harness and install the Playwright Chromium browser.
4. Run `Mone.ScreenshotHarness`, which logs in against the API, seeds a host so
   host-scoped routes resolve, injects the JWT into `localStorage`, and saves each
   screen×theme PNG.
5. Leave the stack running for inspection. Tear it down with
   `cd Mone && docker compose down`.

Overridable environment variables: `SCREENSHOT_BASE_URL`, `SCREENSHOT_OUTPUT_DIR`,
`SCREENSHOT_READY_TIMEOUT`. Credentials default to the dev seed admin and can be
overridden via `SCREENSHOT_EMAIL` / `SCREENSHOT_PASSWORD` (consumed by the harness).

The harness logs one structured line per screen×theme
(`capture screen=… theme=… route=… path=… result=…`) and a final `SUMMARY:` line; on
failure it names the screen/theme that did not capture and exits non-zero.

## Verify

```bash
Mone/scripts/verify-screenshots.sh
```

Asserts all 24 expected PNGs exist under `Mone/artifacts/screenshots/` and are non-empty.
Any missing or zero-byte artifact is printed by filename (`MISSING:` / `ZERO-BYTE:`) and
the script exits non-zero, so a future agent can localize which screen failed to render
without rerunning the full capture.

## Artifacts are not committed

`Mone/artifacts/` is git-ignored — the binary PNGs are regenerated on demand and never
committed. Capture them locally when performing the deuteranope sign-off review.
