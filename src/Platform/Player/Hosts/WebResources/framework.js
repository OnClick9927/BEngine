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

export async function prepareFrameworkRuntime({ readBytes, onProgress = () => {},
  onPreparationProgress = () => {} } = {}) {
  if (!descriptor) throw new Error('The Web runtime descriptor is missing. Rebuild the player.');
  if (typeof DecompressionStream !== 'function')
    throw new Error('This browser does not support gzip decompression. Please update the browser.');
  const url = new URL(descriptor.url, document.baseURI).href;
  if (!Number.isSafeInteger(descriptor.archiveBytes || 0) || (descriptor.archiveBytes || 0) < 0 ||
      (descriptor.archiveBytes || 0) > 32 * 1024 * 1024)
    throw new Error('The shared runtime download size is invalid.');
  const downloadBytes = descriptor.bytes + (descriptor.archiveBytes || 0);
  let packedLoaded = 0;
  onProgress(0, downloadBytes);
  let packed = new Uint8Array(await readBytes(url, async bytes => {
    if (bytes.byteLength !== descriptor.bytes) throw new Error('The Web runtime download is incomplete.');
    await verify(bytes, descriptor.integrity);
  }, loaded => {
    packedLoaded = Math.max(packedLoaded, Math.min(loaded, descriptor.bytes));
    onProgress(packedLoaded, downloadBytes);
  }));
  if (packed.length < 12 || new TextDecoder().decode(packed.subarray(0, 8)) !== 'BENGINE3')
    throw new Error('Invalid Web runtime bundle header.');
  const headerLength = new DataView(packed.buffer, packed.byteOffset, packed.byteLength).getUint32(8, true);
  if (headerLength < 1 || headerLength > 1024 * 1024 || 12 + headerLength > packed.length)
    throw new Error('Invalid Web runtime bundle index size.');
  const header = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(packed.subarray(12, 12 + headerLength)));
  if (![3, 4].includes(header.version) || header.compression !== 'brotli' || !Array.isArray(header.entries) || header.entries.length !== descriptor.files ||
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

  const archives = header.archives || [], externalEntries = header.externalEntries || [];
  if (!Array.isArray(archives) || archives.length > 4096 || !Array.isArray(externalEntries) ||
      externalEntries.length > 4096 || externalEntries.length !== (descriptor.externalFiles || 0) ||
      (header.version === 3 && (archives.length || externalEntries.length)))
    throw new Error('Invalid Web runtime shared content index.');
  const external = new Map(), archiveUrls = new Set();
  let archiveBytes = 0;
  const contentRoot = new URL('StreamingAssets/', document.baseURI);
  for (const archive of archives) {
    if (!archive || typeof archive.path !== 'string' || !archive.path.startsWith('StreamingAssets/') ||
        /[\\?#\u0000-\u001f]/.test(archive.path) || archive.path.split('/').some(part => !part || part === '.' || part === '..') ||
        !Number.isSafeInteger(archive.size) || archive.size < 1 || archive.size > 16 * 1024 * 1024 ||
        !/^sha256-[A-Za-z0-9+/]{43}=$/.test(archive.integrity || ''))
      throw new Error('Invalid Web runtime shared content archive.');
    archive.url = new URL(archive.path, document.baseURI).href;
    if (!archive.url.startsWith(contentRoot.href) || archiveUrls.has(archive.url))
      throw new Error('Invalid Web runtime shared content path.');
    archiveUrls.add(archive.url);
    archiveBytes += archive.size;
  }
  if (archiveBytes > 32 * 1024 * 1024 || archiveBytes !== (descriptor.archiveBytes || 0))
    throw new Error('Invalid Web runtime shared content budget.');
  for (const entry of externalEntries) {
    if (!entry || typeof entry.name !== 'string' || !/^[A-Za-z0-9_.+-]+\.wasm$/.test(entry.name) ||
        entries.has(entry.name) || external.has(entry.name) ||
        !Number.isSafeInteger(entry.size) || entry.size < 0 || entry.size > 256 * 1024 * 1024 ||
        !/^sha256-[A-Za-z0-9+/]{43}=$/.test(entry.integrity || '') ||
        typeof entry.delta !== 'string' || !entries.has(entry.delta) ||
        !Number.isSafeInteger(entry.archive) || entry.archive < 0 || entry.archive >= archives.length ||
        !Number.isSafeInteger(entry.offset) || entry.offset < 0 ||
        !Number.isSafeInteger(entry.compressedSize) || entry.compressedSize < 0 ||
        entry.compressedSize > archives[entry.archive].size - entry.offset ||
        !Number.isSafeInteger(entry.sourceSize) || entry.sourceSize < 0 || entry.sourceSize > 16 * 1024 * 1024 ||
        !/^sha256-[A-Za-z0-9+/]{43}=$/.test(entry.sourceIntegrity || '') || ![0, 8].includes(entry.compression))
      throw new Error('Invalid Web runtime shared assembly.');
    external.set(entry.name, entry);
  }

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

  // These buffers already belong to packaged game content. Keep at most one
  // startup handoff to its first runtime GET, then release it; no disk cache.
  const archiveRequests = new Map(), contentHandoff = new Map();
  const archiveProgress = new Map();
  let handoffTimer, discardHandoff = false;
  async function readArchive(index) {
    let request = archiveRequests.get(index);
    if (!request) {
      const archive = archives[index];
      request = readBytes(archive.url, async bytes => {
        if (bytes.byteLength !== archive.size) throw new Error('The shared runtime archive is incomplete.');
        await verify(bytes, archive.integrity);
      }, loaded => {
        archiveProgress.set(index, Math.max(archiveProgress.get(index) || 0, Math.min(loaded, archive.size)));
        onProgress(descriptor.bytes + [...archiveProgress.values()].reduce((sum, value) => sum + value, 0),
          descriptor.bytes + archiveBytes);
      }).then(bytes => {
        if (!discardHandoff) {
          contentHandoff.set(archive.url, bytes);
          clearTimeout(handoffTimer);
          handoffTimer = setTimeout(() => contentHandoff.clear(), 60000);
        }
        return bytes;
      });
      archiveRequests.set(index, request);
    }
    return request;
  }

  const verifiedEntries = new Set();
  const verifyTotal = descriptor.rawBytes + externalEntries.reduce((sum, entry) => sum + entry.size, 0);
  let verifiedBytes = 0;
  async function decode(entry) {
    const bytes = await decodeEntry(entry);
    if (!verifiedEntries.has(entry.name)) {
      verifiedEntries.add(entry.name);
      verifiedBytes += entry.size;
      onPreparationProgress(verifiedBytes, verifyTotal);
    }
    return bytes;
  }
  async function decodeEntry(entry) {
    if (external.has(entry.name)) {
      const archive = await readArchive(entry.archive);
      const compressed = archive.subarray(entry.offset, entry.offset + entry.compressedSize);
      const source = entry.compression === 0 ? compressed : new Uint8Array(await new Response(
        new Blob([compressed]).stream().pipeThrough(new DecompressionStream('deflate-raw'))).arrayBuffer());
      if (source.length !== entry.sourceSize) throw new Error('The shared runtime assembly size is invalid.');
      await verify(source, entry.sourceIntegrity);
      const delta = await decode(entries.get(entry.delta));
      const bytes = await applyRuntimeDelta(source, delta, entry.size);
      if (bytes.length !== entry.size) throw new Error('The reconstructed runtime assembly size is invalid.');
      await verify(bytes, entry.integrity);
      return bytes;
    }
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
      const entry = entries.get(name) || external.get(name);
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
    takeContent(url, options = {}) {
      const key = new URL(url, document.baseURI).href;
      const headers = new Headers(options.headers);
      if ((options.method || 'GET').toUpperCase() !== 'GET' || options.body || options.signal?.aborted ||
          ['Range', 'If-Range', 'If-Match', 'If-None-Match', 'If-Modified-Since',
            'If-Unmodified-Since', 'Authorization'].some(name => headers.has(name))) return undefined;
      const bytes = contentHandoff.get(key);
      if (!bytes) return undefined;
      contentHandoff.delete(key);
      // Bound both the view and its backing buffer for BYOB and default readers.
      // Blob streams can expose one large chunk on some browser/runtime versions.
      let content = bytes, offset = 0;
      const stream = new ReadableStream({ type: 'bytes', pull(controller) {
        const end = Math.min(offset + 64 * 1024, content.length);
        controller.enqueue(content.slice(offset, end));
        offset = end;
        if (offset === content.length) { content = null; controller.close(); }
      }, cancel() { content = null; } });
      return Promise.resolve(new Response(stream, { headers: {
        'Content-Type': 'application/octet-stream', 'Content-Length': String(bytes.length)
      } }));
    },
    release(discardContent = false) {
      // Keep the small module URLs valid for SDK callbacks; the browser releases
      // them with the page. Drop the unpacked runtime after SDK initialization.
      payload = null;
      pending.clear();
      entries.clear();
      external.clear();
      archiveRequests.clear();
      archiveProgress.clear();
      if (discardContent) {
        discardHandoff = true;
        contentHandoff.clear();
        clearTimeout(handoffTimer);
      }
    }
  };
}

async function applyRuntimeDelta(source, delta, maximumOutputLength = 256 * 1024 * 1024) {
  if (!(source instanceof Uint8Array) || !(delta instanceof Uint8Array))
    throw new TypeError('Web runtime delta inputs must be Uint8Array views.');
  if (!Number.isSafeInteger(maximumOutputLength) || maximumOutputLength < 0)
    throw new Error('Invalid Web runtime delta output limit.');
  let cursor = 0;
  for (const character of [66, 68, 76, 84, 49])
    if (cursor >= delta.length || delta[cursor++] !== character)
      throw new Error('Invalid Web runtime delta header.');
  function readVarUInt() {
    let value = 0;
    for (let index = 0; index < 5; ++index) {
      if (cursor >= delta.length) throw new Error('Truncated Web runtime delta integer.');
      const item = delta[cursor++];
      if (index === 4 && (item & 240) !== 0)
        throw new Error('Overflowed Web runtime delta integer.');
      value += (item & 127) * 2 ** (index * 7);
      if ((item & 128) === 0) {
        if (index > 0 && item === 0) throw new Error('Non-canonical Web runtime delta integer.');
        return value;
      }
    }
    throw new Error('Overflowed Web runtime delta integer.');
  }
  const sourceLength = readVarUInt(), outputLength = readVarUInt();
  if (sourceLength !== source.length) throw new Error('Web runtime delta source length mismatch.');
  if (outputLength > maximumOutputLength) throw new Error('Web runtime delta output exceeds its limit.');
  if (delta.length - cursor < 64) throw new Error('Truncated Web runtime delta hashes.');
  const sourceIntegrity = 'sha256-' + btoa(String.fromCharCode(...delta.subarray(cursor, cursor + 32)));
  const outputIntegrity = 'sha256-' + btoa(String.fromCharCode(...delta.subarray(cursor + 32, cursor + 64)));
  cursor += 64;
  await verify(source, sourceIntegrity);
  const output = new Uint8Array(outputLength);
  let written = 0;
  while (written < outputLength) {
    const tag = readVarUInt(), length = Math.floor(tag / 4), kind = tag % 4;
    if (length === 0 || length > outputLength - written)
      throw new Error('Invalid Web runtime delta output range.');
    if (kind === 0) {
      if (length > delta.length - cursor) throw new Error('Truncated Web runtime delta literal.');
      output.set(delta.subarray(cursor, cursor + length), written);
      cursor += length;
    } else if (kind === 1) {
      const offset = readVarUInt();
      if (offset > source.length || length > source.length - offset)
        throw new Error('Invalid Web runtime delta source range.');
      output.set(source.subarray(offset, offset + length), written);
    } else if (kind !== 2) {
      throw new Error('Unknown Web runtime delta command.');
    }
    written += length;
  }
  if (cursor !== delta.length) throw new Error('Trailing Web runtime delta data.');
  await verify(output, outputIntegrity);
  return output;
}
