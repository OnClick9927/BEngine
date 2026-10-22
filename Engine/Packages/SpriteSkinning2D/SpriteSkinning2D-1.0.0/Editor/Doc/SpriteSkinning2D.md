# Sprite Skinning 2D Guide

## Authoring

Create a Sprite Skin from **Assets > Create > 2D > Sprite Skin**. The default asset contains a root bone, a weighted quad, and a placeholder attachment. Use the Sprite Skinning window to add or reparent bones, edit bind poses, edit the mesh outline, triangulate it, and edit weights. Every vertex stores one to four influences whose weights are normalized when the asset is validated.

Select a GameObject with `SpriteSkin2D` and choose **Create / Bind Skeleton**. The editor creates a common skeleton root so assets with multiple root bones can be rebound by name. `Bone2D` endpoints and mesh vertices can then be dragged in the Scene view. Weight painting uses a radius, falloff, selected bone, and strength. A Scene drag is recorded as one Undo operation.

## Runtime

`SpriteSkin2D.GetDeformedLocalVertices()` evaluates the authored bind pose against the current scene bone transforms and adds per-vertex free-form deformation offsets configured through `SetDeformation`. The renderer submits the resulting arbitrary triangle mesh through the 2D render extension pipeline. Use `SetAttachment(name)` to switch a named texture/UV/tint entry without changing the mesh or bone bindings.

`skinningMode` selects Cpu, Gpu, or Auto. Vulkan/OpenGL GPU skinning uploads bind positions, four bone indices/weights, FFD offsets, and an affine bone palette; weighted deformation runs in the vertex shader. Auto uses the CPU on other shader backends or when a custom camera policy requires vertex snapping, and falls back if a GPU pipeline cannot be created. Explicit Gpu uses the same compatibility path on unsupported shader backends. CPU geometry queries remain available for gameplay and tools.

`LimbIK2D` solves a two-segment chain during `LateUpdate`. It supports either bend direction, weighted blending, reachable targets, and clamped unreachable targets. `ChainIK2D` uses constrained CCD for arbitrary bone-chain lengths with per-joint local angle limits, iteration/tolerance controls, and weighted blending. `TransformConstraint2D` copies a target's position, rotation, and scale with independent weights and authored offsets.

`SpritePoseRetarget2D` maps uniquely named bones between source/target roots, or accepts explicit mappings. Its Inspector provides Map By Name and Capture Reference Pose with Undo. Capture both rigs in their reference poses before animating the source. LateUpdate transfers shortest local rotation deltas, optional scaled translation, and scale ratios with a weight, without accumulating drift. Mappings and reference poses are scene data; retargeting never changes a skin asset.

## Asset format

`.spriteskin.yaml` files use `BEngine.SpriteSkin2D` format version 1. Fixed-point vectors, colors, rotations, and weights are stored as raw integer values for deterministic roundtrips. Runtime loading is registered through `RuntimeAssetCodecRegistry`, so scene and bundle references load the same format in exported Players.

## Limits

GPU skinning is currently implemented for Vulkan/OpenGL GLSL backends; Direct3D/WebGPU use the CPU path. Mesh authoring supports polygon triangulation, direct vertex editing, weights, and runtime FFD; it does not provide automatic image-outline tracing. Retargeting transfers authored local-pose deltas and does not infer semantic humanoid joints.
