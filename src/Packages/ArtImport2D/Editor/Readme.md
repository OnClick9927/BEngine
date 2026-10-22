# Art Import 2D

Art Import 2D imports layered Photoshop PSD/PSB files and Aseprite animation documents. Each Photoshop pixel layer and each Aseprite frame becomes a generated PNG Texture sub-asset addressed by the source asset GUID and a stable local ID.

PSD/PSB imports preserve layer names, stable layer IDs, positions, bounds, visibility, opacity, and pixels. Aseprite imports preserve layer metadata, frame duration, RGBA/grayscale/indexed palette pixels, linked cels, common blend modes, and animation tags.

Reimporting retains the main GUID, local IDs, child GUIDs, and existing scene/asset references. Parsing and decoding complete before outputs are replaced, so a damaged source cannot replace the last valid import. See `Doc/ArtImport2D.md` for supported source variants and limits.
