import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import libCoverage from 'istanbul-lib-coverage';
import libReport from 'istanbul-lib-report';
import reports from 'istanbul-reports';
import v8toIstanbul from 'v8-to-istanbul';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = process.env.REPO_ROOT
  ? path.resolve(process.env.REPO_ROOT)
  : path.resolve(here, '../../../..');
const frontendRoot = process.env.FRONTEND_ROOT
  ? path.resolve(process.env.FRONTEND_ROOT)
  : path.resolve(repoRoot, '../example-app-frontend');
const rawPath =
  process.env.V8_RAW ??
  path.join(repoRoot, 'artifacts/coverage/e2e-js/raw/v8-coverage.json');
const outDir = process.env.V8_OUT ?? path.join(repoRoot, 'artifacts/coverage/e2e-js');

function warn(message) {
  console.warn(`[v8-to-lcov] ${message}`);
}

function shouldSkipScript(url) {
  if (!url) {
    return true;
  }
  const file = url.split('/').pop() ?? '';
  if (file.includes('vendor') || file.startsWith('runtime.')) {
    return true;
  }
  if (url.includes('node_modules') || url.includes('react-refresh')) {
    return true;
  }
  return false;
}

function srcRelativeFromMappedPath(filePath) {
  const normalized = filePath.replaceAll('\\', '/').split('?')[0];
  const markers = [
    '/example-app-frontend/src/',
    '/example-app-frontend/./src/',
    'webpack://example-app-frontend/./src/',
  ];
  for (const marker of markers) {
    const index = normalized.indexOf(marker);
    if (index !== -1) {
      return normalized.slice(index + marker.length);
    }
  }

  const match = normalized.match(
    /\/src\/((?:app|pages|shared|widgets|features|entities)\/.*)$/,
  );
  return match ? match[1] : null;
}

function toFrontendSrcFile(filePath) {
  const relative = srcRelativeFromMappedPath(filePath);
  if (!relative) {
    return null;
  }
  if (
    relative.includes('node_modules') ||
    relative.includes('shared/api/generated/') ||
    relative.includes('shared/lib/test/') ||
    relative.endsWith('.css') ||
    relative.endsWith('.scss') ||
    relative.endsWith('.d.ts')
  ) {
    return null;
  }

  const resolved = path.resolve(frontendRoot, 'src', relative);
  const srcRoot = path.resolve(frontendRoot, 'src');
  if (resolved !== srcRoot && !resolved.startsWith(`${srcRoot}${path.sep}`)) {
    return null;
  }
  return resolved;
}

async function mapEntry(entry, map) {
  if (shouldSkipScript(entry.url)) {
    return;
  }

  let sourceMap;
  if (entry.sourceMap) {
    try {
      sourceMap = { sourcemap: JSON.parse(entry.sourceMap) };
    } catch {
      warn(`Could not parse source map for ${entry.url}`);
    }
  }

  const converter = v8toIstanbul(entry.url ?? '', 0, {
    source: entry.source,
    sourceMap,
  });
  await converter.load();
  converter.applyCoverage(entry.functions ?? []);
  const istanbul = converter.toIstanbul();
  for (const [file, coverage] of Object.entries(istanbul)) {
    const dest = toFrontendSrcFile(file);
    if (!dest) {
      continue;
    }
    const data = coverage.data ?? coverage;
    map.merge({
      [dest]: {
        ...data,
        path: dest,
      },
    });
  }
}

async function main() {
  if (!fs.existsSync(rawPath)) {
    warn(`No raw coverage at ${rawPath}; skipping lcov.`);
    process.exit(0);
  }

  const entries = JSON.parse(fs.readFileSync(rawPath, 'utf8'));
  if (!Array.isArray(entries) || entries.length === 0) {
    warn('Raw coverage is empty; skipping lcov.');
    process.exit(0);
  }

  const map = libCoverage.createCoverageMap({});
  for (const entry of entries) {
    try {
      await mapEntry(entry, map);
    } catch (err) {
      warn(`Failed to map ${entry?.url ?? '(unknown)'}: ${err.message}`);
    }
  }

  const files = map.files();
  if (files.length === 0) {
    warn('No frontend src files after mapping; keeping raw V8 JSON only.');
    process.exit(0);
  }

  fs.mkdirSync(outDir, { recursive: true });
  const context = libReport.createContext({
    dir: outDir,
    coverageMap: map,
  });
  reports.create('lcovonly', { file: 'lcov.info' }).execute(context);
  reports.create('text-summary').execute(context);
  console.log(`Wrote ${path.join(outDir, 'lcov.info')} (${files.length} files)`);
}

main().catch((err) => {
  warn(err.message);
  process.exit(0);
});
