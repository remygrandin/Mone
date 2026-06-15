#!/usr/bin/env bash
# Verify that the screenshot capture produced a non-empty PNG for every dashboard
# screen in both Light and Dark themes. Prints each missing or zero-byte artifact by
# name so a future agent can localize which screen failed to render without rerunning
# the whole capture harness. Exits non-zero if the expected set is incomplete.
set -euo pipefail

MONE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIR="${SCREENSHOT_OUTPUT_DIR:-$MONE_DIR/artifacts/screenshots}"

# The authoritative screen list — must mirror ScreenManifest.All in
# tests/Mone.ScreenshotHarness/ScreenManifest.cs (Login plus the 11 authed screens).
SCREENS=(
  Login
  Index
  HostList
  HostGroups
  HostDetail
  StatusHistory
  Nodes
  NotificationConfigs
  Plugins
  Roles
  Users
  Housekeeping
)
THEMES=(Light Dark)

echo "[verify] checking screenshots in ${OUTPUT_DIR}"
if [ ! -d "$OUTPUT_DIR" ]; then
  echo "[verify] FAIL: output directory does not exist: ${OUTPUT_DIR}" >&2
  echo "[verify] run scripts/capture-screenshots.sh first" >&2
  exit 1
fi

missing=()
empty=()
ok=0
expected=0

for screen in "${SCREENS[@]}"; do
  for theme in "${THEMES[@]}"; do
    expected=$((expected + 1))
    file="$OUTPUT_DIR/${screen}-${theme}.png"
    if [ ! -f "$file" ]; then
      missing+=("${screen}-${theme}.png")
    elif [ ! -s "$file" ]; then
      empty+=("${screen}-${theme}.png")
    else
      ok=$((ok + 1))
    fi
  done
done

for name in "${missing[@]:-}"; do
  [ -n "$name" ] && echo "[verify] MISSING: ${name}" >&2
done
for name in "${empty[@]:-}"; do
  [ -n "$name" ] && echo "[verify] ZERO-BYTE: ${name}" >&2
done

if [ "${#missing[@]}" -gt 0 ] || [ "${#empty[@]}" -gt 0 ]; then
  echo "[verify] FAIL: ${ok}/${expected} valid; missing=${#missing[@]} zero-byte=${#empty[@]}" >&2
  exit 1
fi

echo "[verify] OK: all ${expected} screenshots present and non-empty"
