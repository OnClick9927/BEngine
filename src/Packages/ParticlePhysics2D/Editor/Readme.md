# Particle Physics 2D

Optional bridge between Particles2D and Physics2D. Enable `com.bengine.particle-physics2d`; its dependencies are enabled through the existing package dependency mechanism.

Add **Effects / Particle Physics 2D Collision** on a ParticleSystem2D, then set the system's collisionEnabled. The provider sweeps a circle along each particle step (a ray when radius is zero), avoiding tunneling through thin colliders. The hit normal points out of the surface. Particle settings control bounce, damping and lifetime loss; collision events can trigger sub-emitters.

The provider exposes collisionMask and includeTriggers. Edit-mode particle preview queries the authored scene through a temporary Physics2D query scope and restores the previous query world on disposal. It does not advance rigid bodies while previewing particles.

Windows checks include thin-wall collision, outward bounce, callback and child emission, scene serialization, preview world restoration and independent Player execution. This is a one-way particle response; particles do not apply forces to rigid bodies.
