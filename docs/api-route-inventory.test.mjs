import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

// Falsifiable parity + convention gate for M006/S05. This test reads ONLY
// git-tracked source — the API endpoint files, the dashboard ApiClient, and the
// inventory markdown — never .gsd/ or other gitignored paths, and never runs
// dotnet. It fails loudly if:
//   (a) an endpoint .cs family is missing from the inventory File column,
//   (b) a dashboard ApiClient route literal has no matching server route, or
//   (c) a route marked Style=action is not documented in the accepted D044 list.

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO = join(__dirname, '..'); // Mone/
const ENDPOINTS_DIR = join(REPO, 'src/Mone.Api/Endpoints');
const API_CLIENT = join(REPO, 'src/Mone.Dashboard/Services/ApiClient.cs');
const INVENTORY = join(__dirname, 'api-route-inventory.md');

const inventory = readFileSync(INVENTORY, 'utf8');
const apiClientSrc = readFileSync(API_CLIENT, 'utf8');

// --- Path normalization: params -> '*', drop query, collapse empties ---

function normalize(path) {
  const noQuery = path.split('?')[0];
  const segs = noQuery
    .split('/')
    .filter(Boolean)
    .map((s) => (s.startsWith('{') ? '*' : s));
  return '/' + segs.join('/');
}

// --- Derive the server route set from the endpoint .cs files ---
// Track `var x = something.MapGroup("PREFIX")` per file, then combine each
// `x.MapGet/Post/Put/Delete("SUB")` with its group prefix. Map* calls whose
// literal already starts with /api (inline full paths on app/routes) are used
// verbatim.

function serverRoutesFor(src) {
  const groups = {};
  const groupRe = /(\w+)\s*=\s*\w+\.MapGroup\(\s*"([^"]+)"/g;
  for (let m; (m = groupRe.exec(src)); ) {
    groups[m[1]] = m[2];
  }

  const routes = [];
  const mapRe = /(\w+)\.Map(?:Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"/g;
  for (let m; (m = mapRe.exec(src)); ) {
    const recv = m[1];
    const sub = m[2];
    let full;
    if (groups[recv] !== undefined) {
      full = groups[recv] + sub;
    } else if (/^\/?api\//.test(sub)) {
      full = sub.startsWith('/') ? sub : '/' + sub;
    } else {
      // Group-relative call whose group we could not resolve — skip rather than
      // invent a path. No current endpoint hits this branch.
      continue;
    }
    routes.push(normalize(full));
  }
  return routes;
}

const endpointFiles = readdirSync(ENDPOINTS_DIR).filter((f) => f.endsWith('.cs'));

const serverSet = new Set();
for (const f of endpointFiles) {
  for (const r of serverRoutesFor(readFileSync(join(ENDPOINTS_DIR, f), 'utf8'))) {
    serverSet.add(r);
  }
}

// --- Parse the inventory markdown route table ---

function tableCells(row) {
  return row
    .split('|')
    .slice(1, -1)
    .map((c) => c.trim());
}

const METHODS = new Set(['GET', 'POST', 'PUT', 'DELETE', 'PATCH']);

const inventoryRows = inventory
  .split('\n')
  .filter((l) => l.trim().startsWith('|'))
  .map(tableCells)
  .filter((c) => METHODS.has(c[0])); // route rows only: Method | Path | File | Style | Consumer

function conventionSection() {
  const lines = inventory.split('\n');
  const start = lines.findIndex((l) =>
    /^##\s+Accepted Action-Endpoint Convention/.test(l),
  );
  assert.notEqual(start, -1, 'missing "Accepted Action-Endpoint Convention" section');
  let end = lines.length;
  for (let i = start + 1; i < lines.length; i++) {
    if (/^## /.test(lines[i])) {
      end = i;
      break;
    }
  }
  return lines.slice(start, end).join('\n');
}

// --- (a) Every endpoint .cs basename appears in the inventory File column ---

test('every endpoint .cs file is listed in the inventory', () => {
  const fileColumn = new Set(inventoryRows.map((c) => c[2]));
  const missing = endpointFiles.filter((f) => !fileColumn.has(f));
  assert.deepEqual(missing, [], `endpoint files missing from inventory: ${missing.join(', ')}`);
});

// --- (b) Every distinct ApiClient route literal maps to a server route ---

test('every dashboard ApiClient route maps to a server route (no orphans)', () => {
  const litRe = /\$?"(\/?api\/[^"]*)"/g;
  const clientRoutes = new Set();
  for (let m; (m = litRe.exec(apiClientSrc)); ) {
    clientRoutes.add(normalize(m[1]));
  }

  assert.ok(clientRoutes.size > 0, 'no api/... route literals found in ApiClient.cs');
  assert.ok(serverSet.size > 0, 'no server routes derived from endpoint files');

  const orphans = [...clientRoutes].filter((r) => !serverSet.has(r));
  assert.deepEqual(
    orphans,
    [],
    `dashboard routes with no matching server route: ${orphans.join(', ')}`,
  );
});

// --- (c) Every Style=action route is documented in the accepted D044 list ---

test('every action-style route is in the accepted convention list', () => {
  const conv = conventionSection();
  const actionRows = inventoryRows.filter((c) => c[3] === 'action');

  assert.ok(actionRows.length > 0, 'no action-style routes found in inventory');

  const undocumented = [];
  for (const row of actionRows) {
    const segs = row[1].split('/').filter(Boolean).filter((s) => !s.startsWith('{'));
    const token = segs[segs.length - 1];
    if (!conv.includes(token)) undocumented.push(`${row[1]} (token="${token}")`);
  }
  assert.deepEqual(
    undocumented,
    [],
    `action routes not documented in the convention section: ${undocumented.join(', ')}`,
  );
});

// --- (d) Inventory route rows are consistent with the derived server set ---
// Guards against the inventory drifting from the real .cs routes: every Path in
// the table must be a real server route.

test('every inventory route row matches a real server route', () => {
  const bogus = [];
  for (const row of inventoryRows) {
    if (!serverSet.has(normalize(row[1]))) bogus.push(`${row[0]} ${row[1]}`);
  }
  assert.deepEqual(
    bogus,
    [],
    `inventory rows with no matching server route: ${bogus.join(', ')}`,
  );
});
