# TextureAtlas

Texture atlas authoring, import and packing tools.

Editor packing depends on Pooling and reuses a build-local packing workspace across size attempts and rectangle splits. Parallel builds own separate pools; published placements and generated image data keep their own storage. The build releases the workspace in finally and disposes its pool at completion.

Enable com.bengine.texture-atlas in Package Manager. Disabling the package removes its tools and types from the project.

[编辑器制作流程](Doc/EditorAuthoring.md)。
