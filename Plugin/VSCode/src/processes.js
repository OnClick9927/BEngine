'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const run = promisify(execFile);

function samePath(a, b, platform = process.platform) {
  const normalize = value => path.resolve(value).replace(/[\\/]+$/, '');
  return platform === 'win32' ? normalize(a).toLowerCase() === normalize(b).toLowerCase() : normalize(a) === normalize(b);
}

/** Read identity at attach time. Never attach merely because a PID still exists. */
async function inspectProcess(processId) {
  if (!Number.isSafeInteger(processId) || processId <= 0) return null;
  try {
    if (process.platform === 'win32') {
      // The only interpolation is a validated positive integer, never project text.
      const script = `$ErrorActionPreference='Stop'; $p=Get-Process -Id ${processId}; [pscustomobject]@{processId=$p.Id;processStartTimeUtc=$p.StartTime.ToUniversalTime().ToString('o');executablePath=$p.Path}|ConvertTo-Json -Compress`;
      const { stdout } = await run('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', script],
        { windowsHide: true, timeout: 6000, maxBuffer: 64 * 1024 });
      return JSON.parse(stdout.trim().replace(/^\uFEFF/, ''));
    }
    const { stdout } = await run('ps', ['-p', String(processId), '-o', 'lstart=', '-o', 'comm='],
      { timeout: 6000, maxBuffer: 64 * 1024, env: { ...process.env, LC_ALL: 'C', TZ: 'UTC' } });
    const text = stdout.trim();
    const match = /^(\w{3}\s+\w{3}\s+\d+\s+\d\d:\d\d:\d\d\s+\d{4})\s+(.+)$/.exec(text);
    if (!match) return null;
    const executablePath = process.platform === 'linux' ? await fs.readlink(`/proc/${processId}/exe`) : match[2];
    return { processId, processStartTimeUtc: new Date(`${match[1]} UTC`).toISOString(), executablePath };
  } catch { return null; }
}

async function verifyTarget(target, inspector = inspectProcess, platform = process.platform) {
  const actual = await inspector(target.processId);
  if (!actual || actual.processId !== target.processId || !actual.executablePath) return false;
  const declaredStart = Date.parse(target.processStartTimeUtc);
  const actualStart = Date.parse(actual.processStartTimeUtc);
  // Windows exposes sub-ms StartTime; POSIX ps exposes whole seconds.
  const tolerance = platform === 'win32' ? 2 : 1000;
  return Number.isFinite(declaredStart) && Number.isFinite(actualStart) &&
    Math.abs(declaredStart - actualStart) < tolerance && samePath(target.executablePath, actual.executablePath, platform);
}

module.exports = { inspectProcess, verifyTarget, samePath };
