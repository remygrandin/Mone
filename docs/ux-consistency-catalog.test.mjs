import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

// This test reads ONLY the tracked catalog markdown — never .gsd/ or other
// gitignored paths. It is the falsifiable coverage gate for M006/S01 and the
// S08 catalog-clearance check: it fails loudly if a page or API family is
// missing, if IDs collide, if a locked decision is dropped, or if a FLAG row
// has no Target.

const __dirname = dirname(fileURLToPath(import.meta.url));
const CATALOG_PATH = join(__dirname, 'ux-consistency-catalog.md');
const catalog = readFileSync(CATALOG_PATH, 'utf8');

// --- Canonical lists (baked in; the test owns the truth, not the catalog) ---

const PAGES = [
  'HostDetail.razor',
  'HostGroups.razor',
  'HostList.razor',
  'Housekeeping.razor',
  'Index.razor',
  'Login.razor',
  'Nodes.razor',
  'NotificationConfigs.razor',
  'Plugins.razor',
  'Roles.razor',
  'StatusHistory.razor',
  'Users.razor',
];

const ENDPOINTS = [
  'AssignmentOverrideEndpoints.cs',
  'AuthEndpoints.cs',
  'AuthProviderEndpoints.cs',
  'CheckerAssignmentEndpoints.cs',
  'DashboardEndpoints.cs',
  'EffectiveAssignmentEndpoints.cs',
  'ExecutorNodeEndpoints.cs',
  'GroupCheckerAssignmentEndpoints.cs',
  'GroupProbeAssignmentEndpoints.cs',
  'HostEndpoints.cs',
  'HostGroupEndpoints.cs',
  'HousekeepingEndpoints.cs',
  'LdapAuthEndpoints.cs',
  'LoadedPluginEndpoints.cs',
  'NotificationAuditEndpoints.cs',
  'NotificationConfigEndpoints.cs',
  'OidcAuthEndpoints.cs',
  'PermissionEndpoints.cs',
  'PluginGlobalConfigEndpoints.cs',
  'PluginRepositoryEndpoints.cs',
  'ProbeAssignmentEndpoints.cs',
  'ProbeResultEndpoints.cs',
  'ProbeTriggerEndpoints.cs',
  'RoleEndpoints.cs',
  'StatusEndpoints.cs',
  'TagEndpoints.cs',
  'UserAdminEndpoints.cs',
];

// --- Markdown helpers ---

/** Return the text of a `## <name>` section (up to the next `## ` or EOF). */
function section(name) {
  const lines = catalog.split('\n');
  const start = lines.findIndex((l) => l.trim() === `## ${name}`);
  assert.notEqual(start, -1, `missing "## ${name}" section`);
  let end = lines.length;
  for (let i = start + 1; i < lines.length; i++) {
    if (/^## /.test(lines[i])) {
      end = i;
      break;
    }
  }
  return lines.slice(start, end).join('\n');
}

/** Split a markdown table row "| a | b |" into trimmed cells [a, b]. */
function cells(row) {
  return row
    .split('|')
    .slice(1, -1)
    .map((c) => c.trim());
}

/** Data rows of the first markdown table inside a section (skips header + separator). */
function tableRows(sectionText) {
  const rows = sectionText
    .split('\n')
    .filter((l) => l.trim().startsWith('|'))
    .map(cells)
    .filter((c) => c.length > 0);
  // rows[0] = header, rows[1] = |---| separator
  return rows.slice(2).filter((c) => !c.every((cell) => /^-*:?-*$/.test(cell)));
}

const apiSection = section('API Catalog');
const dashSection = section('Dashboard Catalog');
const decisionsSection = section('Locked Decisions');

// --- (a) Every dashboard page basename appears in the Dashboard Catalog ---

test('every dashboard page is scored in the Dashboard Catalog', () => {
  for (const page of PAGES) {
    assert.ok(
      dashSection.includes(page),
      `Dashboard Catalog is missing page ${page}`,
    );
  }
});

// --- (b) Every API endpoint basename appears in the API Catalog ---

test('every API endpoint family is scored in the API Catalog', () => {
  for (const ep of ENDPOINTS) {
    assert.ok(
      apiSection.includes(ep),
      `API Catalog is missing endpoint ${ep}`,
    );
  }
});

// --- (c) UX-### / API-### IDs present, no duplicates (ID column only) ---

test('catalog IDs are present and unique', () => {
  const apiIds = tableRows(apiSection)
    .map((c) => c[0])
    .filter((id) => /^API-\d{3}$/.test(id));
  const uxIds = tableRows(dashSection)
    .map((c) => c[0])
    .filter((id) => /^UX-\d{3}$/.test(id));

  assert.ok(apiIds.length > 0, 'no API-### IDs found in API Catalog');
  assert.ok(uxIds.length > 0, 'no UX-### IDs found in Dashboard Catalog');

  const dup = (arr) =>
    arr.filter((v, i) => arr.indexOf(v) !== i);
  assert.deepEqual(dup(apiIds), [], `duplicate API IDs: ${dup(apiIds)}`);
  assert.deepEqual(dup(uxIds), [], `duplicate UX IDs: ${dup(uxIds)}`);

  // Every scored endpoint family needs its own API row (API-000 is the
  // global foundation row on top of the 27 files).
  assert.ok(
    apiIds.length >= ENDPOINTS.length,
    `expected at least ${ENDPOINTS.length} API rows, found ${apiIds.length}`,
  );
});

// --- (d) Locked Decisions section names all three decisions ---

test('Locked Decisions records the three foundational decisions', () => {
  assert.match(
    decisionsSection,
    /ProblemDetails/,
    'D1 (RFC-9457 ProblemDetails error shape) not named in Locked Decisions',
  );
  assert.match(
    decisionsSection,
    /glossary/i,
    'D2 (terminology glossary) not named in Locked Decisions',
  );
  assert.match(
    decisionsSection,
    /state-handling/i,
    'D3 (dashboard state-handling convention) not named in Locked Decisions',
  );
});

// --- (e) Every FLAG row carries a non-empty Target ---

test('every FLAG row carries a concrete Target', () => {
  const offenders = [];
  for (const sectionText of [apiSection, dashSection]) {
    for (const row of tableRows(sectionText)) {
      // Catalog rows: ID | Name | File | Flagged | Current State | Target | Status
      const status = row[row.length - 1];
      const target = row[row.length - 2];
      if (status === 'FLAG') {
        const empty = !target || target === '—' || target.toLowerCase() === 'n/a';
        if (empty) offenders.push(`${row[0]} (Target="${target ?? ''}")`);
      }
    }
  }
  assert.deepEqual(
    offenders,
    [],
    `FLAG rows missing a Target: ${offenders.join(', ')}`,
  );
});

// --- (f) Every dim-12-flagged API row records its S07 resolution ---
// S07 owns rubric dimension 12 (list pagination/filtering). Every API row whose
// Flagged Dimensions cell carries the token "12" must record its dim-12
// resolution in the Current State cell via the stable `dim12 S07` marker
// (FIXED for the three bounded log surfaces, ACCEPTED for the operator-bounded
// entity collections). This is falsifiable per D039.

test('every dim-12-flagged API row records its S07 resolution', () => {
  const offenders = [];
  for (const row of tableRows(apiSection)) {
    const id = row[0];
    if (!/^API-\d{3}$/.test(id)) continue;
    const flagged = (row[3] ?? '')
      .split(/[ ,]+/)
      .map((t) => t.trim());
    if (!flagged.includes('12')) continue;
    // Reconstruct the Current State cell (column index 4). Some rows embed a
    // literal "|" in the cell (e.g. the `/status/latest|history` route), which
    // the naive pipe-split in cells() over-splits — so rejoin everything
    // between Flagged Dimensions (3) and the trailing Target/Status columns.
    const currentState = row.slice(4, -2).join('|');
    if (!currentState.includes('dim12 S07')) offenders.push(id);
  }
  assert.deepEqual(
    offenders,
    [],
    `dim-12-flagged API rows missing a "dim12 S07" resolution marker: ${offenders.join(', ')}`,
  );
});

// --- (g) No catalog row is left unresolved (zero FLAG) ---
// S08 clears the catalog: every API and Dashboard row must carry a terminal
// resolution (FIXED / ACCEPTED / OK), never FLAG. The Status cell is the last
// column; read it as row[row.length-1] so a row with an embedded "|" in an
// earlier cell (e.g. API-025's `/status/latest|history` route) still reports
// its true trailing Status. Fails loudly with the offending IDs.

test('no catalog row is left unresolved (zero FLAG)', () => {
  const offenders = [];
  for (const sectionText of [apiSection, dashSection]) {
    for (const row of tableRows(sectionText)) {
      const status = row[row.length - 1];
      if (status === 'FLAG') offenders.push(row[0]);
    }
  }
  assert.deepEqual(
    offenders,
    [],
    `catalog rows still FLAG (unresolved): ${offenders.join(', ')}`,
  );
});
