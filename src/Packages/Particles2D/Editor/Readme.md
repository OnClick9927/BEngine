# Particles2D

2D particle simulation, rendering and edit-mode previews. Requires Animation for reusable curves.

Enable com.bengine.particles2d in Package Manager. Disabling the package removes its tools and types from the project.

Add **Effects / Particle System 2D**. The Inspector exposes rate emission and Burst time/count/repeats/probability; size, velocity and angular-speed curves; color gradient; noise; and trails. Curve times and gradient keys use normalized particle lifetime (0..1). Burst times use seconds within the system duration; a looping system repeats them each cycle. Curve key edits, nested module edits and object references support Undo and scene persistence.

Sub-emitters reference another ParticleSystem2D in the same scene. Select Birth, Death or Collision and an emission count, with optional color/velocity inheritance. Disable playOnAwake on systems used only as sub-emitters. Cyclic references are rejected. World particles preserve their emitted world position when the emitter moves; Local particles follow its transform.

Collision requires a ParticleCollisionProvider2D. Enable the optional **Particle Physics 2D** package and add its component for swept circle/ray queries against Physics2D colliders. Set collisionEnabled, radius, bounce, damping and lifetime loss. The particleCollision event is delivered after simulation changes. Layer and trigger filters are on the provider.

Trails have lifetime, width curve, minimum sampling distance and a bounded point count. They can outlive their particle or disappear with it. Pause freezes both particles and trails. Play resumes; Stop(false) stops emission while existing particles drain; Stop() also clears particles/trails. Looping playOnAwake systems can prewarm one duration without losing the generated particles.

Use **Edit Mode Preview** and its Time slider to sample the system and its sub-emitters. Fixed-seed previews can be repeated and scrubbed backward. Saving, editing serialized properties, closing the Inspector or entering Play clears preview state. Runtime Play is never overwritten by Inspector preview sampling. Preview samples are limited to 3600 seconds.

Windows validation covers simulation, scene round-trip, Inspector edits/Undo, Vulkan/OpenGL pixel readback and an independent Player/content-update fixture in `Temp/Validation/ParticleEnhancements`. Other platform devices require separate verification.
