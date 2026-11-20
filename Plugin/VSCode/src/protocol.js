'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { samePath, verifyTarget } = require('./processes');

const MAX_JSON = 64 * 1024;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const ideDirectory = root => path.join(root, 'Library', 'IDE');

async function readJson(file) {
  const handle = await fs.open(file, 'r');
  try {
    if ((await handle.stat()).size > MAX_JSON) throw new Error(`IDE descriptor exceeds ${MAX_JSON} bytes.`);
    return JSON.parse((await handle.readFile('utf8')).replace(/^\uFEFF/, ''));
  } finally { await handle.close(); }
}

function inside(root, file) {
  const relative = path.relative(root, file);
  return relative !== '' && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

async function projectRoot(start) {
  let current = path.resolve(start);
  for (let i = 0; i < 48; i++) {
    try {
      const file = path.join(current, 'Project.yaml');
      const text = await fs.readFile(file, 'utf8');
      if (/^format:\s*BEngine\.Project\s*$/m.test(text)) return current;
    } catch { /* Continue through parents without creating anything. */ }
    const parent = path.dirname(current);
    if (parent === current) return null;
    current = parent;
  }
  return null;
}

async function readProject(root) {
  const data = await readJson(path.join(ideDirectory(root), 'project.json'));
  if (data.schemaVersion !== 1 || typeof data.projectPath !== 'string' ||
      !samePath(root, data.projectPath) || typeof data.solutionPath !== 'string' ||
      !inside(root, path.resolve(data.solutionPath)) || !/\.slnx?$/i.test(data.solutionPath)) {
    throw new Error('The BEngine IDE project descriptor is invalid. Synchronize the project from BEngine Editor.');
  }
  return { ...data, projectPath: root, projectName: String(data.projectName || path.basename(root)) };
}

function validTarget(data, root, filename) {
  return data && data.schemaVersion === 1 && Number.isSafeInteger(data.processId) && data.processId > 0 &&
    filename === `${data.processId}.json` && data.kind === 'editor' && data.runtime === 'coreclr' &&
    typeof data.projectPath === 'string' && samePath(root, data.projectPath) &&
    typeof data.executablePath === 'string' && path.isAbsolute(data.executablePath) &&
    typeof data.processStartTimeUtc === 'string' && Number.isFinite(Date.parse(data.processStartTimeUtc));
}

async function readTargets(root, inspector, log = () => {}) {
  const directory = path.join(ideDirectory(root), 'Targets');
  let names;
  try { names = await fs.readdir(directory); } catch (error) { if (error.code === 'ENOENT') return []; throw error; }
  const files = names.filter(name => /^\d+\.json$/.test(name));
  if (files.length > 128) throw new Error('Too many BEngine debug target files. Clean stale Library/IDE/Targets files.');
  const targets = [];
  // A small bound avoids starting an unbounded number of OS process probes.
  for (let i = 0; i < files.length; i += 4) {
    const batch = await Promise.all(files.slice(i, i + 4).map(async name => {
      try {
        const data = await readJson(path.join(directory, name));
        if (!validTarget(data, root, name)) { log(`Ignored invalid target ${name}`); return null; }
        if (!await verifyTarget(data, inspector)) { log(`Ignored expired target ${name}`); return null; }
        return { ...data, projectPath: root, projectName: String(data.projectName || path.basename(root)) };
      } catch (error) { log(`Ignored target ${name}: ${error.message}`); return null; }
    }));
    targets.push(...batch.filter(Boolean));
  }
  return targets;
}

/** Local file IPC: no shell command, project build, network listener or user-profile cache. */
async function synchronize(root, { timeoutMs = 45000, cancelled = () => false } = {}) {
  const requestId = randomUUID();
  const directory = ideDirectory(root);
  const requests = path.join(directory, 'Requests');
  const responses = path.join(directory, 'Responses');
  await fs.mkdir(requests, { recursive: true });
  const request = path.join(requests, `${requestId}.json`);
  const temporary = `${request}.tmp`;
  const response = path.join(responses, `${requestId}.json`);
  const deadline = Date.now() + timeoutMs;
  try {
    await fs.writeFile(temporary, JSON.stringify({ schemaVersion: 1, requestId, command: 'sync' }), { encoding: 'utf8', flag: 'wx' });
    await fs.rename(temporary, request);
    while (Date.now() < deadline) {
      if (cancelled()) { const error = new Error('BEngine project synchronization cancelled.'); error.code = 'CANCELLED'; throw error; }
      try {
        const result = await readJson(response);
        if (result.schemaVersion !== 1 || result.requestId !== requestId || typeof result.success !== 'boolean') {
          throw new Error('BEngine returned an invalid synchronization response.');
        }
        if (!result.success) throw new Error(String(result.error || 'BEngine project synchronization failed.'));
        if (typeof result.solutionPath !== 'string' || !inside(root, path.resolve(result.solutionPath)) || !/\.slnx?$/i.test(result.solutionPath)) {
          throw new Error('BEngine returned a solution outside the current project.');
        }
        await fs.access(result.solutionPath);
        return result.solutionPath;
      } catch (error) { if (error.code !== 'ENOENT') throw error; }
      await delay(150);
    }
    throw new Error('No response from BEngine Editor. Open this project in BEngine, install IDE Integration, then retry.');
  } finally {
    await Promise.all([temporary, request, response].map(file => fs.rm(file, { force: true }).catch(() => {})));
  }
}

module.exports = { readJson, projectRoot, readProject, readTargets, validTarget, synchronize, inside, ideDirectory };
