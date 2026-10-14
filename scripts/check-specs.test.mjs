// Tests for the spec gate (spec 009, AC-8). Each test builds a tiny repo in a temp folder:
// the clean repo passes; each broken variant fails with a message that names the problem.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { check } from './check-specs.mjs';

const SPEC = `# Spec 001: Something

- **Status:** Done

## Acceptance criteria

- **AC-1 It works.** Given this, then that.
- **AC-2 It also works.** Given this, then the other.

## Business rules

- **R-1** A rule.
`;

const PLAN = `# Plan 001

## Test plan (criterion to layer)

| Criterion | Unit | Component | Scenario / test |
| --- | --- | --- | --- |
| AC-1 It works | x | x | \`FooTests.Does_it\`; \`features/component/thing.feature\` |
| AC-2 It also works | | x | \`thing.feature\`; \`App.test.tsx\` |

## Risks
`;

const TASKS = `# Tasks 001

- [x] T1 Do it.
- [-] T2 Push it (deferred: no remote yet).
`;

const FEATURE = `# Spec: specs/001-something/spec.md (AC-1, AC-2, R-1)
@component @spec-001
Feature: Thing
`;

const CLEAN = {
  'specs/001-something/spec.md': SPEC,
  'specs/001-something/plan.md': PLAN,
  'specs/001-something/tasks.md': TASKS,
  'features/component/thing.feature': FEATURE,
  'tests/Foo.UnitTests/FooTests.cs': 'public class FooTests { [Fact] public void Does_it() { } }',
  'tests/Foo.UnitTests/Foo.UnitTests.csproj': '<Project><ItemGroup><PackageReference Include="NSubstitute" /></ItemGroup></Project>',
  'web/src/App.test.tsx': '// test',
};

let root;

function repo(overrides = {}) {
  root = mkdtempSync(path.join(tmpdir(), 'check-specs-'));
  for (const [file, content] of Object.entries({ ...CLEAN, ...overrides })) {
    if (content === null) continue;
    mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
    writeFileSync(path.join(root, file), content);
  }
  return root;
}

function problems(overrides) {
  return check(repo(overrides)).map((p) => `${p.rule} ${p.where}: ${p.message}`);
}

function assertOneProblem(overrides, rule, ...fragments) {
  const found = problems(overrides);
  assert.equal(found.length, 1, `expected one problem, got:\n${found.join('\n')}`);
  assert.ok(found[0].startsWith(rule), found[0]);
  for (const fragment of fragments) assert.ok(found[0].includes(fragment), `"${found[0]}" should mention "${fragment}"`);
}

afterEach(() => rmSync(root, { recursive: true, force: true }));

describe('check-specs', () => {
  it('passes a clean repo', () => {
    assert.deepEqual(problems(), []);
  });

  it('ignores specs that are not Done', () => {
    assert.deepEqual(problems({ 'specs/001-something/spec.md': SPEC.replace('Done', 'Ready'), 'specs/001-something/plan.md': '# Plan' }), []);
  });

  describe('AC-1 every criterion has a test', () => {
    it('fails when a criterion has no row', () =>
      assertOneProblem({ 'specs/001-something/plan.md': PLAN.replace(/\| AC-2 .*\n/, '') }, 'AC-1', 'specs/001-something', 'AC-2'));

    it('fails when a row marks no layer', () =>
      assertOneProblem({ 'specs/001-something/plan.md': PLAN.replace('| AC-2 It also works | | x |', '| AC-2 It also works | | |') }, 'AC-1', 'AC-2', 'no layer'));
  });

  describe('AC-2 named tests exist', () => {
    it('fails for a missing feature file', () =>
      assertOneProblem({ 'specs/001-something/plan.md': PLAN.replace('`thing.feature`', '`other.feature`') }, 'AC-2', 'other.feature'));

    it('fails for a missing feature path', () =>
      assertOneProblem({ 'specs/001-something/plan.md': PLAN.replace('features/component/thing.feature', 'features/e2e/thing.feature') }, 'AC-2', 'features/e2e/thing.feature'));

    it('fails for a missing test class', () =>
      assertOneProblem({ 'specs/001-something/plan.md': PLAN.replace('FooTests.Does_it', 'BarTests.Does_it') }, 'AC-2', 'BarTests'));

    it('fails for a missing test method', () =>
      assertOneProblem({ 'specs/001-something/plan.md': PLAN.replace('FooTests.Does_it', 'FooTests.Does_not') }, 'AC-2', 'FooTests.Does_not'));

    it('fails for a missing test file', () =>
      assertOneProblem({ 'web/src/App.test.tsx': null }, 'AC-2', 'App.test.tsx'));
  });

  describe('AC-3 features point at real criteria', () => {
    it('fails for an unknown spec', () =>
      assertOneProblem({ 'features/component/thing.feature': FEATURE.replace('# Spec: specs/001-something/spec.md (AC-1, AC-2, R-1)', '# Spec: specs/001-something/spec.md (AC-1, AC-2, R-1), specs/099-nothing/spec.md (AC-1)').replace('@spec-001', '@spec-001 @spec-099') }, 'AC-3', 'thing.feature', '099-nothing'));

    it('fails for an unknown criterion', () =>
      assertOneProblem({ 'features/component/thing.feature': FEATURE.replace('AC-2, R-1', 'AC-2, AC-9, R-1') }, 'AC-3', 'AC-9'));

    it('fails for a missing tag', () =>
      assertOneProblem({ 'features/component/thing.feature': FEATURE.replace(' @spec-001', '') }, 'AC-3', '@spec-001'));

    it('fails for a feature with no Spec header', () =>
      assertOneProblem({ 'features/component/thing.feature': FEATURE.replace(/^# Spec:.*\n/, '') }, 'AC-3', 'no "# Spec:" header'));
  });

  describe('AC-4 done means done', () => {
    it('fails for an open task', () =>
      assertOneProblem({ 'specs/001-something/tasks.md': TASKS + '- [ ] T3 Not done.\n' }, 'AC-4', 'T3'));

    it('fails for a task deferred without a reason', () =>
      assertOneProblem({ 'specs/001-something/tasks.md': TASKS.replace(' (deferred: no remote yet)', '') }, 'AC-4', 'T2', 'reason'));
  });

  describe('AC-5 banned packages', () => {
    it('fails for Moq', () =>
      assertOneProblem({ 'tests/Foo.UnitTests/Foo.UnitTests.csproj': '<Project><ItemGroup><PackageReference Include="Moq" /></ItemGroup></Project>' }, 'AC-5', 'Moq'));

    it('fails for AutoMapper in central package versions', () =>
      assertOneProblem({ 'Directory.Packages.props': '<Project><ItemGroup><PackageVersion Include="AutoMapper.Extensions.Microsoft.DependencyInjection" Version="1.0.0" /></ItemGroup></Project>' }, 'AC-5', 'AutoMapper'));
  });
});
