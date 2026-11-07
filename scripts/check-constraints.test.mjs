import test from 'node:test';
import assert from 'node:assert/strict';
import { checkConstraints } from './check-constraints.mjs';

test('checkConstraints passes on clean repository', () => {
  const problems = checkConstraints(process.cwd());
  assert.equal(problems.length, 0, `Expected 0 problems, found: ${JSON.stringify(problems, null, 2)}`);
});
