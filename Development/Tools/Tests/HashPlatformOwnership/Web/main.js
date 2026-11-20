import { dotnet } from './_framework/dotnet.js';
import { installWebPlatform, createWebContentHash } from './platform.js';
let real, active, pending = [];
globalThis.hashProbeMode = mode => {
  pending = [];
  active = mode === 'real' ? real : createWebContentHash({
    supported: () => mode !== 'unavailable',
    digest: () => mode === 'failure' ? Promise.reject(new Error('injected digest failure')) :
      mode === 'short' ? Promise.resolve(new ArrayBuffer(7)) : new Promise(resolve => pending.push(resolve))
  });
};
globalThis.hashProbeSettle = () => pending.splice(0).forEach(resolve => resolve(new ArrayBuffer(32)));
globalThis.hashProbeDiagnostics = () => active.getDiagnostics();
globalThis.hashProbeResult = async text => {
  document.getElementById('result').textContent = text;
  await fetch('/result', { method: 'POST', body: text });
};
try {
  installWebPlatform({canvas: document.getElementById('canvas'), accessibilityRoot: document.getElementById('accessibility'), accessibilityLive: document.getElementById('live')});
  real = globalThis.BEngineWebContentHash;
  active = real;
  // Imports are bound once. A stable test router selects real host instances.
  globalThis.BEngineWebContentHash = Object.fromEntries(Object.keys(real).map(name => [name, (...args) => active[name](...args)]));
  const runtime = await dotnet.create();
  await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
} catch (error) { await globalThis.hashProbeResult(JSON.stringify({Passed:false,Error:String(error),Stack:error.stack})); }
