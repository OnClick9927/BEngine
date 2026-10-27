// Filled by the engine's publish step; the SDK's own IL and AOT bytes stay intact.
const descriptor = /* BENGINE_RUNTIME_BUNDLE */ null;

async function verify(bytes, integrity) {
  if (!/^sha256-[A-Za-z0-9+/]{43}=$/.test(integrity || ''))
    throw new Error('Invalid Web runtime integrity metadata.');
  if (!globalThis.crypto?.subtle) {
    // LAN HTTP deployments do not expose Web Crypto. Native fetch integrity
    // still verifies a local Blob without another network request or JS codec.
    const url = URL.createObjectURL(new Blob([bytes]));
    try { await (await fetch(url, { integrity })).arrayBuffer(); }
    finally { URL.revokeObjectURL(url); }
    return;
  }
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  const actual = `sha256-${btoa(String.fromCharCode(...digest))}`;
  if (actual !== integrity) throw new Error('Web runtime integrity check failed. Refresh to retry.');
}

export async function prepareFrameworkRuntime({ readBytes, onProgress = () => {} } = {}) {
  if (!descriptor) throw new Error('The Web runtime descriptor is missing. Rebuild the player.');
  if (typeof DecompressionStream !== 'function')
    throw new Error('This browser does not support gzip decompression. Please update the browser.');
  const url = new URL(descriptor.url, document.baseURI).href;
  onProgress(0, descriptor.bytes);
  let packed = new Uint8Array(await readBytes(url, async bytes => {
    if (bytes.byteLength !== descriptor.bytes) throw new Error('The Web runtime download is incomplete.');
    await verify(bytes, descriptor.integrity);
  }, onProgress));
  if (packed.length < 12 || new TextDecoder().decode(packed.subarray(0, 8)) !== 'BENGINE3')
    throw new Error('Invalid Web runtime bundle header.');
  const headerLength = new DataView(packed.buffer, packed.byteOffset, packed.byteLength).getUint32(8, true);
  if (headerLength < 1 || headerLength > 1024 * 1024 || 12 + headerLength > packed.length)
    throw new Error('Invalid Web runtime bundle index size.');
  const header = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(packed.subarray(12, 12 + headerLength)));
  if (header.version !== 3 || header.compression !== 'brotli' || !Array.isArray(header.entries) || header.entries.length !== descriptor.files ||
      !Number.isSafeInteger(header.size) || header.size !== descriptor.rawBytes || header.size > 1024 * 1024 * 1024 ||
      header.entries.length > 4096) throw new Error('Invalid Web runtime bundle index.');
  const entries = new Map();
  const modules = new Map();
  const pending = new Map();
  const payloadStart = 12 + headerLength;
  let end = 0;
  let rawSize = 0;
  for (const entry of header.entries) {
    if (!entry || typeof entry.name !== 'string' || !/^[A-Za-z0-9_./+-]+$/.test(entry.name) ||
        entry.name.startsWith('/') || entry.name.split('/').some(part => !part || part === '..') ||
        entries.has(entry.name) || !Number.isSafeInteger(entry.offset) || entry.offset !== end ||
        !Number.isSafeInteger(entry.size) || entry.size < 0 || entry.size > 256 * 1024 * 1024 ||
        !/^sha256-[A-Za-z0-9+/]{43}=$/.test(entry.integrity || ''))
      throw new Error('Invalid Web runtime bundle entry.');
    end += entry.size;
    rawSize += entry.size;
    if (end > header.size || rawSize > 1024 * 1024 * 1024)
      throw new Error('Web runtime bundle entry exceeds its bounds.');
    entries.set(entry.name, entry);
  }
  if (end !== header.size || rawSize !== descriptor.rawBytes)
    throw new Error('Web runtime bundle size does not match its index.');

  async function decompress(compressed, size) {
    // Use the same small decoder on HTTP/HTTPS and browsers without native
    // Brotli streams. It is embedded in the integrity-checked player, so static
    // hosts need neither another root file nor Content-Encoding rules.
    if (typeof descriptor.decoder !== 'string' || descriptor.decoder.length > 2 * 1024 * 1024)
      throw new Error('The Web Brotli decoder metadata is invalid.');
    const decoder = Uint8Array.from(atob(descriptor.decoder), character => character.charCodeAt(0));
    await verify(decoder, descriptor.decoderIntegrity);
    const { instance } = await WebAssembly.instantiate(decoder, {
      env: { emscripten_notify_memory_growth() {} }
    });
    const api = instance.exports;
    if (!(api.memory instanceof WebAssembly.Memory) || typeof api.malloc !== 'function' ||
        typeof api.free !== 'function' || typeof api.bengine_brotli_decode !== 'function')
      throw new Error('The Web Brotli decoder exports are invalid.');
    api._initialize?.();
    let input = 0, output = 0;
    try {
      input = api.malloc(compressed.length);
      output = api.malloc(size);
      if (!input || !output) throw new Error('Unable to allocate the Web runtime decode buffer.');
      new Uint8Array(api.memory.buffer, input, compressed.length).set(compressed);
      if (api.bengine_brotli_decode(input, compressed.length, output, size) !== 1)
        throw new Error('The Web runtime Brotli payload is invalid or has an unexpected size.');
      return new Uint8Array(api.memory.buffer, output, size).slice();
    } finally {
      if (input) api.free(input);
      if (output) api.free(output);
    }
  }
  let payload = await decompress(packed.subarray(payloadStart), header.size);
  packed = null;

  async function decode(entry) {
    if (!payload) throw new Error('The Web runtime bundle was released too early.');
    const bytes = payload.subarray(entry.offset, entry.offset + entry.size);
    await verify(bytes, entry.integrity);
    return bytes;
  }

  // Blob modules otherwise resolve import.meta.url against blob:, which cannot
  // resolve the SDK's relative asset paths. Preserve their original HTTP base.
  for (const entry of entries.values()) {
    if (!entry.name.endsWith('.js')) continue;
    const sourceUrl = new URL(`./${entry.name}`, document.baseURI).href;
    const source = new TextDecoder('utf-8', { fatal: true }).decode(await decode(entry))
      .replace(/\bimport\.meta\.url\b/g, () => JSON.stringify(sourceUrl));
    modules.set(entry.name, URL.createObjectURL(new Blob([source], { type: 'text/javascript' })));
  }
  const entryModule = modules.get('dotnet.js');
  if (!entryModule) throw new Error('The Web runtime entry module is missing.');
  const { dotnet } = await import(entryModule);
  if (!dotnet) throw new Error('The Web runtime entry module is invalid.');
  return {
    dotnet,
    loadBootResource(type, name, defaultUri, integrity) {
      const entry = entries.get(name);
      if (!entry) return undefined; // Game content uses the existing VFS downloader.
      if (integrity && integrity !== entry.integrity)
        throw new Error(`Web runtime version mismatch: ${name}`);
      if (type === 'dotnetjs') {
        const module = modules.get(name);
        if (!module) throw new Error(`Web runtime module is missing: ${name}`);
        return module;
      }
      let request = pending.get(name);
      if (!request) {
        request = decode(entry);
        pending.set(name, request);
      }
      return request.then(bytes => new Response(bytes, {
        headers: { 'Content-Type': name.endsWith('.wasm') ? 'application/wasm' : 'application/octet-stream' }
      }));
    },
    release() {
      // Keep the small module URLs valid for SDK callbacks; the browser releases
      // them with the page. Drop the unpacked runtime after SDK initialization.
      payload = null;
      pending.clear();
      entries.clear();
    }
  };
}
