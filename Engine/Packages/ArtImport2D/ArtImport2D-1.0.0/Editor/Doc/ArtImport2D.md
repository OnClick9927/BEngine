# PSD, PSB, and Aseprite Import

## Photoshop

The importer reads PSD version 1 and PSB version 2 layer records, including `Lr16/Lr32` additional layer blocks. It supports 8/16/32-bit RGB and grayscale pixel layers with raw, PackBits RLE, ZIP, or ZIP prediction compression. Layer ID (`lyid`) and Unicode layer names (`luni`) are honored when present. Pixel masks use their own bounds, default color, relative placement, disabled/inverted flags, density, and real-mask channel. Mask coverage is applied to generated alpha; placement, layer opacity, and visibility remain in the `ArtDocument2D` layer entry. High-depth channels are converted to RGBA8; finite floating-point values outside [0,1] are clamped.

Embedded Photoshop RGB, gray and CMYK ICC profiles are validated and converted to sRGB using the Windows editor's native color management module. RGB/gray conversion retains 16-bit input precision; CMYK bitmap conversion uses 8-bit samples. Untagged CMYK uses an explicit simple ink-to-RGB fallback. Source high-depth channels and ICC data remain in the document. Nested Photoshop section-divider groups retain hierarchy, visibility, opacity and blend metadata.

## Layered editor

Open a document from its Inspector or `Window/2D/Layered Art`. The auxiliary editor previews the composite and edits names, visibility, opacity, placement and parent groups with Undo. Save an `.art2d.yaml` copy inside Assets to retain edits without overwriting the source. The bounded preview supports common blend modes and isolated/pass-through groups; adjustment layers and effects are not reconstructed.

## Aseprite

The importer reads 32-bit RGBA, 16-bit grayscale/alpha, and 8-bit indexed documents. Modern and legacy 6/8-bit palette chunks update colors per frame; the transparent index applies to non-background layers. Raw, zlib-compressed, and linked cels retain current-frame placement/opacity and are composited into one generated Texture per frame. All 19 file blend modes, including Hue/Saturation/Color/Luminosity, are supported. Layer hierarchy controls visibility; files with group-blending enabled composite isolated groups with their opacity/mode. Cel Z-index controls ordering. Embedded tilesets and compressed tilemaps support empty tiles and X/Y/diagonal flips. Frame duration and Forward, Reverse, PingPong, or PingPongReverse tags are retained.

## Stable references

The source `.meta` GUID owns every generated Texture. PSD layer local IDs derive from the file's stable layer ID; Aseprite frame local IDs derive from the frame index. A reimport with the same identities overwrites the same generated artifact and preserves its child GUID. Invalid input is decoded before any generated output is written, so a parse failure leaves the previous imported main and children active.

## Limits

Photoshop import excludes vector/text editing semantics, adjustment/effect compositing, vector masks, feather reconstruction and smart-object editing. Those layers are represented when they contain ordinary pixel channels. High-depth textures are quantized rather than retained as HDR. ICC conversion currently requires the Windows editor; it does not claim print proofing or Photoshop-identical rendering of every profile/effect. External Aseprite tilesets resolve relative to the owner file, including nested references with cycle/depth checks. Referenced source hashes participate in the import fingerprint, so editing a referenced tileset triggers reimport. External owner/tileset color depths must match; diagonal tile flips still require square tiles. Aseprite slice/user-data editing and its ICC conversion are not included. Generated outputs live in `Library/Artifacts` and should not be edited directly.

Format references: [Adobe Photoshop specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) and [Aseprite specification](https://github.com/aseprite/aseprite/blob/main/docs/ase-file-specs.md).
