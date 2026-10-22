// Browser-owned asynchronous containers. Video pixels and PCM are delivered to the engine;
// the media element is muted and never bypasses the engine's audio mixer.
export function installWebVideo() {
  const videos = new Map(), audioReaders = new Map();
  let nextHandle = 1;
  const limit = 64 * 1024 * 1024;
  function encode(bytes) {
    let result = '';
    for (let i = 0; i < bytes.length; i += 16384) result += String.fromCharCode(...bytes.subarray(i, i + 16384));
    return btoa(result);
  }
  function waitFor(video, event, signal, action) {
    return new Promise((resolve, reject) => {
      const finish = error => {
        clearTimeout(timer); video.removeEventListener(event, ready); video.removeEventListener('error', failed);
        signal.removeEventListener('abort', aborted); error ? reject(error) : resolve();
      };
      const ready = () => finish();
      const failed = () => finish(new Error(video.error?.message || 'Browser container decode failed.'));
      const aborted = () => finish(new DOMException('Video closed', 'AbortError'));
      const timer = setTimeout(() => finish(new Error('Browser video decode timed out.')), 20000);
      video.addEventListener(event, ready, { once: true }); video.addEventListener('error', failed, { once: true });
      signal.addEventListener('abort', aborted, { once: true });
      try { if (signal.aborted) aborted(); else action(); } catch (error) { finish(error); }
    });
  }
  function metadata(item) {
    return JSON.stringify({ handle: item.handle, width: item.asset.width, height: item.asset.height,
      duration: item.asset.duration, frameRate: item.asset.frameRate, hasAudio: !!item.asset.audio });
  }
  function capture(item) {
    item.context.drawImage(item.video, 0, 0, item.asset.width, item.asset.height);
    item.rgba = encode(item.context.getImageData(0, 0, item.asset.width, item.asset.height).data);
    item.timestamp = item.video.currentTime;
  }
  async function seek(item, target) {
    if (Math.abs(item.video.currentTime - target) > 1e-7)
      await waitFor(item.video, 'seeked', item.abort.signal, () => { item.video.currentTime = target; });
    if (!item.disposed) capture(item);
  }
  function create(asset, initial) {
    const video = document.createElement('video'); video.muted = true; video.preload = 'auto'; video.playsInline = true;
    const canvas = document.createElement('canvas'); canvas.width = asset.width || 1; canvas.height = asset.height || 1;
    const item = { handle: nextHandle++, asset, video, canvas, context: canvas.getContext('2d', { willReadFrequently: true }),
      abort: new AbortController(), disposed: false, pending: false, target: 0, timestamp: initial?.timestamp || 0, rgba: initial?.rgba || '', error: '' };
    asset.references++; videos.set(item.handle, item);
    item.ready = waitFor(video, 'loadeddata', item.abort.signal, () => { video.src = asset.url; video.load(); });
    // A clone starts with a valid copied frame while its own independent media cursor loads.
    item.ready.catch(error => { if (!item.disposed) item.error = error.message; });
    return item;
  }
  function close(handle) {
    const item = videos.get(handle); if (!item) return;
    item.disposed = true; item.abort.abort(); item.video.pause(); item.video.removeAttribute('src'); item.video.load();
    videos.delete(handle); release(item.asset);
  }
  function release(asset) {
    if (--asset.references === 0) { asset.source.close(); if (asset.ownsUrl) URL.revokeObjectURL(asset.url); }
  }
  async function pump(item) {
    item.pending = true;
    try {
      await item.ready;
      while (!item.disposed) {
        const target = item.target;
        await seek(item, target);
        if (target === item.target) break;
      }
    } catch (error) { if (!item.disposed) item.error = error.message; }
    finally { item.pending = false; }
  }
  globalThis.BEngineWebVideo = {
    async open(source, bytesBase64 = '') {
      let bytes = null;
      if (bytesBase64) {
        if (bytesBase64.length > limit * 4 / 3 + 4) throw new Error('Browser video exceeds 64 MiB.');
        bytes = Uint8Array.from(atob(bytesBase64), c => c.charCodeAt(0));
      }
      if (bytes && (!bytes.length || bytes.length > limit)) throw new Error('Browser video size is invalid.');
      const byteSource = new MediaByteSource(source, bytes, limit);
      let container;
      try { container = await readMp4Metadata(byteSource); }
      catch (error) { byteSource.close(); throw error; }
      const buffered = byteSource.bytes;
      const asset = { source: byteSource, url: buffered ? URL.createObjectURL(new Blob([buffered])) : source, ownsUrl: !!buffered,
        references: 0, width: 0, height: 0, duration: 0, frameRate: container.frameRate, audio: container.audio, warm: null };
      const item = create(asset);
      try {
        await item.ready;
        asset.width = item.video.videoWidth; asset.height = item.video.videoHeight; asset.duration = item.video.duration;
        if (!asset.width || !asset.height || asset.width * asset.height > 16777216 || !Number.isFinite(asset.duration) || asset.duration <= 0)
          throw new Error('Browser video dimensions/duration exceed decoder limits.');
        item.canvas.width = asset.width; item.canvas.height = asset.height; capture(item);
        if (asset.audio) {
          asset.audio.samples = Math.min(2147483647, Math.round(asset.duration * asset.audio.frequency));
          if (globalThis.AudioDecoder && asset.audio.config && (await AudioDecoder.isConfigSupported(asset.audio.config)).supported)
            asset.warm = await decodeAudioWindow(asset, 0, Math.min(asset.audio.samples, asset.audio.frequency * 2));
        }
        return metadata(item);
      } catch (error) { close(item.handle); throw error; }
    },
    clone(handle) {
      const source = videos.get(handle); if (!source || source.disposed) throw new Error('Video cursor is closed.');
      const item = create(source.asset, source); return metadata(item);
    },
    read(handle, seconds) {
      const item = videos.get(handle); if (!item || item.disposed) throw new Error('Video cursor is closed.');
      if (item.error) throw new Error(item.error);
      if (!Number.isFinite(seconds) || seconds < 0 || seconds > item.asset.duration) throw new Error('Invalid video seek.');
      item.target = Math.min(seconds, Math.max(0, item.asset.duration - .000001));
      if (!item.pending && Math.abs(item.timestamp - item.target) > 1e-7) void pump(item);
      return JSON.stringify({ timestamp: item.timestamp, rgba: item.rgba, pending: item.pending });
    },
    close,
    openAudio(handle) {
      const asset = videos.get(handle)?.asset;
      if (!asset?.audio) throw new Error('This container has no audio track.');
      if (!asset.warm) throw new Error('This audio codec requires an available WebCodecs AudioDecoder.');
      const id = nextHandle++; asset.references++;
      audioReaders.set(id, { asset, cache: asset.warm, pending: false, target: 0, disposed: false, error: '' });
      return JSON.stringify({ handle: id, channels: asset.audio.channels, frequency: asset.audio.frequency, samples: asset.audio.samples });
    },
    readAudio(handle, frame, frames) {
      const item = audioReaders.get(handle); if (!item) throw new Error('Audio cursor is closed.');
      if (item.error) throw new Error(item.error);
      const audio = item.asset.audio;
      if (!Number.isInteger(frame) || !Number.isInteger(frames) || frame < 0 || frames < 0 || frame + frames > audio.samples || frames > audio.frequency * 4)
        throw new Error('Invalid browser PCM range.');
      if (frame >= item.cache.start && frame + frames <= item.cache.end) {
        const start = (frame - item.cache.start) * audio.channels;
        return encode(new Uint8Array(item.cache.data.buffer, start * 4, frames * audio.channels * 4));
      }
      item.target = frame;
      if (!item.pending) {
        item.pending = true;
        void (async () => {
          try {
            do {
              const target = item.target;
              const cache = await decodeAudioWindow(item.asset, target, Math.min(audio.samples, target + audio.frequency * 4));
              if (item.disposed) break;
              item.cache = cache;
              if (target === item.target) break;
            } while (!item.disposed);
          } catch (error) { if (!item.disposed) item.error = error.message; }
          finally { item.pending = false; }
        })();
      }
      return '';
    },
    closeAudio(handle) { const item = audioReaders.get(handle); if (!item) return; item.disposed = true; audioReaders.delete(handle); release(item.asset); },
    diagnostics() { return { videos: videos.size, audioReaders: audioReaders.size,
      pcmCacheBytes: [...audioReaders.values()].reduce((sum, item) => sum + item.cache.data.byteLength, 0) }; }
  };
  return globalThis.BEngineWebVideo;
}

// MP4 metadata and AAC packets are fetched by range; neither the compressed media nor
// the decoded soundtrack must fit in memory. All work runs on the browser event loop.
class MediaByteSource {
  constructor(url, bytes, fallbackLimit) { this.url = url; this.bytes = bytes; this.length = bytes?.length || 0; this.limit = fallbackLimit; this.abort = new AbortController(); }
  close() { this.abort.abort(); this.bytes = null; }
  async read(offset, length) {
    if (!Number.isSafeInteger(offset) || offset < 0 || length < 0 || length > 16 * 1024 * 1024) throw new Error('Invalid media byte range.');
    if (this.abort.signal.aborted) throw new DOMException('Media closed', 'AbortError');
    if (this.bytes) return this.bytes.subarray(offset, Math.min(this.bytes.length, offset + length));
    const response = await fetch(this.url, { headers: { Range: `bytes=${offset}-${offset + length - 1}` }, signal: this.abort.signal });
    if (!response.ok) throw new Error(`Media request failed: HTTP ${response.status}.`);
    if (response.status === 206) {
      const match = /^bytes (\d+)-(\d+)\/(\d+)$/.exec(response.headers.get('Content-Range') || '');
      if (!match || +match[1] !== offset || +match[2] - offset + 1 > length) { await response.body?.cancel(); throw new Error('Invalid media Content-Range.'); }
      this.length = +match[3]; const bytes = new Uint8Array(await response.arrayBuffer());
      if (bytes.length !== +match[2] - offset + 1) throw new Error('Truncated media range.');
      return bytes;
    }
    // A small server without Range support remains compatible, with a strict cap.
    if (+response.headers.get('Content-Length') > this.limit) { await response.body?.cancel(); throw new Error('Large media requires HTTP byte-range support.'); }
    const reader = response.body.getReader(), chunks = []; let size = 0;
    try {
      while (true) { const part = await reader.read(); if (part.done) break; size += part.value.length;
        if (size > this.limit) { await reader.cancel(); throw new Error('Large media requires HTTP byte-range support.'); } chunks.push(part.value); }
    } finally { reader.releaseLock(); }
    this.bytes = new Uint8Array(size); this.length = size; let position = 0;
    for (const chunk of chunks) { this.bytes.set(chunk, position); position += chunk.length; }
    return this.bytes.subarray(offset, Math.min(size, offset + length));
  }
}
function mp4Boxes(bytes, start = 0, end = bytes.length) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength), boxes = [];
  for (let offset = start; offset + 8 <= end;) {
    let size = view.getUint32(offset), header = 8;
    if (size === 1) { if (offset + 16 > end) throw new Error('Truncated MP4 box.'); size = Number(view.getBigUint64(offset + 8)); header = 16; }
    if (size === 0) size = end - offset;
    if (!Number.isSafeInteger(size) || size < header || offset + size > end) throw new Error('Invalid MP4 box length.');
    boxes.push({ type: String.fromCharCode(...bytes.subarray(offset + 4, offset + 8)), data: offset + header, end: offset + size }); offset += size;
  }
  return boxes;
}
async function readMp4Metadata(source) {
  let offset = 0, moov;
  for (let count = 0; count < 10000; count++) {
    const header = await source.read(offset, 16); if (header.length < 8) break;
    const view = new DataView(header.buffer, header.byteOffset, header.byteLength);
    let size = view.getUint32(0); if (size === 1) size = Number(view.getBigUint64(8)); if (size === 0) size = source.length - offset;
    if (!Number.isSafeInteger(size) || size < 8 || offset + size > source.length) throw new Error('Invalid MP4 container size.');
    const type = String.fromCharCode(...header.subarray(4, 8));
    if (type === 'moov') { moov = await source.read(offset, size); break; }
    offset += size; if (offset >= source.length) break;
  }
  if (!moov) throw new Error('The browser streaming decoder requires an MP4 container with sample tables.');
  const view = new DataView(moov.buffer, moov.byteOffset, moov.byteLength);
  const children = box => mp4Boxes(moov, box.data, box.end);
  const child = (box, type) => box && children(box).find(item => item.type === type);
  const root = mp4Boxes(moov)[0]; let frameRate = 0, audio = null;
  for (const track of children(root).filter(box => box.type === 'trak')) {
    const media = child(track, 'mdia'), header = media && child(media, 'mdhd'), handler = media && child(media, 'hdlr');
    const table = media && child(child(media, 'minf'), 'stbl'); if (!header || !handler || !table) continue;
    const type = String.fromCharCode(...moov.subarray(handler.data + 8, handler.data + 12));
    const version = moov[header.data], timescale = view.getUint32(header.data + (version ? 20 : 12));
    const timing = child(table, 'stts'); if (!timing || !timescale) continue;
    const timingCount = view.getUint32(timing.data + 4); let duration = 0, samples = 0;
    const timings = [];
    for (let i = 0; i < timingCount; i++) { const count = view.getUint32(timing.data + 8 + i * 8), delta = view.getUint32(timing.data + 12 + i * 8); timings.push([count, delta]); samples += count; duration += count * delta; }
    if (type === 'vide') { frameRate = samples * timescale / duration; continue; }
    if (type !== 'soun' || audio) continue;
    const description = child(table, 'stsd'), sizeTable = child(table, 'stsz'), chunkTable = child(table, 'stsc'), offsets = child(table, 'stco') || child(table, 'co64');
    audio = { config: null, channels: 0, frequency: 0 };
    if (!description || !sizeTable || !chunkTable || !offsets) continue;
    const entry = mp4Boxes(moov, description.data + 8, description.end)[0];
    if (!entry || entry.type !== 'mp4a' || view.getUint16(entry.data + 8) !== 0) continue;
    const esds = mp4Boxes(moov, entry.data + 28, entry.end).find(box => box.type === 'esds');
    if (!esds) continue;
    const config = findAudioConfig(moov, esds.data + 4, esds.end); if (!config || config.length < 2) continue;
    const objectType = config[0] >> 3, rateIndex = ((config[0] & 7) << 1) | (config[1] >> 7), channels = (config[1] >> 3) & 15;
    const frequency = [96000,88200,64000,48000,44100,32000,24000,22050,16000,12000,11025,8000,7350][rateIndex];
    // AAC LC has fixed sample/channel configuration. Other profiles remain explicit capabilities.
    if (objectType !== 2 || !frequency || channels < 1 || channels > 6) continue;
    const sampleCount = view.getUint32(sizeTable.data + 8), fixedSize = view.getUint32(sizeTable.data + 4);
    if (sampleCount !== samples || samples > 1000000) throw new Error('Audio sample table exceeds decoder limits.');
    const sizes = new Uint32Array(samples), positions = new Float64Array(samples), timestamps = new Float64Array(samples), durations = new Uint32Array(samples);
    for (let i = 0; i < samples; i++) sizes[i] = fixedSize || view.getUint32(sizeTable.data + 12 + i * 4);
    let mediaTime = 0, presentationOffset = 0;
    const edits = child(track, 'edts'), editList = edits && child(edits, 'elst');
    if (editList && !moov[editList.data]) {
      const count = view.getUint32(editList.data + 4), movieHeader = child(root, 'mvhd');
      const movieScale = movieHeader && view.getUint32(movieHeader.data + (moov[movieHeader.data] ? 20 : 12));
      for (let i = 0; i < count; i++) { const at = editList.data + 8 + i * 12, time = view.getInt32(at + 4);
        if (time === -1 && movieScale) presentationOffset += view.getUint32(at) / movieScale * timescale; else if (time >= 0) { mediaTime = time; break; } }
    }
    let index = 0, time = 0;
    for (const [count, delta] of timings) for (let i = 0; i < count; i++) { timestamps[index] = (time - mediaTime + presentationOffset) * 1000000 / timescale; durations[index++] = Math.round(delta * 1000000 / timescale); time += delta; }
    const mappings = view.getUint32(chunkTable.data + 4), chunks = view.getUint32(offsets.data + 4); let mapping = 0; index = 0;
    for (let chunk = 1; chunk <= chunks; chunk++) {
      while (mapping + 1 < mappings && view.getUint32(chunkTable.data + 8 + (mapping + 1) * 12) <= chunk) mapping++;
      const perChunk = view.getUint32(chunkTable.data + 12 + mapping * 12);
      let position = offsets.type === 'stco' ? view.getUint32(offsets.data + 8 + (chunk - 1) * 4) : Number(view.getBigUint64(offsets.data + 8 + (chunk - 1) * 8));
      for (let i = 0; i < perChunk; i++) { if (index >= samples || !Number.isSafeInteger(position) || position + sizes[index] > source.length) throw new Error('Invalid audio sample offsets.'); positions[index] = position; position += sizes[index++]; }
    }
    if (index !== samples) throw new Error('Incomplete audio sample table.');
    Object.assign(audio, { channels, frequency, sizes, positions, timestamps, durations, presentationShiftUs: (mediaTime - presentationOffset) * 1000000 / timescale,
      config: { codec: 'mp4a.40.2', sampleRate: frequency, numberOfChannels: channels, description: config } });
  }
  if (!(frameRate > 0) || !Number.isFinite(frameRate)) throw new Error('MP4 video timing is unavailable (fragmented containers require a streaming demuxer).');
  return { frameRate, audio };
}
function findAudioConfig(bytes, start, end) {
  for (let at = start; at < end;) {
    const tag = bytes[at++]; let length = 0, part, count = 0;
    do { if (at >= end || count++ === 4) throw new Error('Invalid MPEG-4 descriptor.'); part = bytes[at++]; length = length * 128 + (part & 127); } while (part & 128);
    const stop = at + length; if (stop > end) throw new Error('Truncated MPEG-4 descriptor.');
    if (tag === 5) return bytes.slice(at, stop);
    if (tag === 3 || tag === 4) {
      let nested = at + (tag === 4 ? 13 : 3);
      if (tag === 3) { const flags = bytes[at + 2]; if (flags & 128) nested += 2; if (flags & 64) nested += bytes[nested] + 1; if (flags & 32) nested += 2; }
      const result = findAudioConfig(bytes, nested, stop); if (result) return result;
    }
    at = stop;
  }
  return null;
}
async function decodeAudioWindow(asset, start, end) {
  const audio = asset.audio, beginUs = start * 1000000 / audio.frequency, endUs = end * 1000000 / audio.frequency;
  let first = 0, last = audio.timestamps.length;
  // Binary search makes a late seek independent of the soundtrack duration.
  while (first < last) { const middle = (first + last) >>> 1; if (audio.timestamps[middle] < beginUs) first = middle + 1; else last = middle; }
  first = Math.max(0, first - 3); last = first;
  while (last < audio.timestamps.length && audio.timestamps[last] < endUs) last++;
  const data = new Float32Array((end - start) * audio.channels);
  let failure;
  const decoder = new AudioDecoder({ error: error => { failure = error; }, output: block => {
    try {
      if (block.numberOfChannels !== audio.channels || block.sampleRate !== audio.frequency) throw new Error('Audio configuration changed during decoding.');
      const blockStart = Math.round((block.timestamp - audio.presentationShiftUs) * audio.frequency / 1000000), from = Math.max(start, blockStart), to = Math.min(end, blockStart + block.numberOfFrames);
      if (to > from) for (let channel = 0; channel < audio.channels; channel++) {
        const plane = new Float32Array(block.numberOfFrames); block.copyTo(plane, { planeIndex: channel, format: 'f32-planar' });
        for (let frame = from; frame < to; frame++) data[(frame - start) * audio.channels + channel] = plane[frame - blockStart];
      }
    } catch (error) { failure = error; } finally { block.close(); }
  } });
  try {
    decoder.configure(audio.config);
    // Fetch small packet groups. Interleaved video gaps cannot produce an unbounded request.
    for (let index = first; index < last;) {
      let stop = index + 1, rangeEnd = audio.positions[index] + audio.sizes[index];
      while (stop < last && audio.positions[stop] >= rangeEnd && audio.positions[stop] + audio.sizes[stop] - audio.positions[index] <= 1024 * 1024) { rangeEnd = audio.positions[stop] + audio.sizes[stop]; stop++; }
      const bytes = await asset.source.read(audio.positions[index], rangeEnd - audio.positions[index]);
      for (; index < stop; index++) {
        if (failure) throw failure;
        const origin = rangeEnd - bytes.length;
        decoder.decode(new EncodedAudioChunk({ type: 'key', timestamp: Math.round(audio.timestamps[index] + audio.presentationShiftUs), duration: audio.durations[index], data: bytes.subarray(audio.positions[index] - origin, audio.positions[index] - origin + audio.sizes[index]) }));
        if (decoder.decodeQueueSize >= 16) await new Promise(done => decoder.addEventListener('dequeue', done, { once: true }));
      }
    }
    await decoder.flush(); if (failure) throw failure;
    return { start, end, data };
  } finally { decoder.close(); }
}
