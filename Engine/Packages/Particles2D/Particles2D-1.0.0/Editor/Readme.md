# Particles2D

2D particle simulation, rendering, swept Physics2D collisions and edit-mode previews. Requires Animation for reusable curves, Physics2D for scene collision queries and Pooling for bounded trail-buffer reuse.

Enable com.bengine.particles2d in Package Manager. Disabling the package removes its tools and types from the project.

Add **Effects / Particle System 2D**. The Inspector exposes rate emission and Burst time/count/repeats/probability; size, velocity and angular-speed curves; color gradient; noise; and trails. Curve times and gradient keys use normalized particle lifetime (0..1). Burst times use seconds within the system duration; a looping system repeats them each cycle. Curve key edits, nested module edits and object references support Undo and scene persistence.

Sub-emitters reference another ParticleSystem2D in the same scene. Select Birth, Death or Collision and an emission count, with optional color/velocity inheritance. Disable playOnAwake on systems used only as sub-emitters. Cyclic references are rejected. World particles preserve their emitted world position when the emitter moves; Local particles follow its transform.

Collision requires a ParticleCollisionProvider2D. The built-in **Effects / Particle Physics 2D Collision** component supplies swept circle/ray queries against Physics2D colliders; no additional package is needed. Set collisionEnabled, radius, bounce, damping and lifetime loss. The particleCollision event is delivered after simulation changes. Layer and trigger filters are on the provider.

The provider sweeps along the complete particle step to avoid tunneling through thin colliders. Its hit normal points out of the surface. Edit-mode preview opens a temporary Physics2D query scope for authored scene colliders, restores the previous query world on disposal, and does not advance rigid bodies. Particles respond to surfaces without applying forces to rigid bodies. The public `BEngine.ParticlePhysics2DCollision` type and component menu are unchanged; its assembly is now `BEngine.Particles2D`.

Trails have lifetime, width curve, minimum sampling distance and a bounded point count. They can outlive their particle or disappear with it. Pause freezes both particles and trails. Play resumes; Stop(false) stops emission while existing particles drain; Stop() also clears particles/trails. Looping playOnAwake systems can prewarm one duration without losing the generated particles.

Trail histories use an instance-owned Pooling pool, returned on expiry, clearing or reset. Up to 64 idle buffers retain common histories of at most 128 points; larger histories drop their backing storage on return. Active trails retain their full configured point limit. Destroying the component releases the pool.

Use **Edit Mode Preview** and its Time slider to sample the system and its sub-emitters. Fixed-seed previews can be repeated and scrubbed backward. Saving, editing serialized properties, closing the Inspector or entering Play clears preview state. Runtime Play is never overwritten by Inspector preview sampling. Preview samples are limited to 3600 seconds.

Windows validation covers simulation, scene round-trip, Inspector edits/Undo, Vulkan/OpenGL pixel readback and an independent Player/content-update fixture in `Temp/Validation/ParticleEnhancements`. Other platform devices require separate verification.
