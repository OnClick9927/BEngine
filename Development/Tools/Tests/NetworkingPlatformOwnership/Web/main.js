import { dotnet } from './_framework/dotnet.js';
import { installWebPlatform } from './platform.js';
globalThis.networkProbeBase = () => location.origin + '/';
globalThis.networkProbeDiagnostics = () => globalThis.BEngineWebHttp.getDiagnostics();
globalThis.networkProbeResult = async text => {
  document.getElementById('result').textContent = text;
  globalThis.networkProbeCompleted = JSON.parse(text);
  await fetch('/result', { method: 'POST', body: text });
};
try {
  installWebPlatform({ canvas: document.getElementById('canvas'), accessibilityRoot: document.getElementById('accessibility'), accessibilityLive: document.getElementById('live') });
  const runtime = await dotnet.create();
  await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
} catch (error) {
  await globalThis.networkProbeResult(JSON.stringify({ Passed: false, Error: String(error), Stack: error.stack }));
}
