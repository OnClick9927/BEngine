import { dotnet } from './_framework/dotnet.js';
globalThis.nativeModuleProbeResult = async text => {
  document.getElementById('result').textContent = text;
  await fetch('/result', { method: 'POST', body: text });
};
try {
  const runtime = await dotnet.create();
  await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
} catch (error) {
  await globalThis.nativeModuleProbeResult(JSON.stringify({ Passed: false, Error: String(error), Stack: error.stack }));
}
