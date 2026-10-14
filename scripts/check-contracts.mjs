#!/usr/bin/env node
// Contract gate (spec 014). Compares every contract in contracts/ with an earlier revision and
// fails when a change can break a client or service without a new major version, or when a
// contract changed without a new version at all. Reports every problem in one run; exits 1 if any.
//
//   node scripts/check-contracts.mjs [--base <rev>] [--head <rev>]
//
// --base defaults to HEAD; the comparison starts from the merge base of --base and the head, so
// changes made on the base branch since are not counted. Without --head, the working tree is the
// head. HTTP contracts (OpenAPI) are compared by oasdiff in Docker; AsyncAPI contracts by
// asyncapiBreaks below, over documents resolved by the AsyncAPI parser.
import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Parser, fromFile } from '@asyncapi/parser';
import { parse as parseYaml } from 'yaml';

export const OASDIFF_IMAGE = 'tufin/oasdiff:v1.15.3';

// oasdiff reports these as information only, but a client without a token now gets 401 (Q2).
const RAISED_HTTP_CHECKS = new Set(['api-global-security-added', 'api-security-added']);
const WARN = 2;

// ---------------------------------------------------------------------------------------------
// HTTP: oasdiff's changelog, all levels; warnings and errors are breaking, plus the raised checks.

export function httpBreaks(changelog) {
  return (changelog ?? [])
    .filter((change) => change.level >= WARN || RAISED_HTTP_CHECKS.has(change.id))
    .map((change) => (change.operation ? `${change.operation} ${change.path}: ${change.text}` : change.text));
}

export function runOasdiff(work, baseFile, headFile) {
  const output = execFileSync(
    'docker',
    ['run', '--rm', '-v', `${work}:/w`, OASDIFF_IMAGE, 'changelog', `/w/${baseFile}`, `/w/${headFile}`, '--format', 'json'],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
  ).trim();
  return output && output !== 'null' ? JSON.parse(output) : [];
}

// ---------------------------------------------------------------------------------------------
// AsyncAPI: messages on a broker are written by one version and read by another during a deploy,
// so a payload change counts if it can break either side (AC-2).

const fmt = (value) => (value === undefined ? 'none' : JSON.stringify(value));
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

export function schemaBreaks(before, after, at) {
  if (!before) return [];
  if (!after) return [`${at}: removed`];

  const breaks = [];
  for (const keyword of ['type', 'format', 'const']) {
    if (!same(before[keyword], after[keyword])) breaks.push(`${at}: ${keyword} changed from ${fmt(before[keyword])} to ${fmt(after[keyword])}`);
  }

  if (before.enum || after.enum) {
    const was = new Set((before.enum ?? []).map(fmt));
    const now = new Set((after.enum ?? []).map(fmt));
    const removed = [...was].filter((value) => !now.has(value));
    const added = [...now].filter((value) => !was.has(value));
    if (removed.length) breaks.push(`${at}: allowed values removed: ${removed.join(', ')}`);
    if (added.length) breaks.push(`${at}: allowed values added: ${added.join(', ')} (consumers may not know them, Q4)`);
  }

  for (const keyword of ['oneOf', 'anyOf', 'allOf']) {
    const [was, now] = [before[keyword], after[keyword]];
    if (same(was, now)) continue;
    // Same number of choices: compare them one by one; otherwise the set of choices changed.
    if (was?.length && was.length === now?.length) was.forEach((choice, i) => breaks.push(...schemaBreaks(choice, now[i], `${at}(${keyword} ${i + 1})`)));
    else breaks.push(`${at}: ${keyword} changed`);
  }

  if (before.additionalProperties !== false && after.additionalProperties === false) {
    breaks.push(`${at}: no longer allows other fields`);
  }

  const wasRequired = new Set(before.required ?? []);
  const nowRequired = new Set(after.required ?? []);
  const props = after.properties ?? {};
  for (const [name, schema] of Object.entries(before.properties ?? {})) {
    breaks.push(...schemaBreaks(schema, props[name], `${at}.${name}`));
  }
  for (const name of nowRequired) if (!wasRequired.has(name)) breaks.push(`${at}.${name}: now required`);
  for (const name of wasRequired) if (!nowRequired.has(name) && name in props) breaks.push(`${at}.${name}: no longer required`);

  if (before.items) breaks.push(...schemaBreaks(before.items, after.items, `${at}[]`));
  return breaks;
}

/** Breaking edits to the payloads of messages that are in both versions. */
export function payloadBreaks(before, after) {
  const breaks = [];
  for (const [key, channel] of Object.entries(before.channels ?? {})) {
    const messages = after.channels?.[key]?.messages ?? {};
    for (const [name, message] of Object.entries(channel.messages ?? {})) {
      if (messages[name]) breaks.push(...schemaBreaks(message.payload, messages[name].payload, name));
    }
  }

  // A message used on several channels is reported once.
  return [...new Set(breaks)];
}

export function asyncapiBreaks(before, after) {
  const breaks = [];
  const channels = after.channels ?? {};
  for (const [key, channel] of Object.entries(before.channels ?? {})) {
    const next = channels[key];
    if (!next) {
      breaks.push(`channel ${key}: removed`);
      continue;
    }
    if (channel.address !== next.address) breaks.push(`channel ${key}: address changed from ${fmt(channel.address)} to ${fmt(next.address)}`);

    const messages = next.messages ?? {};
    for (const name of Object.keys(channel.messages ?? {})) {
      if (!messages[name]) breaks.push(`channel ${key}: message ${name} removed`);
    }
  }
  breaks.push(...payloadBreaks(before, after));

  const operations = after.operations ?? {};
  for (const [key, operation] of Object.entries(before.operations ?? {})) {
    const next = operations[key];
    if (!next) breaks.push(`operation ${key}: removed`);
    else if (operation.action !== next.action) breaks.push(`operation ${key}: action changed from ${operation.action} to ${next.action}`);
  }

  return breaks;
}

// ---------------------------------------------------------------------------------------------
// Versions (R-1): semantic versioning; below 1.0.0 the minor version counts as the major one.

const parts = (version) => String(version).split('-')[0].split('.').map((n) => Number.parseInt(n, 10) || 0);

export function compareVersions(a, b) {
  const [x, y] = [parts(a), parts(b)];
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] < y[i] ? -1 : 1;
  return 0;
}

export function majorRaised(before, after) {
  const [b, a] = [parts(before), parts(after)];
  if (b[0] > 0) return a[0] > b[0];
  if (b[1] > 0) return a[0] > 0 || a[1] > b[1];
  return compareVersions(after, before) > 0;
}

export function versionProblems({ before, after, changed, breaks }) {
  const problems = [];
  if (compareVersions(after, before) < 0) problems.push(`version went down from ${before} to ${after}`);
  else if (changed && compareVersions(after, before) === 0) problems.push(`changed, but the version is still ${after} (AC-4)`);
  if (breaks.length && !majorRaised(before, after)) {
    problems.push(`${breaks.length} breaking change(s) need a new major version (${before} to ${after}):`, ...breaks.map((b) => `  - ${b}`));
  }
  return problems;
}

// ---------------------------------------------------------------------------------------------

const git = (root, args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });

function revisionExists(root, rev) {
  try {
    git(root, ['rev-parse', '--verify', '--quiet', `${rev}^{commit}`]);
    return true;
  } catch {
    return false;
  }
}

/** Writes contracts/ as it is at a revision into dir. */
function materialize(root, rev, dir) {
  for (const file of git(root, ['ls-tree', '-r', '--name-only', rev, '--', 'contracts']).split('\n').filter(Boolean)) {
    const target = path.join(dir, file);
    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, execFileSync('git', ['show', `${rev}:${file}`], { cwd: root }));
  }
}

function contractFiles(dir) {
  const found = [];
  const walk = (folder) => {
    if (!existsSync(folder)) return;
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const full = path.join(folder, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (/\.ya?ml$/.test(entry.name)) {
        const doc = parseYaml(readFileSync(full, 'utf8'));
        if (doc?.openapi || doc?.asyncapi) found.push(path.relative(dir, full).split(path.sep).join('/'));
      }
    }
  };
  walk(path.join(dir, 'contracts'));
  return found;
}

const withoutParserKeys = (value) =>
  Array.isArray(value)
    ? value.map(withoutParserKeys)
    : value && typeof value === 'object'
      ? Object.fromEntries(Object.entries(value).filter(([k]) => !k.startsWith('x-parser-')).map(([k, v]) => [k, withoutParserKeys(v)]))
      : value;

async function loadAsyncapi(file) {
  const { document, diagnostics } = await fromFile(new Parser(), file).parse();
  if (!document) throw new Error(`${file} does not parse: ${diagnostics.map((d) => d.message).join('; ')}`);
  return withoutParserKeys(document.json());
}

const withoutVersion = (doc) => ({ ...doc, info: { ...doc.info, version: undefined } });

// Events between services are facts: a breaking change is a new version of the event, not an
// edit, whatever the document's version says (constitution IV). Retiring a message is allowed
// with a new major version, like any other breaking change.
const PUBLISHED_EVENTS = /^contracts\/events\//;

/**
 * Compares the contracts at base with the head (a revision, or the working tree when head is
 * omitted). Returns problems (fail the gate) and notes (breaking changes a new major allowed).
 */
export async function checkContracts({ root, base = 'HEAD', head, oasdiff = runOasdiff }) {
  if (!revisionExists(root, base) || (head && !revisionExists(root, head))) {
    return { problems: [], notes: [`Nothing to compare: ${base}${head ? ` or ${head}` : ''} is not a commit here.`] };
  }
  const from = git(root, ['merge-base', base, head ?? 'HEAD']).trim();

  const work = mkdtempSync(path.join(tmpdir(), 'contracts-'));
  try {
    materialize(root, from, path.join(work, 'base'));
    if (head) materialize(root, head, path.join(work, 'head'));
    else if (existsSync(path.join(root, 'contracts'))) cpSync(path.join(root, 'contracts'), path.join(work, 'head', 'contracts'), { recursive: true });

    const problems = [];
    const notes = [];
    const headFiles = new Set(contractFiles(path.join(work, 'head')));
    for (const file of contractFiles(path.join(work, 'base'))) {
      if (!headFiles.has(file)) {
        problems.push(`${file}: removed, which breaks every client and service that uses it`);
        continue;
      }

      const [baseFile, headFile] = [path.join(work, 'base', file), path.join(work, 'head', file)];
      const raw = [parseYaml(readFileSync(baseFile, 'utf8')), parseYaml(readFileSync(headFile, 'utf8'))];
      let before, after, breaks;
      let edits = [];
      if (raw[0].openapi) {
        [before, after] = raw;
        breaks = httpBreaks(oasdiff(work, `base/${file}`, `head/${file}`));
      } else {
        [before, after] = [await loadAsyncapi(baseFile), await loadAsyncapi(headFile)];
        breaks = asyncapiBreaks(before, after);
        if (PUBLISHED_EVENTS.test(file)) edits = payloadBreaks(before, after);
      }

      const versions = { before: before.info.version, after: after.info.version };
      const changed = !same(withoutVersion(before), withoutVersion(after));
      const found = versionProblems({ ...versions, changed, breaks });
      if (edits.length) {
        found.push('published events edited in a breaking way; publish a new version of the event instead (constitution IV):', ...edits.map((e) => `  - ${e}`));
      }
      if (found.length) problems.push(`${file}:`, ...found.map((p) => `  ${p}`));
      else if (breaks.length) notes.push(`${file}: ${breaks.length} breaking change(s), allowed by the new major version (${versions.before} to ${versions.after}):`, ...breaks.map((b) => `  - ${b}`));
    }
    return { problems, notes, from };
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const arg = (name) => {
    const i = process.argv.indexOf(name);
    return i > 0 ? process.argv[i + 1] : undefined;
  };
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const { problems, notes, from } = await checkContracts({ root, base: arg('--base') ?? 'HEAD', head: arg('--head') });
  const against = from ? ` against ${from.slice(0, 7)}` : '';
  for (const note of notes) console.log(note);
  if (problems.length) {
    console.error(`Contract problems${against}:`);
    for (const problem of problems) console.error(problem);
    process.exit(1);
  }
  console.log(`Contracts are compatible${against}, or carry a new major version.`);
}
