#!/usr/bin/env node
// Constraint gate (Constraint-Driven Development for AI agents).
// Enforces the quality floor across C# and TypeScript codebases:
//   1. No skipped tests (Fact(Skip=...), it.skip, describe.skip, xit)
//   2. No suppression comments (@ts-ignore, @ts-nocheck, pragma warning disable outside migrations/generated files)
//   3. No unimplemented stubs in src/ (NotImplementedException)
//   4. Central Package Management & MassTransit v8 pinning invariant
//   5. Banned packages (Moq, AutoMapper)
//
// Usage: node scripts/check-constraints.mjs [repo-root]

import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SKIP_DIRS = new Set([
  'node_modules',
  'bin',
  'obj',
  '.git',
  'dist',
  'test-results',
  'playwright-report',
  '.features-gen',
  '.specify',
]);

const IS_GENERATED_CS = (filePath) =>
  filePath.endsWith('.Designer.cs') ||
  filePath.endsWith('ModelSnapshot.cs') ||
  filePath.endsWith('.feature.cs') ||
  filePath.includes('/bin/') ||
  filePath.includes('/obj/') ||
  filePath.includes('/.features-gen/');

export function checkConstraints(root) {
  const problems = [];
  const report = (rule, where, message) => problems.push({ rule, where, message });
  const files = walk(root);
  const rel = (file) => path.relative(root, file).split(path.sep).join('/');

  for (const file of files) {
    const relPath = rel(file);

    // 1. Check C# source & tests
    if (file.endsWith('.cs') && !IS_GENERATED_CS(relPath)) {
      const content = readFileSync(file, 'utf8');

      // Check for skipped tests
      if (relPath.startsWith('tests/')) {
        const skipMatch = content.match(/\[(Fact|Theory)\s*\(\s*Skip\s*=/i);
        if (skipMatch) {
          report('FLOOR-SKIPPED-TEST', relPath, 'Skipped test found (Fact/Theory Skip)');
        }
      }

      // Check for suppression comments
      if (/#pragma\s+warning\s+disable/i.test(content)) {
        report('FLOOR-SUPPRESSION', relPath, 'Found #pragma warning disable in non-generated C# code');
      }

      // Check for unimplemented stubs in production code
      if (relPath.startsWith('src/')) {
        if (/throw\s+new\s+(System\.)?NotImplementedException/i.test(content)) {
          report('FLOOR-UNIMPLEMENTED', relPath, 'Unimplemented stub (NotImplementedException) in production code');
        }
      }
    }

    // 2. Check TypeScript / JavaScript source & tests (excluding check-constraints.mjs itself)
    if ((file.endsWith('.ts') || file.endsWith('.tsx') || file.endsWith('.js') || file.endsWith('.mjs')) &&
        !relPath.includes('node_modules') && !relPath.includes('dist/') && relPath !== 'scripts/check-constraints.mjs') {
      const content = readFileSync(file, 'utf8');

      // Check for skipped tests
      if (relPath.startsWith('tests/') || relPath.startsWith('web/') || relPath.startsWith('e2e/')) {
        if (/\b(test|it|describe)\.skip\b/.test(content) || /\b(xit|xdescribe)\s*\(/.test(content)) {
          report('FLOOR-SKIPPED-TEST', relPath, 'Skipped test found (.skip or xit/xdescribe)');
        }
      }

      // Check for suppression comments
      if (/@ts-ignore|@ts-nocheck/.test(content)) {
        report('FLOOR-SUPPRESSION', relPath, 'Found @ts-ignore or @ts-nocheck suppression');
      }

      // Check for unimplemented stubs in production web code
      if (relPath.startsWith('web/src/')) {
        if (/throw\s+new\s+Error\s*\(\s*['"]Not implemented['"]\s*\)/i.test(content)) {
          report('FLOOR-UNIMPLEMENTED', relPath, 'Unimplemented stub ("Not implemented") in web code');
        }
      }
    }

    // 3. Check Directory.Packages.props for MassTransit v8 pinning and banned packages
    if (path.basename(file) === 'Directory.Packages.props') {
      const content = readFileSync(file, 'utf8');
      const mtMatch = content.match(/<PackageVersion\s+Include="MassTransit[^"]*"\s+Version="([^"]+)"/);
      if (mtMatch && !mtMatch[1].startsWith('8.')) {
        report('CONSTRAINTS-MASSTRANSIT-PIN', relPath, `MassTransit must be pinned to v8 (found ${mtMatch[1]})`);
      }

      if (/Include="(Moq|AutoMapper)(\.[^"]*)?"/i.test(content)) {
        report('CONSTRAINTS-BANNED-PACKAGE', relPath, 'Banned package (Moq or AutoMapper) found in Directory.Packages.props');
      }
    }
  }

  return problems;
}

function walk(dir) {
  const result = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) result.push(...walk(full));
    else if (entry.isFile()) result.push(full);
  }
  return result;
}

const isMain = process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);
if (isMain) {
  const repoRoot = process.argv[2] ? path.resolve(process.argv[2]) : process.cwd();
  const problems = checkConstraints(repoRoot);
  if (problems.length > 0) {
    console.error(`Found ${problems.length} constraint violation(s):`);
    for (const p of problems) {
      console.error(`  [${p.rule}] ${p.where}: ${p.message}`);
    }
    process.exit(1);
  } else {
    console.log('All constraint floor checks passed: zero suppressions, zero skipped tests, zero stubs.');
  }
}
