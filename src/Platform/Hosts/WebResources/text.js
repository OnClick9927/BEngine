// Browser text stays at the platform boundary; UI layout owns its logical coordinates.
// Raster dimensions are already scaled by the renderer, so devicePixelRatio must not
// be applied again here. getImageData returns top-to-bottom, straight-alpha RGBA.
export function installWebText() {
  const surface = document.createElement('canvas');
  const context = surface.getContext('2d', { willReadFrequently: true });
  if (!context) throw new Error('BEngine needs Canvas2D for default text rendering.');
  const segmenter = typeof Intl.Segmenter === 'function'
    ? new Intl.Segmenter(undefined, { granularity: 'grapheme' }) : null;
  const defaultFamily = 'system-ui, "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", sans-serif';

  function setFont(size, family) {
    context.font = `${size}px ${family?.trim() || defaultFamily}`;
    context.textBaseline = 'alphabetic';
    context.fillStyle = '#fff';
  }

  function lineMetrics(size, text) {
    const sample = context.measureText(text || 'Mg');
    const ascent = sample.fontBoundingBoxAscent ?? sample.actualBoundingBoxAscent ?? size;
    const descent = sample.fontBoundingBoxDescent ?? sample.actualBoundingBoxDescent ?? size * 0.25;
    return { ascent, height: Math.max(size, ascent + descent) };
  }

  function paragraphs(text) { return text.replace(/\r\n?/g, '\n').split('\n'); }

  function direction(text) {
    // Canvas performs bidi ordering and shaping. Pick paragraph direction from
    // its first strong letter, including Hebrew and Arabic before Latin/CJK.
    for (const character of text) {
      if (/\p{Letter}/u.test(character))
        return /[\u0590-\u08ff\ufb1d-\ufdff\ufe70-\ufefc]/u.test(character) ? 'rtl' : 'ltr';
    }
    return 'ltr';
  }

  function wrappedLines(text, availableWidth) {
    const lines = [];
    for (const paragraph of paragraphs(text)) {
      const paragraphDirection = direction(paragraph);
      const graphemes = segmenter ? Array.from(segmenter.segment(paragraph), part => part.segment) : Array.from(paragraph);
      let line = '';
      let breakAt = -1;
      for (const grapheme of graphemes) {
        const next = line + grapheme;
        if (line && context.measureText(next).width > availableWidth) {
          if (breakAt > 0) {
            lines.push({ text: line.slice(0, breakAt).trimEnd(), direction: paragraphDirection });
            line = line.slice(breakAt).trimStart();
          } else {
            lines.push({ text: line, direction: paragraphDirection });
            line = '';
          }
          // A long word or a cluster still has to make progress at narrow widths.
          if (line && context.measureText(line + grapheme).width > availableWidth) {
            lines.push({ text: line, direction: paragraphDirection });
            line = '';
          }
          breakAt = -1;
        }
        line += grapheme;
        if (/\s/u.test(grapheme)) breakAt = line.length;
      }
      lines.push({ text: line, direction: paragraphDirection });
    }
    return lines;
  }

  globalThis.BEngineWebText = {
    measure(text, fontSize, family) {
      if (!Number.isFinite(fontSize) || fontSize <= 0) return '{}';
      setFont(fontSize, family);
      const lines = paragraphs(text);
      const metrics = lineMetrics(fontSize, text);
      return JSON.stringify({
        width: lines.reduce((width, line) => Math.max(width, context.measureText(line).width), 0),
        height: metrics.height * lines.length
      });
    },
    rasterize(text, width, height, fontSize, family, wordWrap, destination) {
      if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0 ||
          width > 16384 || height > 16384 || width * height * 4 > 256 * 1024 * 1024 ||
          !Number.isFinite(fontSize) || fontSize <= 0 ||
          destination.byteLength !== width * height * 4) return false;
      surface.width = width;
      surface.height = height;
      setFont(fontSize, family);
      const inset = Math.min(2, Math.max(0, (width - 1) / 2));
      const availableWidth = Math.max(1, width - 2 * inset);
      const lines = wordWrap ? wrappedLines(text, availableWidth)
        : paragraphs(text).map(line => ({ text: line, direction: direction(line) }));
      const metrics = lineMetrics(fontSize, text);
      const top = Math.max(0, (height - metrics.height * lines.length) / 2);
      for (let index = 0; index < lines.length; index++) {
        const line = lines[index];
        context.direction = line.direction;
        context.textAlign = line.direction === 'rtl' ? 'right' : 'left';
        context.fillText(line.text, line.direction === 'rtl' ? width - inset : inset,
          top + index * metrics.height + metrics.ascent, availableWidth);
      }
      const pixels = context.getImageData(0, 0, width, height).data;
      // Textures are a white coverage mask tinted by the UI shader. In particular,
      // transparent RGB must not darken filtered glyph edges on coloured labels.
      for (let offset = 0; offset < pixels.length; offset += 4)
        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 255;
      // MemoryView.set requires Uint8Array, while Canvas returns Uint8ClampedArray.
      // Copy into the caller-owned RGBA buffer before this synchronous import ends.
      destination.set(new Uint8Array(pixels.buffer, pixels.byteOffset, pixels.byteLength));
      return true;
    }
  };
}
