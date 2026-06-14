import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

// Falsifiable guard for the M006 single-product walkthrough record (D048/D039).
// Reads ONLY tracked markdown siblings (the walkthrough + the catalog) — never
// .gsd/ or other gitignored paths. Fails loudly if the recorded walkthrough
// drops a journey step, stops citing the runtime journey test, lets a catalog
// FLAG row sneak back, or omits the autonomous-mode deviation note.

const __dirname = dirname(fileURLToPath(import.meta.url));
const WALKTHROUGH_PATH = join(__dirname, 'm006-walkthrough.md');
const CATALOG_PATH = join(__dirname, 'ux-consistency-catalog.md');

const walkthrough = readFileSync(WALKTHROUGH_PATH, 'utf8');

// --- (a) Every canonical journey step is named in the record ---
// Each entry is a list of acceptable literal phrases (case-insensitive); the
// step passes if any one of its phrases appears.

const JOURNEY_STEPS = [
  ['Login'],
  ['Host list'],
  ['Host detail'],
  ['Manifest config', 'manifest'],
  ['Group management', 'group'],
  ['Override edit'],
];

test('the walkthrough names every canonical journey step', () => {
  const haystack = walkthrough.toLowerCase();
  const missing = [];
  for (const phrases of JOURNEY_STEPS) {
    const found = phrases.some((p) => haystack.includes(p.toLowerCase()));
    if (!found) missing.push(phrases.join(' / '));
  }
  assert.deepEqual(
    missing,
    [],
    `walkthrough is missing journey step(s): ${missing.join(', ')}`,
  );
});

// --- (b) The record cites the runtime operator-journey integration test ---

test('the walkthrough references OperatorJourneyTests', () => {
  assert.match(
    walkthrough,
    /OperatorJourneyTests/,
    'walkthrough must cite the runtime OperatorJourneyTests as the journey proof',
  );
});

// --- (c) The catalog has zero unresolved (FLAG) rows ---
// Read the sibling tracked catalog markdown and assert no `| FLAG |` row remains.

test('the UX consistency catalog has zero unresolved (FLAG) rows', () => {
  const catalog = readFileSync(CATALOG_PATH, 'utf8');
  const flagRows = catalog
    .split('\n')
    .filter((line) => /\|\s*FLAG\s*\|/.test(line));
  assert.deepEqual(
    flagRows,
    [],
    `catalog still has FLAG row(s):\n${flagRows.join('\n')}`,
  );
});

// --- (d) The autonomous-mode deviation is named (and cites D048) ---

test('the walkthrough names the autonomous-mode deviation and cites D048', () => {
  assert.match(
    walkthrough,
    /deviation/i,
    'walkthrough must name the autonomous-mode deviation',
  );
  assert.match(
    walkthrough,
    /D048/,
    'walkthrough must cite D048 for the autonomous-mode substitution',
  );
});
