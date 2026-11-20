import { prepareFrameworkRuntime } from './framework.js';
import { installWebPlatform, decodeHostLaunchEnvironment } from './platform.js';

// Startup downloads stay scoped to the .NET loader; game/media fetches are untouched.
export function createStartupDownloads({ getConfig = () => ({}),
  fetchResource = (url, options) => fetch(url, options),
  delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
  onRetry = () => {} } = {}) {
  const requests = new Map();
  const retryDelays = [0, 250, 1000];

  function assetOptions(name, behavior) {
    const config = getConfig();
    const asset = config.assets?.find(item => item.name === name && (!behavior || item.behavior === behavior)) ||
      Object.values(config.resources || {}).flatMap(items => Array.isArray(items) ? items : [])
        .find(item => item.name === name);
    const options = {};
    if (asset?.cache) options.cache = asset.cache;
    else if (!config.disableNoCacheFetch) options.cache = 'no-cache';
    if (asset?.useCredentials) options.credentials = 'include';
    return options;
  }

  async function request(url, options, key) {
    let state = requests.get(key);
    if (!state) requests.set(key, state = { attempts: 0, terminal: null, lastError: null });
    if (state.terminal) return state.terminal;
    if (state.attempts >= retryDelays.length)
      throw state.lastError || new Error(`Unable to download ${url}: retry budget exhausted.`);
    const attempt = state.attempts++;
    if (attempt) {
      onRetry(url, attempt + 1, retryDelays.length);
      await delay(retryDelays[attempt]);
    }
    const fetchOptions = { ...options };
    if (attempt) fetchOptions.cache = 'reload';
    try {
      const response = await fetchResource(url, fetchOptions);
      if (!response.ok) {
        state.lastError = new Error(`Unable to download ${url} (HTTP ${response.status}).`);
        // The SDK may re-enter for other 4xx statuses. Reuse the failure without
        // issuing another HTTP request; it never consumes unsuccessful bodies.
        if (response.status >= 400 && response.status < 500 &&
            response.status !== 408 && response.status !== 429) state.terminal = response;
      }
      return response;
    } catch (error) {
      state.lastError = new Error(`Unable to download ${url} (attempt ${attempt + 1}/${retryDelays.length}): ${error.message || error}`,
        { cause: error });
      throw state.lastError;
    }
  }

  return {
    loadBootResource(type, name, defaultUri, integrity, behavior) {
      // JavaScript modules are imported by the SDK and require URL strings.
      if (type === 'dotnetjs' || type === 'manifest') return undefined;
      const options = assetOptions(name, behavior);
      if (integrity) options.integrity = integrity;
      // The SDK owns its three-attempt loop, including body-read failures. Each
      // invocation makes at most one fetch, so retries cannot multiply to 3x3.
      return request(defaultUri, options, `${type}:${name}:${defaultUri}:${integrity}`);
    },
    async readBytes(url, validate, onProgress = () => {}) {
      const key = `bytes:${url}`;
      for (let attempt = 0; attempt < retryDelays.length; attempt++) {
        try {
          const response = await request(url, { cache: 'no-cache', credentials: 'same-origin' }, key);
          if (!response.ok) {
            const error = new Error(`Unable to load ${url} (HTTP ${response.status}).`);
            if (response.status >= 400 && response.status < 500 &&
                response.status !== 408 && response.status !== 429) error.permanent = true;
            throw error;
          }
          const total = Number(response.headers.get('Content-Length')) || 0;
          const chunks = [];
          let loaded = 0;
          const reader = response.body.getReader();
          try {
            for (;;) {
              const { done, value } = await reader.read();
              if (done) break;
              chunks.push(value);
              loaded += value.length;
              onProgress(loaded, total);
            }
          } finally { reader.releaseLock(); }
          const bytes = new Uint8Array(loaded);
          let offset = 0;
          for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
          await validate(bytes);
          return bytes;
        } catch (error) {
          if (error.permanent || attempt === retryDelays.length - 1) throw error;
        }
      }
    },
    clear() { requests.clear(); }
  };
}
// End startup download helpers.

const canvas = document.querySelector('#bengine-game');
let activePlayer;
let frameworkRuntime;
const platform = installWebPlatform({
  canvas,
  accessibilityRoot: document.querySelector('#bengine-accessibility'),
  accessibilityLive: document.querySelector('#bengine-accessibility-live'),
  fetchResource: (url, options) => frameworkRuntime?.takeContent(url, options) ?? globalThis.fetch(url, options),
  onAccessibilityAction: (documentId, nodeId, action) => activePlayer?.AccessibilityAction(documentId, nodeId, action)
});

let startupConfig = {};
let dotnet;
const contentManifest = /* BENGINE_CONTENT_MANIFEST */ null;
const startupDownloads = createStartupDownloads({
  getConfig: () => startupConfig,
  onRetry(url, attempt, total) { globalThis.BEngineStartupStatus?.retry?.(url, attempt, total); }
});

async function configurePlayerContent() {
  const manifest = contentManifest;
  const runtime = await prepareFrameworkRuntime({
      readBytes: startupDownloads.readBytes,
      onProgress: (loaded, total) => globalThis.BEngineStartupStatus?.download?.(loaded, total),
      onPreparationProgress: (loaded, total) => globalThis.BEngineStartupStatus?.verified?.(loaded, total)
    });
  globalThis.BEngineStartupStatus?.frameworkReady?.();
  frameworkRuntime = runtime;
  dotnet = runtime.dotnet;
  if (!manifest || typeof manifest.dataDirectory !== 'string' || !Array.isArray(manifest.files) ||
      manifest.files.length === 0) {
    throw new Error('The BEngine content manifest is invalid or empty.');
  }
  const vfs = manifest.files.map(file => {
    if (!file || typeof file.url !== 'string' || typeof file.virtualPath !== 'string' ||
        file.virtualPath.startsWith('/') || file.virtualPath.split('/').includes('..')) {
      throw new Error('The BEngine content manifest contains an invalid file entry.');
    }
    return { name: file.url, virtualPath: file.virtualPath };
  });
  if (manifest.streamingAssetsTransport === 'http') {
    if (vfs.some(file => file.virtualPath.startsWith(`${manifest.dataDirectory}/StreamingAssets/`))) {
      throw new Error('HTTP StreamingAssets must not be mounted into the virtual file system.');
    }
    dotnet.withEnvironmentVariable('BENGINE_STREAMING_ASSETS_URL', new URL('StreamingAssets/', document.baseURI).href);
  }
  canvas.dataset.bengineStreamingAssetsTransport = manifest.streamingAssetsTransport || 'vfs';
  canvas.dataset.bengineMountedStreamingAssets = String(vfs.filter(file =>
    file.virtualPath.startsWith(`${manifest.dataDirectory}/StreamingAssets/`)).length);
  // Bound concurrent startup resource downloads for static servers.
  dotnet.withConfig({ maxParallelDownloads: 4, enableDownloadRetry: true, resources: { vfs } })
    .withOnConfigLoaded(config => { startupConfig = config; })
    .withResourceLoader((...args) => frameworkRuntime.loadBootResource(...args) ?? startupDownloads.loadBootResource(...args));
}

async function initializePlayer() {
  await configurePlayerContent();
  const environment = decodeHostLaunchEnvironment(new URLSearchParams(location.search).get('bengineEnvironment'));
  for (const [name, value] of Object.entries(environment)) dotnet.withEnvironmentVariable(name, value);
  dotnet.withModuleConfig({
    onDownloadResourceProgress(resourcesLoaded, totalResources) {
      globalThis.BEngineStartupStatus?.progress(resourcesLoaded, totalResources);
    }
  });
  const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
  globalThis.BEngineStartupStatus?.runtimeReady?.();
  frameworkRuntime.release();
  startupDownloads.clear();
  const config = getConfig();
  const exports = await getAssemblyExports(config.mainAssemblyName);
  await runMain();
  return exports.BEngine.Player.Hosts.Web.Program;
}

const player = await initializePlayer().catch(error => {
  frameworkRuntime?.release(true);
  startupDownloads.clear();
  globalThis.BEngineStartupStatus?.fail?.(error);
  throw error;
});
activePlayer = player;
globalThis.BEnginePlayer = player;
// Keep pumping the cooperative scheduler while the environment is prepared.
// Game startup and scene Awake/Start remain behind the common presentation gate.
while (!player.PumpStartup()) {
  globalThis.BEngineStartupStatus?.preparing?.(player.GetStartupProgress(), player.GetStartupStage());
  await new Promise(requestAnimationFrame);
}
await globalThis.BEngineStartupStatus?.complete();
player.ContinueStartup();
canvas.dataset.bengineReady = 'true';
const { resize, syncTextInput, reportGamepads } = platform.installInput({
  submitTextEdit: (sessionId, text, anchor, caret) => player.SubmitTextEdit(sessionId, text, anchor, caret),
  setComposition: text => player.SetComposition(text),
  getTextInputRequest: () => player.GetTextInputRequest(),
  reportSafeArea: (left, bottom, right, top) => player.ReportSafeArea(left, bottom, right, top),
  resize: (width, height, scale) => player.Resize(width, height, scale),
  reportGamepads: json => player.ReportGamepads(json),
  keyEvent: (code, pressed, text) => player.KeyEvent(code, pressed, text),
  pointerEvent: (id, type, button, pressed, x, y, deltaX, deltaY, pressure, radius) =>
    player.PointerEvent(id, type, button, pressed, x, y, deltaX, deltaY, pressure, radius),
  pointerEnded: (id, type, button, x, y, canceled) => player.PointerEnded(id, type, button, x, y, canceled),
  wheelEvent: (x, y) => player.WheelEvent(x, y)
});
let lastDiagnosticsTimestamp = -Infinity;
const automaticStartup = new URLSearchParams(window.location.search).get('bengineAutoStart') === '1';
const preferInstalledContent = new URLSearchParams(window.location.search).get('bengineUseInstalledContent') === '1';

window.addEventListener('focus', () => player.SetFocused(true));
window.addEventListener('blur', () => player.SetFocused(false));
document.addEventListener('visibilitychange', () => player.SetPaused(document.hidden));
window.addEventListener('pagehide', () => player.Shutdown(), { once: true });

function update(timestampMilliseconds) {
  resize();
  reportGamepads();
  if (automaticStartup) player.AdvanceStartup(preferInstalledContent);
  if (timestampMilliseconds - lastDiagnosticsTimestamp >= 250) {
    canvas.dataset.bengineDiagnostics = player.GetDiagnostics();
    canvas.dataset.bengineAudioDiagnostics = globalThis.BEngineWebAudio.getDiagnostics();
    canvas.dataset.bengineHttpDiagnostics = globalThis.BEngineWebHttp.getDiagnostics();
    canvas.dataset.bengineContentHashDiagnostics = globalThis.BEngineWebContentHash.getDiagnostics();
    canvas.dataset.bengineImageDiagnostics = globalThis.BEngineWebImages.getDiagnostics();
    lastDiagnosticsTimestamp = timestampMilliseconds;
  }
  const running = player.Tick(timestampMilliseconds);
  if (running) { syncTextInput(); requestAnimationFrame(update); }
}

resize();
requestAnimationFrame(update);
