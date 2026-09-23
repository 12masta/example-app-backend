#!/usr/bin/env node

/**
 * build-security-manifest.mjs
 *
 * Walks an artifact directory, hashes files (SHA256), and writes
 * analysis-manifest.json for the security-test-analysis agent.
 *
 * Usage:
 *   node build-security-manifest.mjs \
 *     --agent security-test-analysis \
 *     --repository <owner/repo> \
 *     --workflow-run-id <runId> \
 *     --run-attempt <attempt> \
 *     --workflow <workflow-name> \
 *     --job <job-name> \
 *     --commit <sha> \
 *     --conclusion <status> \
 *     --out-dir <path>
 */

import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, readdirSync, statSync, mkdirSync } from 'node:fs';
import { join, relative } from 'node:path';

const MAX_LOG_BYTES = 512 * 1024; // 512 KB

function parseArgs() {
  const args = process.argv.slice(2);
  const parsed = {};

  for (let i = 0; i < args.length; i++) {
    if (args[i].startsWith('--')) {
      const key = args[i].slice(2);

      if (key === 'help') {
        parsed[key] = true;
        continue;
      }

      const value = args[++i];
      if (!value || value.startsWith('--')) {
        throw new Error(`Missing value for --${key}`);
      }
      parsed[key] = value;
    }
  }

  if (parsed.help) {
    return parsed;
  }

  const required = [
    'agent',
    'repository',
    'workflow-run-id',
    'run-attempt',
    'workflow',
    'job',
    'commit',
    'conclusion',
    'out-dir',
  ];

  for (const key of required) {
    if (!parsed[key]) {
      throw new Error(`Missing required argument: --${key}`);
    }
  }

  return parsed;
}

function walkDir(dir, fileList = [], baseDir = dir) {
  const files = readdirSync(dir);

  for (const file of files) {
    const filePath = join(dir, file);
    const stat = statSync(filePath);

    if (stat.isDirectory()) {
      walkDir(filePath, fileList, baseDir);
    } else {
      fileList.push({
        absolute: filePath,
        relative: relative(baseDir, filePath),
      });
    }
  }

  return fileList;
}

function hashFile(path, maxBytes = null) {
  let content = readFileSync(path);
  if (maxBytes !== null && content.length > maxBytes) {
    content = content.subarray(content.length - maxBytes);
  }
  const hash = createHash('sha256');
  hash.update(content);
  return hash.digest('hex');
}

function inferRole(relativePath) {
  const normalized = relativePath.toLowerCase().replace(/\\/g, '/');

  // Required roles
  if (
    normalized === 'zap/report.json' ||
    normalized.endsWith('/zap/report.json') ||
    (normalized.includes('zap/') && normalized.endsWith('report.json'))
  ) {
    return 'zap-json';
  }
  if (
    normalized === 'openapi/openapi.json' ||
    normalized.endsWith('/openapi/openapi.json') ||
    (normalized.includes('openapi/') && normalized.endsWith('.json'))
  ) {
    return 'openapi';
  }
  if (normalized.includes('workflow/metadata.json')) {
    return 'workflow-metadata';
  }

  // Optional roles
  if (
    normalized.includes('zap/') &&
    (normalized.endsWith('.html') || normalized.includes('report.html'))
  ) {
    return 'zap-html';
  }
  if (
    normalized.includes('zap/') &&
    (normalized.endsWith('.sarif') || normalized.endsWith('.sarif.json'))
  ) {
    return 'zap-sarif';
  }
  if (normalized.includes('zap/') && normalized.includes('log')) {
    return 'zap-log';
  }
  if (
    (normalized.includes('api') && normalized.includes('log')) ||
    (normalized.includes('backend') && normalized.includes('log'))
  ) {
    return 'api-log';
  }

  return 'supplemental';
}

function buildManifest(args) {
  const outDir = args['out-dir'];
  const agentSlug = args.agent;
  const manifestPath = join(outDir, 'analysis-manifest.json');

  const allFiles = walkDir(outDir);
  const files = allFiles.filter((f) => f.absolute !== manifestPath);

  if (files.length === 0) {
    console.warn('Warning: No files found to include in manifest');
  }

  for (const file of files) {
    if (file.relative.startsWith('/') || file.relative.match(/^[A-Z]:\\/i)) {
      throw new Error(`Absolute path detected: ${file.relative}`);
    }
  }

  const entries = files.map((file) => {
    const role = inferRole(file.relative);
    const stat = statSync(file.absolute);

    const maxBytes =
      role === 'api-log' || role === 'zap-log' ? MAX_LOG_BYTES : null;
    const sha256 = hashFile(file.absolute, maxBytes);

    return {
      path: file.relative,
      role,
      sha256,
      bytes: stat.size,
    };
  });

  const workflowMetadata = {
    repository: args.repository,
    workflowRunId: parseInt(args['workflow-run-id'], 10),
    runAttempt: parseInt(args['run-attempt'], 10),
    workflow: args.workflow,
    job: args.job,
    commit: args.commit,
    conclusion: args.conclusion,
  };

  const workflowMetadataPath = join(outDir, 'workflow', 'metadata.json');
  const workflowDir = join(outDir, 'workflow');

  try {
    mkdirSync(workflowDir, { recursive: true });
    writeFileSync(workflowMetadataPath, JSON.stringify(workflowMetadata, null, 2));

    const workflowStat = statSync(workflowMetadataPath);
    entries.push({
      path: 'workflow/metadata.json',
      role: 'workflow-metadata',
      sha256: hashFile(workflowMetadataPath),
      bytes: workflowStat.size,
    });
  } catch (err) {
    console.error('Failed to write workflow metadata:', err.message);
    throw err;
  }

  const manifest = {
    schemaVersion: 1,
    agentSlug,
    files: entries,
  };

  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
  console.log(`Manifest written to ${manifestPath}`);
  console.log(`Total files: ${entries.length}`);

  const roleCounts = {};
  for (const entry of entries) {
    roleCounts[entry.role] = (roleCounts[entry.role] || 0) + 1;
  }
  console.log('Files by role:', roleCounts);

  const requiredRoles = ['zap-json', 'openapi', 'workflow-metadata'];
  const presentRoles = new Set(entries.map((e) => e.role));

  for (const role of requiredRoles) {
    if (!presentRoles.has(role)) {
      console.warn(`Warning: Required role '${role}' is missing`);
    }
  }
}

try {
  const args = parseArgs();

  if (args.help) {
    console.log(`
Usage: node build-security-manifest.mjs [options]

Required options:
  --agent <slug>              Agent slug (e.g., security-test-analysis)
  --repository <owner/repo>   GitHub repository
  --workflow-run-id <id>      Workflow run ID
  --run-attempt <num>         Run attempt number
  --workflow <name>           Workflow name
  --job <name>                Job name
  --commit <sha>              Commit SHA
  --conclusion <status>       Job conclusion (success, failure, etc.)
  --out-dir <path>            Output directory for manifest and files

Optional:
  --help                      Show this help message
`);
    process.exit(0);
  }

  buildManifest(args);
} catch (err) {
  console.error('Error:', err.message);
  process.exit(1);
}
