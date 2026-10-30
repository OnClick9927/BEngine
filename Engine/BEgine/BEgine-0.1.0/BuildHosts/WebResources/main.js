import { prepareFrameworkRuntime } from './framework.js';
import { installWebVideo } from './video.js';
import { installWebText } from './text.js';

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

// Runtime HTTP: browser I/O owns its buffers; managed callbacks run only in Poll.
export function createWebHttpTransport({ fetchResource = (...args) => globalThis.fetch(...args),
  getAbortController = () => globalThis.AbortController,
  isSupported = () => typeof globalThis.fetch === 'function' && typeof globalThis.ReadableStream === 'function' } = {}) {
  const maximumRequests = 32, normalOwnedBytes = 2 * 1024 * 1024, maximumOwnedBytes = 4 * 1024 * 1024;
  // Preserve every normal request's old receive allowance. Loading rings may
  // borrow only the remaining eight MiB; admission never waits or falls back.
  const maximumExtraRingBytes = 8 * 1024 * 1024, maximumSharedOwnedBytes = 128 * 1024 * 1024;
  let extraRingBytes = 0;
  const maximumReadBytes = 256 * 1024, maximumDefaultReadBytes = 2 * 1024 * 1024, maximumCopyBytes = 1024 * 1024;
  const jobs = new Map(), retiringReaders = new Set();
  const statistics = { Started: 0, Completed: 0, Failed: 0, Canceled: 0, ReceivedBytes: 0,
    ReadCalls: 0, CopiedBytes: 0, BYOB: 0, DefaultReader: 0, DefaultLargeChunks: 0,
    PeakQueuedBytes: 0, PeakRequestQueuedBytes: 0, PeakRequestOwnedBufferBytes: 0, PeakExtraRingBytes: 0 };
  let nextHandle = 1;
  function available() { return isSupported() && typeof getAbortController() === 'function'; }
  function wake(job) { const resume = job.resume; job.resume = null; resume?.(); }
  function cancelReader(reader) {
    try { Promise.resolve(reader?.cancel()).catch(() => {}); } catch { /* The stream may already be closed. */ }
  }
  function reclaimExtraRing(job) {
    if (!job.receiving && !job.queue && job.extraRingBytes) {
      extraRingBytes -= job.extraRingBytes; job.extraRingBytes = 0;
    }
  }
  function discard(job) { job.queue = null; job.queued = job.head = 0; wake(job); reclaimExtraRing(job); }
  function abortIo(job) {
    try { job.controller.abort(); } catch { /* Aborting is idempotent. */ }
    cancelReader(job.reader);
  }
  function fail(job, error) {
    if (job.released || job.error) return;
    job.error = String(error?.message || error || 'The browser HTTP request failed.');
    statistics.Failed++;
    discard(job); abortIo(job);
  }
  function updatePeak(job) {
    let queued = 0;
    for (const item of jobs.values()) queued += item.queued;
    statistics.PeakQueuedBytes = Math.max(statistics.PeakQueuedBytes, queued);
    statistics.PeakRequestQueuedBytes = Math.max(statistics.PeakRequestQueuedBytes, job.queued);
    statistics.PeakRequestOwnedBufferBytes = Math.max(statistics.PeakRequestOwnedBufferBytes,
      (job.queue?.byteLength || 0) + job.reserved);
  }
  async function waitForSpace(job, minimumBytes) {
    while (!job.released && !job.error && job.queueCapacity - job.queued < minimumBytes)
      await new Promise(resolve => { job.resume = resolve; });
    return !job.released && !job.error;
  }
  function append(job, bytes) {
    job.queue ||= new Uint8Array(job.queueCapacity);
    const tail = (job.head + job.queued) % job.queueCapacity;
    const first = Math.min(bytes.length, job.queueCapacity - tail);
    job.queue.set(bytes.subarray(0, first), tail);
    if (first < bytes.length) job.queue.set(bytes.subarray(first), 0);
    job.queued += bytes.length;
    updatePeak(job);
  }
  async function receive(job, request, options) {
    let reader;
    try {
      const response = await fetchResource(request.url, options);
      if (job.released || job.error) { cancelReader(response.body); return; }
      if (response.status === 0 || response.type === 'opaque' || response.type === 'opaqueredirect')
        throw new Error('The browser cannot expose this HTTP response because of its origin policy.');
      const headers = Object.create(null);
      for (const [name, value] of response.headers) headers[name] = [value];
      job.response = JSON.stringify({ statusCode: response.status, reasonPhrase: response.statusText, headers });
      // Headers retain the server's wire Content-Length/Content-Encoding. The
      // progress below counts the decoded bytes actually returned by fetch.
      if (request.method === 'HEAD' || response.status === 204 || response.status === 304 || !response.body) {
        cancelReader(response.body); job.done = true; statistics.Completed++; return;
      }
      let byob = true;
      try { reader = response.body.getReader({ mode: 'byob' }); statistics.BYOB++; }
      catch { byob = false; reader = response.body.getReader(); statistics.DefaultReader++; }
      job.reader = reader;
      job.receiving = true;
      let emptyReads = 0;
      while (await waitForSpace(job, 1)) {
        const capacity = Math.min(job.readCapacity, job.queueCapacity - job.queued);
        // BYOB shares the declared budget with the ring. A default reader has
        // the additional bounded allowance below. No managed view crosses await.
        job.reserved = byob ? capacity : maximumDefaultReadBytes;
        updatePeak(job);
        const result = byob ? await reader.read(new Uint8Array(capacity)) : await reader.read();
        job.reserved = 0;
        if (job.released || job.error) return;
        const bytes = result.value;
        if (bytes?.byteLength) {
          if (!(bytes instanceof Uint8Array)) throw new Error('The HTTP stream returned non-byte data.');
          // A non-BYOB stream controls its allocation size. Allow one bounded
          // input chunk and stop reading while its bytes drain into the ring.
          // The normal two-MiB budget bounds this fallback at 3.75 MiB;
          // loading may borrow a larger ring from the shared eight-MiB pool.
          // BYOB keeps the ring plus its in-flight allocation within ownedBytes.
          const inputLimit = byob ? capacity : maximumDefaultReadBytes;
          if (bytes.byteLength > inputLimit || bytes.buffer.byteLength > inputLimit)
            throw new Error('The browser HTTP stream exceeded its bounded receive buffer.');
          if (bytes.length > Number.MAX_SAFE_INTEGER - job.downloaded)
            throw new Error('The HTTP response exceeds the supported progress counter.');
          job.downloaded += bytes.length; statistics.ReceivedBytes += bytes.length;
          if (!byob && bytes.length > maximumReadBytes) statistics.DefaultLargeChunks++;
          job.reserved = bytes.buffer.byteLength;
          for (let offset = 0; offset < bytes.length;) {
            if (!await waitForSpace(job, 1)) return;
            const count = Math.min(bytes.length - offset, job.queueCapacity - job.queued);
            append(job, bytes.subarray(offset, offset + count)); offset += count;
          }
          job.reserved = 0; emptyReads = 0;
        } else if (!result.done && ++emptyReads > 1024) throw new Error('The HTTP stream returned too many empty chunks.');
        if (result.done) { job.done = true; statistics.Completed++; return; }
      }
    } catch (error) { fail(job, error); }
    finally {
      job.reserved = 0;
      try { reader?.releaseLock(); } catch { /* A canceled reader may still be settling. */ }
      job.reader = null;
      job.receiving = false;
      retiringReaders.delete(job);
      reclaimExtraRing(job);
    }
  }
  return {
    available,
    begin(requestJson, uploadView) {
      if (!available() || jobs.size + retiringReaders.size >= maximumRequests) return 0;
      const request = JSON.parse(requestJson);
      request.method = String(request.method || 'GET').toUpperCase();
      const requestedBytes = request.maximumQueueBytes ?? normalOwnedBytes;
      if (!Number.isInteger(requestedBytes) || requestedBytes < 65536 || requestedBytes > maximumOwnedBytes)
        throw new RangeError('The browser HTTP receive budget must be between 64 KiB and 4 MiB.');
      const headers = new Headers(request.headers || []);
      const options = { method: request.method, headers };
      if (request.bypassCache) options.cache = 'no-store';
      if (request.hasUpload) {
        options.body = uploadView.slice();
        if (request.uploadContentType && !headers.has('Content-Type')) headers.set('Content-Type', request.uploadContentType);
      }
      const controller = new (getAbortController())(); options.signal = controller.signal;
      const handle = nextHandle++;
      const extraBytes = Math.min(Math.max(0, requestedBytes - normalOwnedBytes), maximumExtraRingBytes - extraRingBytes);
      const ownedBytes = Math.min(requestedBytes, normalOwnedBytes) + extraBytes;
      extraRingBytes += extraBytes;
      statistics.PeakExtraRingBytes = Math.max(statistics.PeakExtraRingBytes, extraRingBytes);
      const readCapacity = Math.min(maximumReadBytes, Math.floor(ownedBytes / 2));
      const job = { controller, reader: null, response: null, error: null, done: false, released: false,
        queue: null, head: 0, queued: 0, reserved: 0, queueCapacity: ownedBytes - readCapacity, readCapacity,
        downloaded: 0, resume: null, receiving: false, extraRingBytes: extraBytes };
      jobs.set(handle, job); statistics.Started++;
      // All fetch/body failures are observed here and exposed through read(). A
      // failed POST retains its handle and is never resubmitted via fallback.
      void receive(job, request, options);
      return handle;
    },
    response(handle) { return jobs.get(handle)?.response ?? null; },
    downloaded(handle) { return jobs.get(handle)?.downloaded ?? 0; },
    error(handle) { return jobs.get(handle)?.error ?? (jobs.has(handle) ? null : 'The browser HTTP request has been released.'); },
    read(handle, destination) {
      statistics.ReadCalls++;
      const job = jobs.get(handle);
      if (!job || job.error) return -2;
      if (!job.queued) return job.done ? -1 : 0;
      const count = Math.min(job.queued, destination.byteLength, maximumCopyBytes);
      if (!count) return 0;
      const first = Math.min(count, job.queueCapacity - job.head);
      destination.set(job.queue.subarray(job.head, job.head + first));
      if (first < count) destination.set(job.queue.subarray(0, count - first), first);
      job.head = (job.head + count) % job.queueCapacity; job.queued -= count;
      statistics.CopiedBytes += count;
      if (!job.queued && job.done) { job.queue = null; reclaimExtraRing(job); }
      wake(job);
      return count;
    },
    abort(handle) {
      const job = jobs.get(handle);
      if (!job || job.error || job.done) return;
      job.error = 'The browser HTTP operation was aborted.'; statistics.Canceled++;
      discard(job); abortIo(job);
    },
    release(handle) {
      const job = jobs.get(handle);
      if (!job) return;
      if (!job.done && !job.error) statistics.Canceled++;
      if (job.receiving) retiringReaders.add(job);
      job.released = true; jobs.delete(handle);
      discard(job); abortIo(job);
    },
    getDiagnostics() {
      let queued = 0, reserved = 0, owned = 0;
      for (const job of jobs.values()) {
        queued += job.queued; reserved += job.reserved;
        owned += (job.queue?.byteLength || 0) + job.reserved;
      }
      for (const job of retiringReaders) {
        reserved += job.reserved;
        owned += (job.queue?.byteLength || 0) + job.reserved;
      }
      return JSON.stringify({ ...statistics, ActiveRequests: jobs.size, QueuedBytes: queued, ReservedReadBytes: reserved,
        OwnedBufferBytes: owned, MaximumRequests: maximumRequests, RetiringReaders: retiringReaders.size,
        ExtraRingBytes: extraRingBytes, MaximumExtraRingBytes: maximumExtraRingBytes, MaximumSharedOwnedBytes: maximumSharedOwnedBytes,
        MaximumByobOwnedBytesPerRequest: maximumOwnedBytes,
        MaximumDefaultOwnedBytesPerRequest: maximumOwnedBytes - maximumReadBytes + maximumDefaultReadBytes,
        MaximumReadBytes: maximumReadBytes, MaximumDefaultReadBytes: maximumDefaultReadBytes, MaximumCopyBytes: maximumCopyBytes });
    }
  };
}
globalThis.BEngineWebHttp = createWebHttpTransport({
  fetchResource: (url, options) => frameworkRuntime?.takeContent(url, options) ?? globalThis.fetch(url, options)
});
// End runtime HTTP bridge.

// Whole-bundle verification uses the browser's native SHA implementation. Copy
// transient managed memory before returning; keep admission charged until the
// unabortable native digest settles, including after the owner releases it.
export function createWebAssetBundleHash({ digest = bytes => globalThis.crypto.subtle.digest('SHA-256', bytes),
  supported = () => typeof globalThis.crypto?.subtle?.digest === 'function',
  now = () => globalThis.performance.now() } = {}) {
  const maximumBytes = 64 * 1024 * 1024, maximumJobs = 32;
  const jobs = new Map();
  const statistics = { Started: 0, Completed: 0, Failed: 0, Canceled: 0,
    CopyMilliseconds: 0, CopiedBytes: 0, DigestMilliseconds: 0, PeakReservedBytes: 0 };
  let nextHandle = 1, slots = 0, pending = 0, reservedBytes = 0;
  function available() { try { return !!supported(); } catch { return false; } }
  function freeInput(job) {
    if (job.reserved) { reservedBytes -= job.size; job.reserved = false; }
    job.input = null;
  }
  function finish(job, value, failure) {
    try {
      if (job.released) return;
      if (failure) throw failure;
      if (!(value instanceof ArrayBuffer) || value.byteLength !== 32)
        throw new Error('The browser SHA-256 result must contain exactly 32 bytes.');
      job.result = Array.from(new Uint8Array(value), byte => byte.toString(16).padStart(2, '0')).join('');
      job.state = 1; statistics.Completed++;
    } catch (error) {
      if (!job.released) {
        job.error = String(error?.message || error || 'Browser SHA-256 failed.');
        job.state = -1; statistics.Failed++;
      }
    } finally {
      if (job.pending) {
        pending--; job.pending = false;
        statistics.DigestMilliseconds += Math.max(0, now() - job.started);
      }
      freeInput(job);
      if (job.released) slots--;
    }
  }
  return {
    available,
    begin(view) {
      if (!available()) return 0;
      let size;
      try {
        size = view?.byteLength;
        if (!Number.isSafeInteger(size) || size < 0 || size > maximumBytes || typeof view?.slice !== 'function') return -1;
      } catch { return -1; }
      if (slots >= maximumJobs || reservedBytes + size > maximumBytes) return 0;
      // Allocate the handle before copying: accepted failures cannot trigger a
      // managed fallback that would repeat the same expensive input work.
      const job = { handle: nextHandle++, size, state: 0, input: null, result: null,
        error: null, released: false, reserved: true, pending: false, started: 0 };
      jobs.set(job.handle, job); slots++; reservedBytes += size; statistics.Started++;
      statistics.PeakReservedBytes = Math.max(statistics.PeakReservedBytes, reservedBytes);
      const copyStarted = now();
      try {
        job.input = view.slice();
        if (!(job.input instanceof Uint8Array) || job.input.byteLength !== size)
          throw new Error('The managed SHA-256 input copy has an invalid length or type.');
        statistics.CopiedBytes += size;
      } catch (error) {
        finish(job, null, error);
        return job.handle;
      } finally { statistics.CopyMilliseconds += Math.max(0, now() - copyStarted); }
      job.started = now(); job.pending = true; pending++;
      try {
        Promise.resolve(digest(job.input)).then(value => finish(job, value, null), error =>
          finish(job, null, error || new Error('Browser SHA-256 rejected its input.')));
      } catch (error) { finish(job, null, error); }
      return job.handle;
    },
    status(handle) { return jobs.get(handle)?.state ?? -1; },
    result(handle) { const job = jobs.get(handle); return job?.state === 1 ? job.result : null; },
    error(handle) { return jobs.get(handle)?.error ?? null; },
    release(handle) {
      const job = jobs.get(handle); if (!job) return;
      jobs.delete(handle); job.released = true; job.result = null; job.error = null;
      if (job.state === 0) statistics.Canceled++;
      if (!job.pending) { freeInput(job); slots--; }
    },
    getDiagnostics() { return JSON.stringify({ ...statistics, Active: jobs.size, Pending: pending,
      ReservedBytes: reservedBytes, MaximumReservedBytes: maximumBytes, MaximumInputBytes: maximumBytes,
      MaximumJobs: maximumJobs }); }
  };
}
globalThis.BEngineWebAssetBundleHash = createWebAssetBundleHash();
// End native whole-bundle SHA bridge.

// Encoded PNG preparation stays in the browser. The device's budgeted commit
// uploads the ImageBitmap directly, without a CPU canvas/pixel round trip.
export function createWebImagePreparation({ decodeImage = (blob, options) => globalThis.createImageBitmap(blob, options),
  supported = () => typeof globalThis.createImageBitmap === 'function',
  uploadImage = uploadPreparedImage } = {}) {
  const jobs = new Map(), queued = [];
  const maxJobs = 32, maxConcurrent = 2, maxDecodedBytes = 64 * 1024 * 1024,
    maxEncodedBytes = 32 * 1024 * 1024, maxSingleEncodedBytes = 16 * 1024 * 1024;
  let nextHandle = 1, active = 0, decodedBytes = 0, encodedBytes = 0;
  const counts = { Started: 0, Completed: 0, Failed: 0, Canceled: 0, Uploaded: 0,
    DecodeMilliseconds: 0, UploadMilliseconds: 0, PeakDecodedBytes: 0, PeakEncodedBytes: 0 };
  const close = bitmap => { try { bitmap?.close(); } catch { } };
  function free(job) {
    if (job.accounted) {
      decodedBytes -= job.decodedBytes; encodedBytes -= job.encodedBytes;
      job.accounted = false;
    }
    job.encoded = null;
    close(job.bitmap); job.bitmap = null;
  }
  function pump() {
    while (active < maxConcurrent && queued.length) {
      const job = queued.shift();
      if (job.released) continue;
      active++; job.running = true;
      void (async () => {
        let bitmap;
        const began = performance.now();
        try {
          bitmap = await decodeImage(new Blob([job.encoded], {type:'image/png'}),
            {premultiplyAlpha:'none', colorSpaceConversion:'none', imageOrientation:'none'});
          if (job.released) { close(bitmap); return; }
          if (bitmap.width !== job.width || bitmap.height !== job.height)
            throw new Error('The browser image dimensions differ from the encoded header.');
          job.bitmap = bitmap; bitmap = null; job.state = 1; counts.Completed++;
        } catch (error) {
          close(bitmap);
          if (!job.released) { job.state = -1; counts.Failed++; free(job); }
        } finally {
          job.running = false;
          if (job.released) free(job);
          if (job.accounted) { encodedBytes -= job.encodedBytes; job.encodedBytes = 0; }
          job.encoded = null;
          counts.DecodeMilliseconds += performance.now() - began;
          active--; pump();
        }
      })();
    }
  }
  return {
    available: supported,
    begin(view, width, height) {
      const size = width * height * 4;
      if (!supported() || !Number.isSafeInteger(size) || width <= 0 || height <= 0 ||
          width > 16384 || height > 16384 || size > maxDecodedBytes ||
          !view || view.byteLength < 33 || view.byteLength > maxSingleEncodedBytes) return -1;
      if (jobs.size >= maxJobs || decodedBytes + size > maxDecodedBytes ||
          encodedBytes + view.byteLength > maxEncodedBytes) return 0;
      // A managed MemoryView must be copied synchronously before any await.
      const encoded = view.slice();
      const job = { handle:nextHandle++, width, height, decodedBytes:size, encodedBytes:encoded.byteLength,
        encoded, bitmap:null, state:0, released:false, accounted:true, running:false };
      decodedBytes += size; encodedBytes += encoded.byteLength;
      counts.PeakDecodedBytes = Math.max(counts.PeakDecodedBytes, decodedBytes);
      counts.PeakEncodedBytes = Math.max(counts.PeakEncodedBytes, encodedBytes);
      jobs.set(job.handle,job); queued.push(job); counts.Started++; pump();
      return job.handle;
    },
    status(handle) { return jobs.get(handle)?.state ?? -1; },
    upload(handle, json) {
      const job = jobs.get(handle), description = JSON.parse(json);
      if (!job || job.released || job.state !== 1 || !job.bitmap)
        throw new Error('The prepared image is unavailable.');
      if (description.Width !== job.width || description.Height !== job.height ||
          description.Format !== 1 || description.MipLevels !== 1)
        throw new Error('The texture description does not match its prepared image.');
      const began = performance.now();
      try { const texture = uploadImage(job.bitmap,description); counts.Uploaded++; job.state = 2; return texture; }
      finally { counts.UploadMilliseconds += performance.now()-began; free(job); }
    },
    release(handle) {
      const job = jobs.get(handle); if (!job) return;
      jobs.delete(handle); job.released = true;
      const index = queued.indexOf(job); if (index >= 0) queued.splice(index,1);
      if (job.state === 0) counts.Canceled++;
      // createImageBitmap cannot be aborted. Keep its memory admission charged
      // until the late result is closed, even after its managed owner is gone.
      if (!job.running) free(job);
      pump();
    },
    getDiagnostics() { return JSON.stringify({...counts, ActiveDecodes:active, Handles:jobs.size,
      Queued:queued.length, ReservedDecodedBytes:decodedBytes, EncodedBytes:encodedBytes,
      MaximumDecodedBytes:maxDecodedBytes, MaximumEncodedBytes:maxEncodedBytes}); }
  };
}

function uploadPreparedImage(bitmap, description) {
  const gl = requireGl(), texture = gl.createTexture();
  if (!texture) throw new Error('Unable to allocate a prepared WebGL texture.');
  // ImageBitmap options determine orientation, alpha and color conversion.
  // Keep the same top-down, straight-alpha RGBA8 contract as byte uploads.
  const flip = gl.getParameter(gl.UNPACK_FLIP_Y_WEBGL), alpha = gl.getParameter(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL),
    colors = gl.getParameter(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL);
  try {
    configureTexture(texture,description);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT,1);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL,false);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,gl.NONE);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA8,gl.RGBA,gl.UNSIGNED_BYTE,bitmap);
    return allocateHandle(state.textures,texture);
  } catch (error) { gl.deleteTexture(texture); throw error; }
  finally {
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,flip); gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL,alpha);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,colors);
    gl.bindTexture(gl.TEXTURE_2D,null);
  }
}
globalThis.BEngineWebImages = createWebImagePreparation();
// End encoded image preparation.

// PlayerPrefs is small, synchronously committed user data. AssetBundles never use this store.
export function createWebPlayerPrefsStorage(getStorage = () => globalThis.localStorage) {
  const key = (company, product) =>
    `BEngine.PlayerPrefs.v1:${encodeURIComponent(company)}/${encodeURIComponent(product)}`;
  return {
    read(company, product) { return getStorage().getItem(key(company, product)); },
    write(company, product, contents) { getStorage().setItem(key(company, product), contents); }
  };
}
globalThis.BEnginePlayerPrefs = createWebPlayerPrefsStorage();

installWebVideo();
installWebText();

const canvas = document.querySelector('#bengine-game');
let clipboardCache = '';
globalThis.BEngineText = {
  readClipboard: () => clipboardCache,
  writeClipboard: text => { clipboardCache = text; navigator.clipboard?.writeText(text).catch(() => {}); return true; }
};
if (!(canvas instanceof HTMLCanvasElement)) {
  throw new Error("BEngine Player canvas '#bengine-game' was not found.");
}

const accessibilityRoot = document.querySelector('#bengine-accessibility');
const accessibilityLive = document.querySelector('#bengine-accessibility-live');
if (!(accessibilityRoot instanceof HTMLElement) || !(accessibilityLive instanceof HTMLElement)) {
  throw new Error('BEngine accessibility hosts were not found.');
}

const accessibilityRoles = [
  'group', 'text', 'heading', 'button', 'checkbox', 'radio', 'textbox', 'searchbox', 'slider',
  'combobox', 'img', 'progressbar', 'list', 'listitem', 'group', 'menu', 'menuitem', 'link'
];
const accessibilityDocuments = new Map();

function applyAccessibilityState(element, node) {
  element.setAttribute('role', accessibilityRoles[node.Role] || 'group');
  if (node.Name) element.setAttribute('aria-label', node.Name);
  if (node.Description) element.setAttribute('aria-description', node.Description);
  if (node.Value) element.setAttribute('aria-valuetext', node.Value);
  const disabled = (node.State & 1) !== 0;
  const focused = (node.State & 2) !== 0;
  const checked = (node.State & 4) !== 0;
  const selected = (node.State & 8) !== 0;
  const readOnly = (node.State & 16) !== 0;
  element.setAttribute('aria-disabled', String(disabled));
  if (node.Role === 4 || node.Role === 5) element.setAttribute('aria-checked', String(checked));
  if (selected) element.setAttribute('aria-selected', 'true');
  if (readOnly) element.setAttribute('aria-readonly', 'true');
  if (node.MinimumValue !== null) element.setAttribute('aria-valuemin', String(node.MinimumValue));
  if (node.MaximumValue !== null) element.setAttribute('aria-valuemax', String(node.MaximumValue));
  const numericValue = Number(node.Value);
  if (Number.isFinite(numericValue)) element.setAttribute('aria-valuenow', String(numericValue));
  if (node.LiveRegion) element.setAttribute('aria-live', node.LiveRegion === 2 ? 'assertive' : 'polite');
  const interactive = [3, 4, 5, 6, 7, 8, 9, 16, 17].includes(node.Role);
  element.tabIndex = interactive && !disabled ? 0 : -1;
  element.dataset.bengineFocused = String(focused);
}

function positionAccessibilityNode(element, node) {
  const bounds = canvas.getBoundingClientRect();
  const scaleX = bounds.width / Math.max(1, canvas.width);
  const scaleY = bounds.height / Math.max(1, canvas.height);
  element.style.left = `${bounds.left + node.X * scaleX}px`;
  element.style.top = `${bounds.top + node.Y * scaleY}px`;
  element.style.width = `${Math.max(1, node.Width * scaleX)}px`;
  element.style.height = `${Math.max(1, node.Height * scaleY)}px`;
}

globalThis.BEngineAccessibility = {
  hasSupport() { return typeof document !== 'undefined' && 'ariaLive' in accessibilityLive; },
  update(json) {
    const snapshot = JSON.parse(json);
    const key = String(snapshot.DocumentId);
    const previousFocus = document.activeElement?.dataset?.bengineAccessibilityKey || '';
    accessibilityDocuments.get(key)?.remove();
    const documentRoot = document.createElement('div');
    documentRoot.dataset.bengineDocument = key;
    const elements = new Map();
    for (const node of snapshot.Nodes) {
      const element = document.createElement('div');
      const nodeKey = `${key}:${node.Id}`;
      element.className = 'bengine-a11y-node';
      element.dataset.bengineAccessibilityKey = nodeKey;
      element.textContent = node.Name || node.Value || node.Description || accessibilityRoles[node.Role] || 'item';
      applyAccessibilityState(element, node);
      positionAccessibilityNode(element, node);
      element.addEventListener('focus', () => globalThis.BEnginePlayer?.AccessibilityAction(snapshot.DocumentId, node.Id, 0));
      element.addEventListener('click', () => globalThis.BEnginePlayer?.AccessibilityAction(snapshot.DocumentId, node.Id, 1));
      element.addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') {
          globalThis.BEnginePlayer?.AccessibilityAction(snapshot.DocumentId, node.Id, 1);
          event.preventDefault();
        } else if (event.key === 'ArrowUp' || event.key === 'ArrowRight') {
          globalThis.BEnginePlayer?.AccessibilityAction(snapshot.DocumentId, node.Id, 2);
          event.preventDefault();
        } else if (event.key === 'ArrowDown' || event.key === 'ArrowLeft') {
          globalThis.BEnginePlayer?.AccessibilityAction(snapshot.DocumentId, node.Id, 3);
          event.preventDefault();
        }
      });
      elements.set(node.Id, element);
    }
    for (const node of snapshot.Nodes) {
      const element = elements.get(node.Id);
      const parent = node.ParentId === null ? documentRoot : elements.get(node.ParentId) || documentRoot;
      parent.appendChild(element);
    }
    accessibilityRoot.appendChild(documentRoot);
    accessibilityDocuments.set(key, documentRoot);
    const restore = Array.from(elements.values()).find(element =>
      element.dataset.bengineAccessibilityKey === previousFocus || element.dataset.bengineFocused === 'true');
    if (restore instanceof HTMLElement && restore.tabIndex >= 0 && document.activeElement !== restore)
      restore.focus({ preventScroll: true });
  },
  remove(documentId) {
    const key = String(documentId);
    accessibilityDocuments.get(key)?.remove();
    accessibilityDocuments.delete(key);
  },
  announce(message, liveRegion) {
    accessibilityLive.setAttribute('aria-live', liveRegion === 2 ? 'assertive' : 'polite');
    accessibilityLive.textContent = '';
    requestAnimationFrame(() => { accessibilityLive.textContent = message; });
  }
};

const state = {
  gl: null,
  nextHandle: 1,
  programs: new Map(),
  meshes: new Map(),
  textures: new Map(),
  targets: new Map(),
  currentTarget: 0,
  emptyVao: null,
  contextLost: false,
  frameCount: 0
};

function requireGl() {
  if (!state.gl || state.contextLost) throw new Error('The BEngine WebGL 2 context is unavailable.');
  return state.gl;
}

function allocateHandle(map, value) {
  const handle = state.nextHandle++;
  map.set(handle, value);
  return handle;
}

function requireHandle(map, handle, kind) {
  const value = map.get(handle);
  if (!value) throw new Error(`Unknown WebGL ${kind} handle ${handle}.`);
  return value;
}

function decodeBase64(value) {
  if (!value) return new Uint8Array(0);
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

function encodeBase64(bytes) {
  let result = '';
  const blockSize = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += blockSize) {
    result += String.fromCharCode(...bytes.subarray(offset, Math.min(bytes.length, offset + blockSize)));
  }
  return btoa(result);
}

function compileShader(gl, type, label, source) {
  const shader = gl.createShader(type);
  if (!shader) throw new Error(`Unable to allocate WebGL shader for '${label}'.`);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (gl.getShaderParameter(shader, gl.COMPILE_STATUS)) return shader;
  const log = gl.getShaderInfoLog(shader) || 'No compiler diagnostic was returned.';
  gl.deleteShader(shader);
  throw new Error(`WebGL shader '${label}' failed to compile: ${log}`);
}

function textureFormat(gl, format) {
  switch (format) {
    case 0: return { internal: gl.R8, format: gl.RED, type: gl.UNSIGNED_BYTE, bytes: 1 };
    case 1: return { internal: gl.RGBA8, format: gl.RGBA, type: gl.UNSIGNED_BYTE, bytes: 4 };
    case 2: return { internal: gl.DEPTH_COMPONENT24, format: gl.DEPTH_COMPONENT, type: gl.UNSIGNED_INT, bytes: 4 };
    case 3: return { internal: gl.DEPTH24_STENCIL8, format: gl.DEPTH_STENCIL, type: gl.UNSIGNED_INT_24_8, bytes: 4 };
    default: throw new Error(`Unsupported BEngine WebGL texture format ${format}.`);
  }
}

function uploadTexture(texture, description, pixelsView) {
  const gl = requireGl();
  // Own a JS copy before upload; never retain a transient managed MemoryView.
  const data = pixelsView ? pixelsView.slice() : new Uint8Array(0);
  const format = textureFormat(gl, description.Format);
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  let offset = 0;
  for (let level = 0; level < description.MipLevels; level++) {
    const width = Math.max(1, description.Width >> level);
    const height = Math.max(1, description.Height >> level);
    const length = width * height * format.bytes;
    const pixels = data.length === 0 ? null : data.subarray(offset, offset + length);
    gl.texImage2D(gl.TEXTURE_2D, level, format.internal, width, height, 0,
      format.format, format.type, pixels);
    offset += length;
  }
}

function configureTexture(texture, description) {
  const gl = requireGl();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  const min = description.MipLevels <= 1
    ? (description.MinFilter === 0 ? gl.NEAREST : gl.LINEAR)
    : description.Trilinear ? gl.LINEAR_MIPMAP_LINEAR
      : description.MinFilter === 0 ? gl.NEAREST_MIPMAP_NEAREST : gl.LINEAR_MIPMAP_NEAREST;
  const mag = description.MagFilter === 0 ? gl.NEAREST : gl.LINEAR;
  const wrap = description.AddressMode === 1 ? gl.REPEAT
    : description.AddressMode === 2 ? gl.MIRRORED_REPEAT : gl.CLAMP_TO_EDGE;
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, min);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, mag);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, wrap);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, wrap);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAX_LEVEL, description.MipLevels - 1);
}

function createTextureCore(description, pixelsView) {
  const gl = requireGl();
  const texture = gl.createTexture();
  if (!texture) throw new Error('Unable to allocate a WebGL texture.');
  try {
    configureTexture(texture, description);
    uploadTexture(texture, description, pixelsView);
    return texture;
  } catch (error) {
    gl.deleteTexture(texture);
    throw error;
  } finally {
    gl.bindTexture(gl.TEXTURE_2D, null);
  }
}

function uploadDynamicMesh(gl, mesh, vertices) {
  const byteLength = vertices.byteLength;
  if (byteLength > mesh.capacityBytes) {
    // Retain at most 1.5x the previous peak (or the requested size for a jump).
    // The managed MemoryView is only valid during this synchronous import.
    const capacity = Math.max(byteLength, Math.min(0x7fffffff,
      mesh.capacityBytes + Math.floor(mesh.capacityBytes / 2)));
    const uploadBytes = new Uint8Array(capacity);
    gl.bufferData(gl.ARRAY_BUFFER, capacity, gl.DYNAMIC_DRAW);
    mesh.uploadBytes = uploadBytes;
    mesh.capacityBytes = capacity;
  }
  if (byteLength !== 0) {
    vertices.copyTo(mesh.uploadBytes);
    // WebGL 2's explicit source length excludes unused retained capacity.
    gl.bufferSubData(gl.ARRAY_BUFFER, 0, mesh.uploadBytes, 0, byteLength);
  }
  mesh.byteLength = byteLength;
}


globalThis.BEngineWebInterop = {
  initialize(selector) {
    const selected = document.querySelector(selector);
    if (!(selected instanceof HTMLCanvasElement)) throw new Error(`Canvas '${selector}' was not found.`);
    if (!state.gl) {
      state.gl = selected.getContext('webgl2', {
        alpha: false,
        antialias: true,
        depth: true,
        stencil: true,
        preserveDrawingBuffer: true
      });
      if (!state.gl) throw new Error('This browser does not provide WebGL 2.');
      selected.addEventListener('webglcontextlost', event => {
        event.preventDefault();
        state.contextLost = true;
      });
      selected.addEventListener('webglcontextrestored', () => location.reload());
    }
    const gl = requireGl();
    const debug = gl.getExtension('WEBGL_debug_renderer_info');
    return JSON.stringify({
      Vendor: debug ? gl.getParameter(debug.UNMASKED_VENDOR_WEBGL) : gl.getParameter(gl.VENDOR),
      Renderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER),
      Version: gl.getParameter(gl.VERSION),
      MaxTextureSize: gl.getParameter(gl.MAX_TEXTURE_SIZE)
    });
  },

  beginFrame(width, height) {
    const gl = requireGl();
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    state.currentTarget = 0;
  },

  present() {
    // The rAF boundary submits WebGL work for composition. Keep context-loss
    // validation without forcing an additional command-buffer submission.
    requireGl();
    state.frameCount++;
    canvas.dataset.bengineFrames = String(state.frameCount);
  },

  disposeDevice() {
    if (!state.gl) return;
    for (const program of state.programs.values()) state.gl.deleteProgram(program.program);
    for (const mesh of state.meshes.values()) {
      state.gl.deleteBuffer(mesh.buffer);
      state.gl.deleteVertexArray(mesh.vao);
    }
    for (const texture of state.textures.values()) state.gl.deleteTexture(texture);
    for (const target of state.targets.values()) state.gl.deleteFramebuffer(target.framebuffer);
    if (state.emptyVao) state.gl.deleteVertexArray(state.emptyVao);
    state.programs.clear();
    state.meshes.clear();
    state.textures.clear();
    state.targets.clear();
    state.gl = null;
  },

  createProgram(label, vertexSource, fragmentSource) {
    const gl = requireGl();
    const vertex = compileShader(gl, gl.VERTEX_SHADER, `${label} vertex`, vertexSource);
    const fragment = compileShader(gl, gl.FRAGMENT_SHADER, `${label} fragment`, fragmentSource);
    const program = gl.createProgram();
    if (!program) throw new Error(`Unable to allocate WebGL program '${label}'.`);
    try {
      gl.attachShader(program, vertex);
      gl.attachShader(program, fragment);
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        throw new Error(`WebGL program '${label}' failed to link: ${gl.getProgramInfoLog(program) || ''}`);
      }
      return allocateHandle(state.programs, { program, uniforms: new Map() });
    } catch (error) {
      gl.deleteProgram(program);
      throw error;
    } finally {
      gl.deleteShader(vertex);
      gl.deleteShader(fragment);
    }
  },

  bindProgram(handle) {
    requireGl().useProgram(requireHandle(state.programs, handle, 'program').program);
  },

  deleteProgram(handle) {
    const item = state.programs.get(handle);
    if (!item) return;
    requireGl().deleteProgram(item.program);
    state.programs.delete(handle);
  },

  setUniformMatrix(handle, name, ...values) {
    const gl = requireGl();
    const item = requireHandle(state.programs, handle, 'program');
    gl.uniformMatrix4fv(uniformLocation(gl, item, name), false, new Float32Array(values));
  },

  setUniformVector(handle, name, x, y, z, w) {
    const gl = requireGl();
    const item = requireHandle(state.programs, handle, 'program');
    gl.uniform4f(uniformLocation(gl, item, name), x, y, z, w);
  },

  setUniformFloat(handle, name, value) {
    const gl = requireGl();
    const item = requireHandle(state.programs, handle, 'program');
    gl.uniform1f(uniformLocation(gl, item, name), value);
  },

  setUniformInt(handle, name, value) {
    const gl = requireGl();
    const item = requireHandle(state.programs, handle, 'program');
    gl.uniform1i(uniformLocation(gl, item, name), value);
  },

  createMesh(layoutJson, vertices, dynamic) {
    const gl = requireGl();
    const layout = JSON.parse(layoutJson);
    const vao = gl.createVertexArray();
    const buffer = gl.createBuffer();
    if (!vao || !buffer) {
      if (vao) gl.deleteVertexArray(vao);
      if (buffer) gl.deleteBuffer(buffer);
      throw new Error('Unable to allocate a WebGL vertex buffer.');
    }
    try {
      gl.bindVertexArray(vao);
      gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      const mesh = { vao, buffer, dynamic, capacityBytes: 0, byteLength: 0, uploadBytes: null };
      if (dynamic) uploadDynamicMesh(gl, mesh, vertices);
      else {
        // Static data needs no retained staging array after this one upload.
        gl.bufferData(gl.ARRAY_BUFFER, vertices.slice(), gl.STATIC_DRAW);
        mesh.capacityBytes = mesh.byteLength = vertices.byteLength;
      }
      for (const attribute of layout.Attributes) {
        gl.enableVertexAttribArray(attribute.Location);
        gl.vertexAttribPointer(attribute.Location, attribute.ComponentCount, gl.FLOAT, false,
          layout.StrideBytes, attribute.OffsetBytes);
      }
      return allocateHandle(state.meshes, mesh);
    } catch (error) {
      gl.deleteBuffer(buffer);
      gl.deleteVertexArray(vao);
      throw error;
    } finally {
      gl.bindVertexArray(null);
    }
  },

  updateMesh(handle, vertices) {
    const gl = requireGl();
    const mesh = requireHandle(state.meshes, handle, 'mesh');
    if (!mesh.dynamic) throw new Error('Cannot update a static WebGL mesh.');
    gl.bindBuffer(gl.ARRAY_BUFFER, mesh.buffer);
    uploadDynamicMesh(gl, mesh, vertices);
  },

  deleteMesh(handle) {
    const mesh = state.meshes.get(handle);
    if (!mesh) return;
    const gl = requireGl();
    gl.deleteBuffer(mesh.buffer);
    gl.deleteVertexArray(mesh.vao);
    state.meshes.delete(handle);
  },

  createTexture(label, descriptionJson, pixelsView) {
    const description = JSON.parse(descriptionJson);
    const texture = createTextureCore(description, pixelsView);
    return allocateHandle(state.textures, texture);
  },

  updateTexture(handle, descriptionJson, pixelsView) {
    const gl = requireGl();
    const texture = requireHandle(state.textures, handle, 'texture');
    uploadTexture(texture, JSON.parse(descriptionJson), pixelsView);
    gl.bindTexture(gl.TEXTURE_2D, null);
  },

  deleteTexture(handle) {
    const texture = state.textures.get(handle);
    if (!texture) return;
    requireGl().deleteTexture(texture);
    state.textures.delete(handle);
  },

  createRenderTarget(label, descriptionJson) {
    const gl = requireGl();
    const description = JSON.parse(descriptionJson);
    const previous = state.currentTarget;
    const framebuffer = gl.createFramebuffer();
    if (!framebuffer) throw new Error(`Unable to allocate WebGL render target '${label}'.`);
    let color = null;
    let depth = null;
    try {
      gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
      if (description.ColorFormat !== null) {
        color = createTextureCore({
          Width: description.Width, Height: description.Height, Format: description.ColorFormat,
          Usage: 3, MinFilter: description.Filter, MagFilter: description.Filter,
          AddressMode: 0, MipLevels: 1, Trilinear: false
        }, null);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, color, 0);
      } else {
        gl.drawBuffers([gl.NONE]);
        gl.readBuffer(gl.NONE);
      }
      if (description.DepthFormat !== null) {
        depth = createTextureCore({
          Width: description.Width, Height: description.Height, Format: description.DepthFormat,
          Usage: 3, MinFilter: description.Filter, MagFilter: description.Filter,
          AddressMode: 0, MipLevels: 1, Trilinear: false
        }, null);
        const attachment = description.DepthFormat === 3 ? gl.DEPTH_STENCIL_ATTACHMENT : gl.DEPTH_ATTACHMENT;
        gl.framebufferTexture2D(gl.FRAMEBUFFER, attachment, gl.TEXTURE_2D, depth, 0);
      }
      const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
      if (status !== gl.FRAMEBUFFER_COMPLETE) {
        throw new Error(`WebGL render target '${label}' is incomplete: 0x${status.toString(16)}.`);
      }
      const handle = allocateHandle(state.targets, { framebuffer, color, depth });
      const colorHandle = color ? allocateHandle(state.textures, color) : 0;
      const depthHandle = depth ? allocateHandle(state.textures, depth) : 0;
      return JSON.stringify({ Framebuffer: handle, Color: colorHandle, Depth: depthHandle });
    } catch (error) {
      if (color) gl.deleteTexture(color);
      if (depth) gl.deleteTexture(depth);
      gl.deleteFramebuffer(framebuffer);
      throw error;
    } finally {
      const prior = previous === 0 ? null : requireHandle(state.targets, previous, 'render target').framebuffer;
      gl.bindFramebuffer(gl.FRAMEBUFFER, prior);
      state.currentTarget = previous;
    }
  },

  deleteRenderTarget(handle) {
    const target = state.targets.get(handle);
    if (!target) return;
    const gl = requireGl();
    gl.deleteFramebuffer(target.framebuffer);
    if (target.color) {
      gl.deleteTexture(target.color);
      deleteTextureHandleForObject(target.color);
    }
    if (target.depth) {
      gl.deleteTexture(target.depth);
      deleteTextureHandleForObject(target.depth);
    }
    state.targets.delete(handle);
  },

  bindRenderTarget(handle) {
    const gl = requireGl();
    const previous = state.currentTarget;
    gl.bindFramebuffer(gl.FRAMEBUFFER, requireHandle(state.targets, handle, 'render target').framebuffer);
    state.currentTarget = handle;
    return previous;
  },

  restoreRenderTarget(handle) {
    const gl = requireGl();
    gl.bindFramebuffer(gl.FRAMEBUFFER,
      handle === 0 ? null : requireHandle(state.targets, handle, 'render target').framebuffer);
    state.currentTarget = handle;
  },

  setViewport(x, y, width, height) { requireGl().viewport(x, y, width, height); },
  disableScissor() { requireGl().disable(requireGl().SCISSOR_TEST); },
  setScissor(x, y, width, height) {
    const gl = requireGl();
    gl.enable(gl.SCISSOR_TEST);
    gl.scissor(x, y, width, height);
  },
  clear(flags, r, g, b, a) {
    const gl = requireGl();
    let mask = 0;
    if (flags & 1) { gl.clearColor(r, g, b, a); mask |= gl.COLOR_BUFFER_BIT; }
    if (flags & 2) mask |= gl.DEPTH_BUFFER_BIT;
    if (flags & 4) mask |= gl.STENCIL_BUFFER_BIT;
    if (mask) gl.clear(mask);
  },
  setDepthState(test, write) {
    const gl = requireGl();
    test ? gl.enable(gl.DEPTH_TEST) : gl.disable(gl.DEPTH_TEST);
    gl.depthMask(write);
  },
  setBlendMode(mode) {
    const gl = requireGl();
    if (mode === 0) gl.disable(gl.BLEND);
    else if (mode === 1) { gl.enable(gl.BLEND); gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA); }
    else throw new Error(`Unknown BEngine blend mode ${mode}.`);
  },
  setRasterizerState(depthBias, slope, constantBias, cullMode) {
    const gl = requireGl();
    if (cullMode === 0) gl.disable(gl.CULL_FACE);
    else {
      gl.enable(gl.CULL_FACE);
      gl.cullFace(cullMode === 1 ? gl.BACK : gl.FRONT);
      gl.frontFace(gl.CCW);
    }
    if (depthBias) { gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(slope, constantBias); }
    else gl.disable(gl.POLYGON_OFFSET_FILL);
  },
  bindTexture(slot, handle) {
    const gl = requireGl();
    gl.activeTexture(gl.TEXTURE0 + slot);
    gl.bindTexture(gl.TEXTURE_2D, requireHandle(state.textures, handle, 'texture'));
  },
  drawMesh(handle, topology, firstVertex, vertexCount) {
    const gl = requireGl();
    gl.bindVertexArray(requireHandle(state.meshes, handle, 'mesh').vao);
    gl.drawArrays(topology === 0 ? gl.TRIANGLES : gl.LINES, firstVertex, vertexCount);
  },
  drawWithoutMesh(topology, firstVertex, vertexCount) {
    const gl = requireGl();
    state.emptyVao ||= gl.createVertexArray();
    gl.bindVertexArray(state.emptyVao);
    gl.drawArrays(topology === 0 ? gl.TRIANGLES : gl.LINES, firstVertex, vertexCount);
  },
  readPixels(x, y, width, height) {
    const gl = requireGl();
    const bytes = new Uint8Array(width * height * 4);
    gl.pixelStorei(gl.PACK_ALIGNMENT, 1);
    gl.readPixels(x, y, width, height, gl.RGBA, gl.UNSIGNED_BYTE, bytes);
    return encodeBase64(bytes);
  }
};

function uniformLocation(gl, item, name) {
  if (!item.uniforms.has(name)) item.uniforms.set(name, gl.getUniformLocation(item.program, name));
  return item.uniforms.get(name);
}

function deleteTextureHandleForObject(texture) {
  for (const [handle, value] of state.textures) {
    if (value === texture) state.textures.delete(handle);
  }
}

const audioState = { nextHandle: 1, outputs: new Map(), context: null };
const audioDecodeState = {
  nextHandle: 1, jobs: new Map(), pending: [], active: 0,
  completed: 0, failed: 0, canceled: 0, milliseconds: 0,
  copyCalls: 0, copiedBytes: 0, copyMilliseconds: 0, longestCopyMilliseconds: 0, largestCopyBytes: 0
};
function pumpAudioDecodes() {
  // decodeAudioData itself cannot be canceled. Keep canceled in-flight jobs in
  // this limit until their promise settles, including after scene unload.
  while (audioDecodeState.active < 2 && audioDecodeState.pending.length) {
    const job = audioDecodeState.pending.shift();
    if (job.canceled) continue;
    audioDecodeState.active++;
    const started = performance.now();
    void (async () => {
      try {
        const OfflineContext = window.OfflineAudioContext || window.webkitOfflineAudioContext;
        // An offline context requires no user gesture and preserves the source
        // rate. The playback device's rate must not change imported clip data.
        const context = new OfflineContext(1, 1, job.frequency);
        const bytes = job.encoded;
        job.encoded = null;
        const buffer = await context.decodeAudioData(bytes.buffer);
        if (job.canceled) return;
        if (buffer.sampleRate !== job.frequency || buffer.length !== job.frames ||
            buffer.numberOfChannels < 1 || buffer.numberOfChannels > 8 ||
            (!job.mono && buffer.numberOfChannels !== job.channels))
          throw new Error('The browser decoder changed the imported audio format.');
        job.buffer = buffer;
        job.sourceChannels = Array.from({ length: buffer.numberOfChannels }, (_, channel) => buffer.getChannelData(channel));
        job.status = 1;
        audioDecodeState.completed++;
      } catch {
        if (!job.canceled) { job.status = -1; audioDecodeState.failed++; }
      } finally {
        job.encoded = null;
        audioDecodeState.milliseconds += Math.max(0, performance.now() - started);
        audioDecodeState.active--;
        pumpAudioDecodes();
      }
    })();
  }
}
function beginAudioDecode(encodedView, frames, channels, frequency, mono, gain) {
  if (!(window.OfflineAudioContext || window.webkitOfflineAudioContext) ||
      audioDecodeState.jobs.size >= 32 || !Number.isInteger(frames) || frames <= 0 ||
      !Number.isInteger(channels) || channels < 1 || channels > 8 || frames * channels > 64 * 1024 * 1024 ||
      !Number.isInteger(frequency) || frequency < 8000 || frequency > 96000 ||
      (mono && channels !== 1) || !Number.isFinite(gain) || gain < 0) return 0;
  const length = encodedView.byteLength;
  if (length < 32768 || length > 64 * 1024 * 1024) return 0;
  // MemoryView expires on return from the synchronous import. Only this owned
  // copy can be consumed asynchronously by the browser's native decoder.
  const encoded = encodedView.slice();
  const handle = audioDecodeState.nextHandle++;
  const job = { handle, frames, channels, frequency, mono, gain, encoded, status: 0, canceled: false,
    buffer: null, sourceChannels: null, scratch: null };
  audioDecodeState.jobs.set(handle, job);
  audioDecodeState.pending.push(job);
  pumpAudioDecodes();
  return handle;
}
function copyDecodedAudio(handle, firstFrame, destinationView) {
  const job = audioDecodeState.jobs.get(handle);
  if (!job || job.status !== 1) return 0;
  const byteLength = destinationView.byteLength;
  const frames = byteLength / (4 * job.channels);
  if (byteLength > 262144 || !Number.isInteger(frames) || !Number.isInteger(firstFrame) ||
      firstFrame < 0 || firstFrame + frames > job.frames)
    throw new RangeError('Invalid decoded audio PCM block.');
  const started = performance.now();
  const samples = frames * job.channels;
  if (!job.scratch || job.scratch.length < samples) job.scratch = new Float32Array(samples);
  const pcm = job.scratch;
  const sources = job.sourceChannels;
  if (job.mono) {
    for (let frame = 0; frame < frames; frame++) {
      let sum = 0;
      for (let channel = 0; channel < sources.length; channel++) {
        const value = sources[channel][firstFrame + frame];
        sum = Math.fround(sum + (Number.isFinite(value) ? Math.max(-1, Math.min(1, value)) : 0));
      }
      const value = Math.fround(Math.fround(sum / sources.length) * job.gain);
      pcm[frame] = Number.isFinite(value) ? Math.max(-1, Math.min(1, value)) : 0;
    }
  } else {
    for (let channel = 0; channel < job.channels; channel++) {
      const source = sources[channel];
      for (let frame = 0; frame < frames; frame++) {
        const sample = source[firstFrame + frame];
        const value = Math.fround((Number.isFinite(sample) ? Math.max(-1, Math.min(1, sample)) : 0) * job.gain);
        pcm[frame * job.channels + channel] = Number.isFinite(value) ? Math.max(-1, Math.min(1, value)) : 0;
      }
    }
  }
  destinationView.set(new Uint8Array(pcm.buffer, 0, byteLength));
  const milliseconds = Math.max(0, performance.now() - started);
  audioDecodeState.copyCalls++;
  audioDecodeState.copiedBytes += byteLength;
  audioDecodeState.copyMilliseconds += milliseconds;
  audioDecodeState.longestCopyMilliseconds = Math.max(audioDecodeState.longestCopyMilliseconds, milliseconds);
  audioDecodeState.largestCopyBytes = Math.max(audioDecodeState.largestCopyBytes, byteLength);
  return samples;
}
function releaseAudioDecode(handle) {
  const job = audioDecodeState.jobs.get(handle);
  if (!job) return;
  if (job.status === 0) audioDecodeState.canceled++;
  job.canceled = true;
  job.encoded = job.buffer = job.sourceChannels = job.scratch = null;
  audioDecodeState.jobs.delete(handle);
  const queued = audioDecodeState.pending.indexOf(job);
  if (queued >= 0) audioDecodeState.pending.splice(queued, 1);
}
function audioContext() {
  audioState.context ||= new (window.AudioContext || window.webkitAudioContext)();
  return audioState.context;
}
function updateAudioCompletion(output) {
  const now = output.capacityFrames ? bufferedAudioTime(output) : audioContext().currentTime;
  while (output.pending.length && output.pending[0].end <= now) {
    output.completedFrames = output.pending[0].frames;
    output.pending.shift();
  }
  return now;
}
function bufferedAudioTime(output) {
  const context = output.context;
  const now = context.currentTime;
  if (context.state !== 'running') output.gapArmed = false;
  // A reset/invalid device clock must not manufacture consumption or capacity.
  // A new output owns its context; it never inherits another context's timeline.
  if (!Number.isFinite(now) || now < 0 || now < output.lastClock) return NaN;
  output.lastClock = now;
  return now;
}
function bufferedAudioAvailableFrames(output, now) {
  if (output.context.state !== 'running' || !Number.isFinite(now)) return 0;
  // Capacity includes the scheduling lead, rather than adding 10 ms to every
  // configured buffer. Use the actual device tail, not whole-buffer counters.
  const start = Math.max(now + 0.01, output.nextTime);
  return Math.max(0, Math.floor((now + output.capacityFrames / output.sampleRate - start) * output.sampleRate + 1e-7));
}
function bufferedAudioQueuedFrames(output, now) {
  if (output.context.state === 'closed' || !Number.isFinite(now)) return 0;
  return Math.max(0, Math.ceil((output.nextTime - now) * output.sampleRate - 1e-7));
}
function addAudioFrames(value, frames) {
  // The JS counters remain exact and JSON/Int64-readable even at their numeric
  // limit. Queue capacity never depends on subtracting these lifetime totals.
  return Math.min(Number.MAX_SAFE_INTEGER, value + frames);
}
function copyAudioSamples(output, samplesView, byteLength, sourceByteLength = byteLength) {
  // Retain at most 64 KiB per output. A partial acceptance copies only its
  // prefix; MemoryView.copyTo cannot truncate a larger source into a target.
  const maximumScratchBytes = 64 * 1024;
  if (byteLength > maximumScratchBytes || byteLength !== sourceByteLength) {
    const bytes = byteLength === sourceByteLength ? samplesView.slice() : samplesView.slice(0, byteLength);
    return new Float32Array(bytes.buffer, bytes.byteOffset, byteLength / 4);
  }
  if (!output.scratchBytes || byteLength > output.scratchBytes.byteLength) {
    const capacity = Math.min(maximumScratchBytes,
      Math.max(byteLength, (output.scratchBytes?.byteLength || 1024) * 2));
    output.scratchBytes = new Uint8Array(capacity);
    output.scratchSamples = new Float32Array(output.scratchBytes.buffer);
  }
  // Synchronous ownership transfer: never retain a managed MemoryView.
  samplesView.copyTo(output.scratchBytes);
  return output.scratchSamples;
}
function scheduleAudioSamples(output, context, samples, frames) {
  const buffer = context.createBuffer(output.channelCount, frames, output.sampleRate);
  for (let channel = 0; channel < output.channelCount; channel++) {
    const destination = buffer.getChannelData(channel);
    for (let frame = 0; frame < frames; frame++) {
      destination[frame] = samples[frame * output.channelCount + channel];
    }
  }
  const source = context.createBufferSource();
  source.buffer = buffer;
  source.connect(context.destination);
  const start = Math.max(context.currentTime + 0.01, output.nextTime);
  source.start(start);
  if (output.capacityFrames) {
    if (output.gapArmed && start - 0.01 > output.nextTime + 1 / output.sampleRate)
      output.queueGapEvents = addAudioFrames(output.queueGapEvents, 1);
    output.gapArmed = true;
    output.submittedFrames = addAudioFrames(output.submittedFrames, frames);
  } else output.submittedFrames += frames;
  output.nextTime = start + buffer.duration;
  const pending = { end: output.nextTime, frames: output.submittedFrames };
  if (output.capacityFrames) pending.source = source;
  output.pending.push(pending);
}
globalThis.BEngineWebAudio = {
  beginDecode: beginAudioDecode,
  decodeStatus(handle) { return audioDecodeState.jobs.get(handle)?.status ?? -1; },
  copyDecodedPcm: copyDecodedAudio,
  releaseDecode: releaseAudioDecode,
  create(sampleRate, channelCount, bufferMilliseconds = 0) {
    if (bufferMilliseconds !== 0 && (!Number.isInteger(bufferMilliseconds) || bufferMilliseconds < 20 || bufferMilliseconds > 2000))
      throw new RangeError('Web Audio buffer duration must be between 20 and 2000 ms.');
    // Closing an AudioContext is terminal. Replacement outputs start a fresh
    // timeline while existing handles retain their original closed context.
    if (audioState.context?.state === 'closed') audioState.context = null;
    const context = audioContext();
    const handle = audioState.nextHandle++;
    audioState.outputs.set(handle, {
      sampleRate, channelCount, nextTime: context.currentTime, context,
      capacityFrames: bufferMilliseconds ? Math.floor(sampleRate * bufferMilliseconds / 1000) : 0,
      lastClock: 0, gapArmed: false, queueGapEvents: 0,
      submittedFrames: 0, completedFrames: 0, droppedFrames: 0, pending: [],
      scratchBytes: null, scratchSamples: null
    });
    return handle;
  },
  submit(handle, samplesBase64) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    const context = audioContext();
    updateAudioCompletion(output);
    const bytes = decodeBase64(samplesBase64);
    const samples = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
    const frames = samples.length / output.channelCount;
    if (output.capacityFrames) {
      const accepted = Math.min(frames, bufferedAudioAvailableFrames(output, bufferedAudioTime(output)));
      output.droppedFrames = addAudioFrames(output.droppedFrames, frames - accepted);
      if (accepted > 0) scheduleAudioSamples(output, output.context, samples, accepted);
      return;
    }
    if (output.nextTime - context.currentTime > 0.75) {
      output.droppedFrames += frames;
      return;
    }
    scheduleAudioSamples(output, context, samples, frames);
  },
  submitPcm(handle, samplesView) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    if (output.capacityFrames) { globalThis.BEngineWebAudio.trySubmitPcm(handle, samplesView); return; }
    const context = audioContext();
    updateAudioCompletion(output);
    const byteLength = samplesView.byteLength;
    const frames = byteLength / (4 * output.channelCount);
    if (output.nextTime - context.currentTime > 0.75) {
      output.droppedFrames += frames;
      return;
    }
    const samples = copyAudioSamples(output, samplesView, byteLength);
    scheduleAudioSamples(output, context, samples, frames);
  },
  trySubmitPcm(handle, samplesView) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    if (!output.capacityFrames) {
      const previous = output.submittedFrames;
      globalThis.BEngineWebAudio.submitPcm(handle, samplesView);
      return output.submittedFrames - previous;
    }
    const now = updateAudioCompletion(output);
    const byteLength = samplesView.byteLength;
    const frames = byteLength / (4 * output.channelCount);
    if (!Number.isInteger(frames)) throw new RangeError('Web Audio PCM must contain complete frames.');
    const accepted = Math.min(frames, bufferedAudioAvailableFrames(output, now));
    output.droppedFrames = addAudioFrames(output.droppedFrames, frames - accepted);
    if (accepted > 0) {
      const samples = copyAudioSamples(output, samplesView, accepted * output.channelCount * 4, byteLength);
      scheduleAudioSamples(output, output.context, samples, accepted);
    }
    return accepted;
  },
  getAvailableFrames(handle) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    const now = updateAudioCompletion(output);
    return output.capacityFrames ? bufferedAudioAvailableFrames(output, now) : 0;
  },
  getQueuedFrames(handle) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    const now = updateAudioCompletion(output);
    return output.capacityFrames ? bufferedAudioQueuedFrames(output, now) : 0;
  },
  getStatistics(handle) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    const now = updateAudioCompletion(output);
    return JSON.stringify({
      SubmittedFrames: output.submittedFrames,
      CompletedFrames: output.completedFrames,
      DroppedFrames: output.droppedFrames,
      PendingBuffers: output.pending.length,
      ...(output.capacityFrames ? {
        QueuedFrames: bufferedAudioQueuedFrames(output, now),
        CapacityFrames: output.capacityFrames,
        BufferedMilliseconds: output.context.state !== 'closed' && Number.isFinite(now) ? Math.max(0, output.nextTime - now) * 1000 : 0,
        ContextState: output.context.state,
        // Observed schedule gaps may include intentional silence/pause. They
        // cannot be labelled acoustic underruns without playback intent.
        QueueGapEvents: output.queueGapEvents, UnderrunEvents: null
      } : {})
    });
  },
  getDiagnostics() {
    const outputs = Array.from(audioState.outputs.values());
    for (const output of outputs) updateAudioCompletion(output);
    const buffered = outputs.filter(output => output.capacityFrames);
    return JSON.stringify({
      ContextCreated: audioState.context !== null,
      ContextState: audioState.context?.state || 'uninitialized',
      ContextSampleRate: audioState.context?.sampleRate || 0,
      OutputCount: outputs.length,
      NativeDecodeActive: audioDecodeState.active,
      NativeDecodePending: audioDecodeState.pending.filter(job => !job.canceled).length,
      NativeDecodeCompleted: audioDecodeState.completed,
      NativeDecodeFailed: audioDecodeState.failed,
      NativeDecodeCanceled: audioDecodeState.canceled,
      NativeDecodeMilliseconds: Math.round(audioDecodeState.milliseconds),
      NativeCopyCalls: audioDecodeState.copyCalls,
      NativeCopiedBytes: audioDecodeState.copiedBytes,
      NativeCopyMilliseconds: Math.round(audioDecodeState.copyMilliseconds),
      NativeCopyMaximumMilliseconds: Math.round(audioDecodeState.longestCopyMilliseconds * 1000) / 1000,
      NativeCopyLargestBytes: audioDecodeState.largestCopyBytes,
      SubmittedFrames: outputs.reduce((sum, output) => addAudioFrames(sum, output.submittedFrames), 0),
      CompletedFrames: outputs.reduce((sum, output) => addAudioFrames(sum, output.completedFrames), 0),
      DroppedFrames: outputs.reduce((sum, output) => addAudioFrames(sum, output.droppedFrames), 0),
      PendingBuffers: outputs.reduce((sum, output) => sum + output.pending.length, 0),
      ...(buffered.length ? {
        BufferedMilliseconds: Math.max(...buffered.map(output => output.context.state !== 'closed' ? Math.max(0, output.nextTime - output.lastClock) * 1000 : 0)),
        CapacityMilliseconds: Math.max(...buffered.map(output => output.capacityFrames / output.sampleRate * 1000)),
        QueueGapEvents: buffered.reduce((sum, output) => addAudioFrames(sum, output.queueGapEvents), 0),
        UnderrunEvents: null
      } : {})
    });
  },
  dispose(handle) {
    const output = audioState.outputs.get(handle);
    if (output?.capacityFrames) {
      for (const pending of output.pending) {
        try { pending.source.stop(); } catch { /* Already ended or stopped. */ }
        pending.source.disconnect();
      }
      output.pending.length = 0;
    }
    audioState.outputs.delete(handle);
  }
};

function resumeAudio() {
  const context = audioContext();
  if (context.state === 'suspended') void context.resume();
}
window.addEventListener('keydown', resumeAudio, { passive: true });
window.addEventListener('pointerdown', resumeAudio, { passive: true });

let startupConfig = {};
let dotnet;
let frameworkRuntime;
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
  const testOptions = new URLSearchParams(location.search);
  if (testOptions.get('bengineRunTests') === '1') {
    dotnet.withEnvironmentVariable('BENGINE_TEST_RUNNER_MODE', 'player')
      .withEnvironmentVariable('BENGINE_TEST_RUNNER_RESULT', '/bengine-tests.json')
      .withEnvironmentVariable('BENGINE_TEST_RUNNER_COVERAGE', testOptions.get('bengineTestCoverage') || '0')
      .withEnvironmentVariable('BENGINE_ASSET_BUNDLE_REMOTE_URL', testOptions.get('bengineTestContentUrl') || '')
      .withEnvironmentVariable('BENGINE_ASSET_BUNDLE_ALLOW_INSECURE_LOOPBACK', '1');
    if (testOptions.has('bengineTestIds')) dotnet.withEnvironmentVariable('BENGINE_TEST_RUNNER_IDS', testOptions.get('bengineTestIds'));
  }
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
  return { player: exports.BEngine.Player.Hosts.Web.Program, testOptions };
}

const { player, testOptions } = await initializePlayer().catch(error => {
  frameworkRuntime?.release(true);
  startupDownloads.clear();
  globalThis.BEngineStartupStatus?.fail?.(error);
  throw error;
});
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
const keyState = new Map();
const textInput = document.createElement('textarea');
textInput.setAttribute('aria-hidden', 'true'); textInput.tabIndex = -1;
Object.assign(textInput.style, { position: 'fixed', left: '1px', top: '1px', width: '1px', height: '1px',
  opacity: '0.01', padding: '0', border: '0', fontSize: '16px', pointerEvents: 'none' });
document.body.appendChild(textInput);
let textSession = null; let composingText = false; let pendingNativeEdit = false;
function submitNativeText() {
  if (!textSession || composingText) return;
  pendingNativeEdit = true;
  const backwards = textInput.selectionDirection === 'backward';
  player.SubmitTextEdit(textSession.Id, textInput.value,
    backwards ? textInput.selectionEnd : textInput.selectionStart,
    backwards ? textInput.selectionStart : textInput.selectionEnd);
}
textInput.addEventListener('input', submitNativeText);
textInput.addEventListener('select', submitNativeText);
textInput.addEventListener('paste', event => { clipboardCache = event.clipboardData?.getData('text/plain') || ''; });
textInput.addEventListener('compositionstart', () => { composingText = true; });
textInput.addEventListener('compositionupdate', event => player.SetComposition(event.data || ''));
textInput.addEventListener('compositionend', () => { composingText = false; player.SetComposition(''); submitNativeText(); });
function syncTextInput() {
  const json = player.GetTextInputRequest(); const next = json ? JSON.parse(json) : null;
  if (!next) { if (textSession) textInput.blur(); textSession = null; return; }
  const changed = textSession?.Id !== next.Id; textSession = next;
  if (!composingText && !pendingNativeEdit) {
    if (textInput.value !== next.Text) textInput.value = next.Text;
    const start = Math.min(next.Anchor, next.Caret); const end = Math.max(next.Anchor, next.Caret);
    if (textInput.selectionStart !== start || textInput.selectionEnd !== end)
      textInput.setSelectionRange(start, end, next.Anchor > next.Caret ? 'backward' : 'forward');
  }
  textInput.style.webkitTextSecurity = next.Password ? 'disc' : 'none';
  textInput.setAttribute('autocomplete', next.Password ? 'new-password' : 'off');
  if (changed || document.activeElement === canvas) textInput.focus({ preventScroll: true });
  pendingNativeEdit = false;
}
const safeAreaProbe = document.createElement('div');
safeAreaProbe.style.cssText = 'position:fixed;visibility:hidden;pointer-events:none;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)';
document.body.appendChild(safeAreaProbe);
let safeAreaDirty = true;
let safeAreaLeft, safeAreaBottom, safeAreaRight, safeAreaTop;
let safeAreaWidth, safeAreaHeight;
function invalidateSafeArea() { safeAreaDirty = true; }
function reportSafeArea(clientWidth) {
  const style = getComputedStyle(safeAreaProbe);
  const ratio = canvas.width / Math.max(1, clientWidth);
  const left = parseFloat(style.paddingLeft) * ratio || 0;
  const bottom = parseFloat(style.paddingBottom) * ratio || 0;
  const right = parseFloat(style.paddingRight) * ratio || 0;
  const top = parseFloat(style.paddingTop) * ratio || 0;
  // The managed display clamps insets to its current dimensions. Reapply raw
  // insets after a size change, including when a previously tiny canvas expands.
  if (left !== safeAreaLeft || bottom !== safeAreaBottom || right !== safeAreaRight || top !== safeAreaTop ||
      safeAreaWidth !== canvas.width || safeAreaHeight !== canvas.height) {
    player.ReportSafeArea(left, bottom, right, top);
    safeAreaLeft = left; safeAreaBottom = bottom; safeAreaRight = right; safeAreaTop = top;
    safeAreaWidth = canvas.width; safeAreaHeight = canvas.height;
  }
  safeAreaDirty = false;
}
let lastPointerX = 0;
let lastPointerY = 0;
let lastDiagnosticsTimestamp = -Infinity;
const automaticStartup = new URLSearchParams(window.location.search).get('bengineAutoStart') === '1';
const preferInstalledContent = new URLSearchParams(window.location.search).get('bengineUseInstalledContent') === '1';

let reportedWidth, reportedHeight, reportedScale, previousClientWidth, previousClientHeight;
function resize() {
  const clientWidth = canvas.clientWidth;
  const clientHeight = canvas.clientHeight;
  const scale = Math.max(1, window.devicePixelRatio || 1);
  const width = Math.max(1, Math.round(clientWidth * scale));
  const height = Math.max(1, Math.round(clientHeight * scale));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  if (reportedWidth !== width || reportedHeight !== height || reportedScale !== scale) {
    player.Resize(width, height, scale);
    reportedWidth = width; reportedHeight = height; reportedScale = scale;
    safeAreaDirty = true;
  }
  if (previousClientWidth !== clientWidth || previousClientHeight !== clientHeight) {
    previousClientWidth = clientWidth; previousClientHeight = clientHeight;
    safeAreaDirty = true;
  }
  if (safeAreaDirty) reportSafeArea(clientWidth);
}

// Poll geometry/DPR each game frame, but cross the managed boundary only on change.
// Browser chrome and display cutouts can change insets without changing the canvas.
window.addEventListener('resize', invalidateSafeArea);
window.addEventListener('orientationchange', invalidateSafeArea);
document.addEventListener('fullscreenchange', invalidateSafeArea);
document.addEventListener('visibilitychange', invalidateSafeArea);
window.visualViewport?.addEventListener('resize', invalidateSafeArea);
window.visualViewport?.addEventListener('scroll', invalidateSafeArea);
if (typeof ResizeObserver === 'function') {
  const safeAreaObserver = new ResizeObserver(invalidateSafeArea);
  safeAreaObserver.observe(safeAreaProbe, { box: 'border-box' });
}

const gamepadSnapshots = [];
let gamepadSnapshotInitialized = false;
let gamepadSnapshotJson = '[]';
function reportGamepads() {
  const gamepads = navigator.getGamepads?.();
  let count = 0, changed = !gamepadSnapshotInitialized;
  // Always sample connected devices once per game frame, including held controls.
  // Retain the snapshot arrays so idle devices do not allocate/serialize every frame.
  for (let slot = 0; slot < (gamepads?.length || 0); slot++) {
    const gamepad = gamepads[slot];
    if (!gamepad) continue;
    let snapshot = gamepadSnapshots[count++];
    if (!snapshot) {
      snapshot = { index: gamepad.index, id: gamepad.id, axes: [], buttons: [] };
      gamepadSnapshots.push(snapshot);
      changed = true;
    }
    if (snapshot.index !== gamepad.index || snapshot.id !== gamepad.id) {
      snapshot.index = gamepad.index; snapshot.id = gamepad.id; changed = true;
    }
    if (snapshot.axes.length !== gamepad.axes.length) { snapshot.axes.length = gamepad.axes.length; changed = true; }
    for (let axis = 0; axis < gamepad.axes.length; axis++) {
      if (snapshot.axes[axis] !== gamepad.axes[axis]) { snapshot.axes[axis] = gamepad.axes[axis]; changed = true; }
    }
    if (snapshot.buttons.length !== gamepad.buttons.length) { snapshot.buttons.length = gamepad.buttons.length; changed = true; }
    for (let button = 0; button < gamepad.buttons.length; button++) {
      const value = gamepad.buttons[button].pressed ? 1 : gamepad.buttons[button].value;
      if (snapshot.buttons[button] !== value) { snapshot.buttons[button] = value; changed = true; }
    }
  }
  if (gamepadSnapshots.length !== count) { gamepadSnapshots.length = count; changed = true; }
  if (changed) gamepadSnapshotJson = JSON.stringify(gamepadSnapshots);
  // Connected controllers must be reapplied after Input.ResetInputAxes(), even
  // when the physical snapshot is unchanged. The idle, disconnected path is silent.
  if (changed || count !== 0) {
    player.ReportGamepads(gamepadSnapshotJson);
    gamepadSnapshotInitialized = true;
  }
}

function pointerPosition(event) {
  const bounds = canvas.getBoundingClientRect();
  const x = (event.clientX - bounds.left) * canvas.width / Math.max(1, bounds.width);
  // Engine Input uses a bottom-left origin; DOM pointer events use top-left.
  const y = canvas.height - (event.clientY - bounds.top) * canvas.height / Math.max(1, bounds.height);
  return [x, y];
}

window.addEventListener('keydown', event => {
  if (event.target === textInput) {
    if (event.key === 'Escape' || event.key === 'Tab' || event.key === 'Enter' && !textSession?.Multiline) {
      player.KeyEvent(event.code, true, ''); event.preventDefault();
    }
    return;
  }
  if (event.isComposing) return;
  if (!keyState.get(event.code)) player.KeyEvent(event.code, true, event.key.length === 1 ? event.key : '');
  keyState.set(event.code, true);
  if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space'].includes(event.code)) event.preventDefault();
});
window.addEventListener('keyup', event => {
  if (event.target === textInput && !['Escape', 'Tab', 'Enter'].includes(event.key)) return;
  keyState.set(event.code, false);
  player.KeyEvent(event.code, false, '');
});
canvas.addEventListener('pointerdown', event => {
  canvas.focus();
  canvas.setPointerCapture(event.pointerId);
  const [x, y] = pointerPosition(event);
  lastPointerX = x; lastPointerY = y;
  player.PointerEvent(event.pointerId, event.pointerType, event.button, true, x, y, 0, 0,
    event.pressure || 1, event.width || 1);
  event.preventDefault();
});
canvas.addEventListener('pointermove', event => {
  const [x, y] = pointerPosition(event);
  const dx = x - lastPointerX; const dy = y - lastPointerY;
  lastPointerX = x; lastPointerY = y;
  player.PointerEvent(event.pointerId, event.pointerType, event.button, (event.buttons & 1) !== 0,
    x, y, dx, dy, event.pressure || 0, event.width || 1);
});
function endPointer(event, canceled) {
  const [x, y] = pointerPosition(event);
  player.PointerEnded(event.pointerId, event.pointerType, event.button, x, y, canceled);
}
canvas.addEventListener('pointerup', event => endPointer(event, false));
canvas.addEventListener('pointercancel', event => endPointer(event, true));
canvas.addEventListener('wheel', event => {
  player.WheelEvent(event.deltaX, event.deltaY);
  event.preventDefault();
}, { passive: false });
window.addEventListener('focus', () => player.SetFocused(true));
window.addEventListener('blur', () => player.SetFocused(false));
document.addEventListener('visibilitychange', () => player.SetPaused(document.hidden));
window.addEventListener('resize', resize);
window.addEventListener('pagehide', () => player.Shutdown(), { once: true });

let testReportSent = false;
function update(timestampMilliseconds) {
  resize();
  reportGamepads();
  if (automaticStartup) player.AdvanceStartup(preferInstalledContent);
  if (timestampMilliseconds - lastDiagnosticsTimestamp >= 250) {
    canvas.dataset.bengineDiagnostics = player.GetDiagnostics();
    canvas.dataset.bengineAudioDiagnostics = globalThis.BEngineWebAudio.getDiagnostics();
    canvas.dataset.bengineHttpDiagnostics = globalThis.BEngineWebHttp.getDiagnostics();
    canvas.dataset.bengineAssetBundleHashDiagnostics = globalThis.BEngineWebAssetBundleHash.getDiagnostics();
    canvas.dataset.bengineImageDiagnostics = globalThis.BEngineWebImages.getDiagnostics();
    lastDiagnosticsTimestamp = timestampMilliseconds;
  }
  const running = player.Tick(timestampMilliseconds);
  if (!testReportSent && testOptions.get('bengineRunTests') === '1') {
    const report = player.GetTestResult();
    if (report) { testReportSent = true; fetch('/__bengine_tests/' + testOptions.get('bengineTestToken'), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: report }).catch(error => console.error('Test report delivery failed', error)); }
  }
  if (running) { syncTextInput(); requestAnimationFrame(update); }
}

resize();
requestAnimationFrame(update);
