#!/usr/bin/env node
// Spec gate (spec 009). Checks that specs, plans, feature files and tests agree, and that no
// banned package is referenced. Reports every problem in one run; exits 1 if there is any.
//
//   node scripts/check-specs.mjs [repo-root]
//
// Rules (spec 009 criteria):
//   AC-1 A Done spec's every AC has a test-plan row with at least one layer marked ("x").
//   AC-2 Backticked references in test-plan rows exist: path/to/file, name.feature, *.ts(x)/*.mjs
//        file names, and .NET test classes/methods (FooTests, FooTests.Method).
//   AC-3 Each feature file has a "# Spec:" header naming existing specs and criteria, and the
//        matching @spec-NNN tag.
//   AC-4 A Done spec has no open task; a deferred task ("- [-]") says "(deferred: reason)".
//   AC-5 No Moq or AutoMapper package reference (organization policy, constitution VI).
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SKIP_DIRS = new Set(['node_modules', 'bin', 'obj', '.git', 'dist', 'test-results', 'playwright-report', '.features-gen']);
const BANNED = /Include="(Moq|AutoMapper)(\.[^"]*)?"/gi;

export function check(root) {
  const problems = [];
  const report = (rule, where, message) => problems.push({ rule, where, message });
  const files = walk(root);
  const rel = (file) => path.relative(root, file).split(path.sep).join('/');

  const specs = readSpecs(root);
  const testSources = files.filter((f) => f.endsWith('.cs') && rel(f).startsWith('tests/')).map((f) => readFileSync(f, 'utf8'));
  const fileNames = new Set(files.map((f) => path.basename(f)));
  const featureNames = new Set(files.filter((f) => f.endsWith('.feature') && rel(f).startsWith('features/')).map((f) => path.basename(f)));

  for (const spec of specs.values()) {
    const where = `specs/${spec.dir}`;
    if (spec.status !== 'Done') continue;

    // AC-1: every criterion has a row with a layer marked.
    const rows = testPlanRows(read(path.join(root, 'specs', spec.dir, 'plan.md')));
    for (const ac of spec.criteria) {
      const row = rows.find((r) => r.ids.includes(ac));
      if (!row) report('AC-1', where, `${ac} has no row in the plan's test plan`);
      else if (!row.layered) report('AC-1', where, `${ac} has no layer marked in the plan's test plan`);
    }

    // AC-2: references in the test plan exist.
    for (const ref of rows.flatMap((r) => r.refs)) {
      const missing = missingReference(ref, { root, featureNames, fileNames, testSources });
      if (missing) report('AC-2', `${where}/plan.md`, `${missing} does not exist`);
    }

    // AC-4: done means done.
    for (const line of read(path.join(root, 'specs', spec.dir, 'tasks.md')).split('\n')) {
      const task = line.match(/^- \[( |-)\] (T\d+)/);
      if (task?.[1] === ' ') report('AC-4', `${where}/tasks.md`, `${task[2]} is open but the spec is Done`);
      if (task?.[1] === '-' && !/\(deferred: [^)]+\)/.test(line)) report('AC-4', `${where}/tasks.md`, `${task[2]} is deferred without a reason "(deferred: ...)"`);
    }
  }

  // AC-3: feature files point at real specs and criteria, and are tagged.
  for (const file of files.filter((f) => f.endsWith('.feature') && rel(f).startsWith('features/'))) {
    const text = readFileSync(file, 'utf8');
    const where = rel(file);
    const headers = text.split('\n').filter((l) => l.startsWith('# Spec:'));
    if (headers.length === 0) {
      report('AC-3', where, 'has no "# Spec:" header');
      continue;
    }

    for (const [, dir, ids = ''] of headers.join(' ').matchAll(/specs\/([\w-]+)\/spec\.md(?: \(([^)]*)\))?/g)) {
      const spec = specs.get(dir);
      if (!spec) {
        report('AC-3', where, `names specs/${dir}, which does not exist`);
        continue;
      }

      for (const id of ids.match(/\b(AC|R)-\d+\b/g) ?? []) {
        if (!spec.criteria.includes(id) && !spec.rules.includes(id)) report('AC-3', where, `names ${id}, which specs/${dir} does not define`);
      }

      const tag = `@spec-${dir.slice(0, 3)}`;
      if (!new RegExp(`${tag}\\b`).test(text)) report('AC-3', where, `names specs/${dir} but has no ${tag} tag`);
    }
  }

  // AC-5: banned packages.
  for (const file of files.filter((f) => /\.(csproj|props|targets)$/.test(f))) {
    for (const [, name] of readFileSync(file, 'utf8').matchAll(BANNED)) {
      report('AC-5', rel(file), `references banned package ${name} (constitution VI)`);
    }
  }

  return problems;
}

function readSpecs(root) {
  const specs = new Map();
  const dir = path.join(root, 'specs');
  if (!existsSync(dir)) return specs;
  for (const name of readdirSync(dir)) {
    const text = read(path.join(dir, name, 'spec.md'));
    if (!text) continue;
    specs.set(name, {
      dir: name,
      status: text.match(/^- \*\*Status:\*\* (\w+)/m)?.[1],
      criteria: [...text.matchAll(/^- \*\*(AC-\d+)\b/gm)].map((m) => m[1]),
      rules: [...text.matchAll(/^- \*\*(R-\d+)\b/gm)].map((m) => m[1]),
    });
  }
  return specs;
}

/** Rows of the "## Test plan" table: criterion IDs, whether a layer is marked, backticked references. */
function testPlanRows(plan) {
  const section = plan.split(/^## /m).find((s) => s.startsWith('Test plan')) ?? '';
  return section
    .split('\n')
    .filter((line) => line.startsWith('|'))
    .map((line) => line.split('|').slice(1, -1).map((cell) => cell.trim()))
    .filter((cells) => cells.length > 2 && !/^-+$/.test(cells[0]))
    .map((cells) => ({
      ids: cells[0].match(/\bAC-\d+\b/g) ?? [],
      layered: cells.slice(1, -1).some((cell) => cell.toLowerCase() === 'x'),
      refs: [...cells.at(-1).matchAll(/`([^`]+)`/g)].map((m) => m[1]),
    }));
}

/** The reference as written if it cannot be found; null if it exists or is not a checkable kind. */
function missingReference(ref, { root, featureNames, fileNames, testSources }) {
  if (ref.includes('/') && /\.\w+$/.test(ref)) return existsSync(path.join(root, ref)) ? null : ref;
  if (ref.endsWith('.feature')) return featureNames.has(ref) ? null : ref;
  if (/\.(tsx?|mjs)$/.test(ref)) return fileNames.has(ref) ? null : ref;

  const test = ref.match(/^([A-Z]\w*Tests)(?:\.(\w+))?$/);
  if (!test) return null;
  const [, cls, method] = test;
  const sources = testSources.filter((s) => new RegExp(`\\bclass ${cls}\\b`).test(s));
  if (sources.length === 0) return cls;
  if (method && !sources.some((s) => new RegExp(`\\b${method}\\s*\\(`).test(s))) return ref;
  return null;
}

function walk(dir) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else out.push(full);
  }
  return out;
}

function read(file) {
  return existsSync(file) ? readFileSync(file, 'utf8') : '';
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const root = path.resolve(process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
  const problems = check(root);
  for (const p of problems) console.error(`[${p.rule}] ${p.where}: ${p.message}`);
  console.log(problems.length === 0 ? 'Specs, plans, features and tests agree.' : `${problems.length} problem(s).`);
  process.exit(problems.length === 0 ? 0 : 1);
}
