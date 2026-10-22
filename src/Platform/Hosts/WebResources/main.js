import { dotnet } from './_framework/dotnet.js';
import { installWebVideo } from './video.js';
installWebVideo();

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

function uploadTexture(texture, description, encodedData) {
  const gl = requireGl();
  const data = decodeBase64(encodedData);
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

function createTextureCore(description, encodedData) {
  const gl = requireGl();
  const texture = gl.createTexture();
  if (!texture) throw new Error('Unable to allocate a WebGL texture.');
  try {
    configureTexture(texture, description);
    uploadTexture(texture, description, encodedData);
    return texture;
  } catch (error) {
    gl.deleteTexture(texture);
    throw error;
  } finally {
    gl.bindTexture(gl.TEXTURE_2D, null);
  }
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
    requireGl().flush();
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

  createMesh(layoutJson, encodedVertices, dynamic) {
    const gl = requireGl();
    const layout = JSON.parse(layoutJson);
    const vao = gl.createVertexArray();
    const buffer = gl.createBuffer();
    if (!vao || !buffer) throw new Error('Unable to allocate a WebGL vertex buffer.');
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, decodeBase64(encodedVertices), dynamic ? gl.DYNAMIC_DRAW : gl.STATIC_DRAW);
    for (const attribute of layout.Attributes) {
      gl.enableVertexAttribArray(attribute.Location);
      gl.vertexAttribPointer(attribute.Location, attribute.ComponentCount, gl.FLOAT, false,
        layout.StrideBytes, attribute.OffsetBytes);
    }
    gl.bindVertexArray(null);
    return allocateHandle(state.meshes, { vao, buffer, dynamic });
  },

  updateMesh(handle, encodedVertices) {
    const gl = requireGl();
    const mesh = requireHandle(state.meshes, handle, 'mesh');
    if (!mesh.dynamic) throw new Error('Cannot update a static WebGL mesh.');
    gl.bindBuffer(gl.ARRAY_BUFFER, mesh.buffer);
    gl.bufferData(gl.ARRAY_BUFFER, decodeBase64(encodedVertices), gl.DYNAMIC_DRAW);
  },

  deleteMesh(handle) {
    const mesh = state.meshes.get(handle);
    if (!mesh) return;
    const gl = requireGl();
    gl.deleteBuffer(mesh.buffer);
    gl.deleteVertexArray(mesh.vao);
    state.meshes.delete(handle);
  },

  createTexture(label, descriptionJson, encodedData) {
    const description = JSON.parse(descriptionJson);
    const texture = createTextureCore(description, encodedData);
    return allocateHandle(state.textures, texture);
  },

  updateTexture(handle, descriptionJson, encodedData) {
    const gl = requireGl();
    const texture = requireHandle(state.textures, handle, 'texture');
    uploadTexture(texture, JSON.parse(descriptionJson), encodedData);
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
        }, '');
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
        }, '');
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
function audioContext() {
  audioState.context ||= new (window.AudioContext || window.webkitAudioContext)();
  return audioState.context;
}
function updateAudioCompletion(output) {
  const now = audioContext().currentTime;
  while (output.pending.length && output.pending[0].end <= now) {
    output.completedFrames = output.pending[0].frames;
    output.pending.shift();
  }
}
globalThis.BEngineWebAudio = {
  create(sampleRate, channelCount) {
    const context = audioContext();
    const handle = audioState.nextHandle++;
    audioState.outputs.set(handle, {
      sampleRate, channelCount, nextTime: context.currentTime,
      submittedFrames: 0, completedFrames: 0, droppedFrames: 0, pending: []
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
    if (output.nextTime - context.currentTime > 0.75) {
      output.droppedFrames += frames;
      return;
    }
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
    output.nextTime = start + buffer.duration;
    output.submittedFrames += frames;
    output.pending.push({ end: output.nextTime, frames: output.submittedFrames });
  },
  getStatistics(handle) {
    const output = audioState.outputs.get(handle);
    if (!output) throw new Error(`Unknown Web Audio output ${handle}.`);
    updateAudioCompletion(output);
    return JSON.stringify({
      SubmittedFrames: output.submittedFrames,
      CompletedFrames: output.completedFrames,
      DroppedFrames: output.droppedFrames,
      PendingBuffers: output.pending.length
    });
  },
  getDiagnostics() {
    const outputs = Array.from(audioState.outputs.values());
    for (const output of outputs) updateAudioCompletion(output);
    return JSON.stringify({
      ContextCreated: audioState.context !== null,
      ContextState: audioState.context?.state || 'uninitialized',
      ContextSampleRate: audioState.context?.sampleRate || 0,
      OutputCount: outputs.length,
      SubmittedFrames: outputs.reduce((sum, output) => sum + output.submittedFrames, 0),
      CompletedFrames: outputs.reduce((sum, output) => sum + output.completedFrames, 0),
      DroppedFrames: outputs.reduce((sum, output) => sum + output.droppedFrames, 0),
      PendingBuffers: outputs.reduce((sum, output) => sum + output.pending.length, 0)
    });
  },
  dispose(handle) { audioState.outputs.delete(handle); }
};

function resumeAudio() {
  const context = audioContext();
  if (context.state === 'suspended') void context.resume();
}
window.addEventListener('keydown', resumeAudio, { passive: true });
window.addEventListener('pointerdown', resumeAudio, { passive: true });

async function configurePlayerContent() {
  const response = await fetch('./bengine-content.json', { cache: 'no-store' });
  if (!response.ok) throw new Error(`Unable to load BEngine content manifest (${response.status}).`);
  const manifest = await response.json();
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
  dotnet.withConfig({ resources: { vfs } });
}

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
const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
await runMain();

const player = exports.BEngine.Player.Hosts.Web.Program;
globalThis.BEnginePlayer = player;
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
function reportSafeArea() {
  const style = getComputedStyle(safeAreaProbe); const ratio = canvas.width / Math.max(1, canvas.clientWidth);
  player.ReportSafeArea(parseFloat(style.paddingLeft) * ratio || 0, parseFloat(style.paddingBottom) * ratio || 0,
    parseFloat(style.paddingRight) * ratio || 0, parseFloat(style.paddingTop) * ratio || 0);
}
let lastPointerX = 0;
let lastPointerY = 0;
let lastDiagnosticsTimestamp = -Infinity;
const automaticStartup = new URLSearchParams(window.location.search).get('bengineAutoStart') === '1';
const preferInstalledContent = new URLSearchParams(window.location.search).get('bengineUseInstalledContent') === '1';

function resize() {
  const scale = Math.max(1, window.devicePixelRatio || 1);
  const width = Math.max(1, Math.round(canvas.clientWidth * scale));
  const height = Math.max(1, Math.round(canvas.clientHeight * scale));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  player.Resize(width, height, scale);
  reportSafeArea();
}

function pointerPosition(event) {
  const bounds = canvas.getBoundingClientRect();
  const x = (event.clientX - bounds.left) * canvas.width / Math.max(1, bounds.width);
  const y = (event.clientY - bounds.top) * canvas.height / Math.max(1, bounds.height);
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
  player.ReportGamepads(JSON.stringify(Array.from(navigator.getGamepads?.() || []).filter(Boolean).map(gamepad => ({
    index: gamepad.index,
    id: gamepad.id,
    axes: Array.from(gamepad.axes),
    buttons: gamepad.buttons.map(button => button.pressed ? 1 : button.value)
  }))));
  if (automaticStartup) player.AdvanceStartup(preferInstalledContent);
  if (timestampMilliseconds - lastDiagnosticsTimestamp >= 250) {
    canvas.dataset.bengineDiagnostics = player.GetDiagnostics();
    canvas.dataset.bengineAudioDiagnostics = globalThis.BEngineWebAudio.getDiagnostics();
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
