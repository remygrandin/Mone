#!/usr/bin/env bash
# Capture full-page screenshots of every restyled dashboard screen in Light and Dark
# themes. Brings up the docker compose stack (the API serves the WASM dashboard at
# http://localhost:8080), installs the Playwright Chromium browser, then runs the
# screenshot harness. The stack is left running afterwards for manual inspection.
#
# These captures feed the M007 deuteranope (red-green colour-blind) human sign-off:
# they are the rendered visual-identity and dual-encoded-status evidence.
set -euo pipefail

# The .NET 10 SDK lives at $HOME/.dotnet and is not on the default PATH.
export PATH="$HOME/.dotnet:$PATH"

# Resolve the Mone project dir (this script lives in Mone/scripts) so the script
# works regardless of the caller's current directory.
MONE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$MONE_DIR"

BASE_URL="${SCREENSHOT_BASE_URL:-http://localhost:8080}"
OUTPUT_DIR="${SCREENSHOT_OUTPUT_DIR:-$MONE_DIR/artifacts/screenshots}"
HARNESS="tests/Mone.ScreenshotHarness/Mone.ScreenshotHarness.csproj"
READY_TIMEOUT_SECONDS="${SCREENSHOT_READY_TIMEOUT:-300}"

echo "[capture] bringing up docker compose stack (this builds images on first run)"
docker compose up -d --build

echo "[capture] waiting up to ${READY_TIMEOUT_SECONDS}s for the dashboard at ${BASE_URL}"
deadline=$(( $(date +%s) + READY_TIMEOUT_SECONDS ))
until curl -fsS -o /dev/null "$BASE_URL"; do
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "[capture] FATAL: ${BASE_URL} did not become ready within ${READY_TIMEOUT_SECONDS}s" >&2
    echo "[capture] recent api logs:" >&2
    docker compose logs --tail 50 api >&2 || true
    exit 1
  fi
  sleep 5
done
echo "[capture] dashboard is responding"

echo "[capture] building the screenshot harness"
dotnet build "$HARNESS" -c Debug

# Install the Playwright Chromium browser. The Microsoft.Playwright NuGet package
# emits a PowerShell driver script (playwright.ps1) into the build output; the
# documented install command is:
#     pwsh <output>/playwright.ps1 install chromium --with-deps
# pwsh (PowerShell 7+) is the documented path. When it is absent, the same package
# also ships a bundled Node driver (.playwright/package/cli.js + .playwright/node)
# that installs the identical browser without PowerShell — we fall back to that.
#
# On an OS Playwright has no prebuilt Chromium for (e.g. a newer Ubuntu than the
# driver knows), export PLAYWRIGHT_HOST_PLATFORM_OVERRIDE to the nearest supported
# platform (e.g. ubuntu24.04-x64) before running this script. It is honoured by both
# the install below and the harness launch, and is inherited by the dotnet run.
BUILD_OUT="$MONE_DIR/tests/Mone.ScreenshotHarness/bin/Debug/net10.0"
PLAYWRIGHT_SCRIPT="$BUILD_OUT/playwright.ps1"
DRIVER_CLI="$BUILD_OUT/.playwright/package/cli.js"
if command -v pwsh >/dev/null 2>&1; then
  if [ ! -f "$PLAYWRIGHT_SCRIPT" ]; then
    echo "[capture] FATAL: Playwright driver not found at $PLAYWRIGHT_SCRIPT (did the build succeed?)" >&2
    exit 1
  fi
  echo "[capture] installing Playwright Chromium browser (pwsh driver)"
  pwsh "$PLAYWRIGHT_SCRIPT" install chromium --with-deps
else
  echo "[capture] pwsh not found; using the bundled Playwright Node driver"
  if [ ! -f "$DRIVER_CLI" ]; then
    echo "[capture] FATAL: Playwright Node driver not found at $DRIVER_CLI (did the build succeed?)" >&2
    exit 1
  fi
  DRIVER_NODE="$BUILD_OUT/.playwright/node/linux-x64/node"
  [ -x "$DRIVER_NODE" ] || DRIVER_NODE="$(command -v node || true)"
  if [ -z "$DRIVER_NODE" ]; then
    echo "[capture] FATAL: no Node runtime found to drive the Playwright install." >&2
    echo "[capture] install pwsh or Node, or run: dotnet tool install --global Microsoft.Playwright.CLI && playwright install chromium" >&2
    exit 1
  fi
  echo "[capture] installing Playwright Chromium browser (node driver)"
  "$DRIVER_NODE" "$DRIVER_CLI" install chromium
  echo "[capture] installing Chromium OS dependencies (needs sudo/apt)"
  "$DRIVER_NODE" "$DRIVER_CLI" install-deps chromium
fi

echo "[capture] running the screenshot harness -> ${OUTPUT_DIR}"
dotnet run --project "$HARNESS" -c Debug -- \
  --base-url "$BASE_URL" \
  --output-dir "$OUTPUT_DIR"

echo "[capture] done. screenshots in ${OUTPUT_DIR}"
echo "[capture] stack left running; tear down with: (cd \"$MONE_DIR\" && docker compose down)"
