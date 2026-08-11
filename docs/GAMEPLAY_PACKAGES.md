# BEngine Gameplay Packages

The gameplay stack is split into four independently enabled packages. New projects enable all four in `Packages/manifest.yaml`; existing projects receive the entries when opened. Use `Window > Package Manager` to change package state. Disabling Physics also disables Terrain and Navigation, while enabling Navigation enables both dependencies.

## 3D Physics

Package: `com.bengine.physics3d`

Namespace: `BEngine.Physics3D`

The physics world runs once per `Time.fixedDeltaTime` through the scene runtime. Positions, velocities, forces, contacts, ray distances and material values use `Fix64`.

Available API:

- `Rigidbody`, force modes, damping, gravity, kinematic bodies, constraints and collision modes
- `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider` and `PhysicMaterial`
- `Physics.Raycast`, `RaycastAll`, `OverlapSphere`, `OverlapBox`, `CheckSphere` and `CheckBox`
- `OnCollisionEnter/Stay/Exit(Collision)` and `OnTriggerEnter/Stay/Exit(Collider)` script messages

```csharp
using BEngine;
using BEngine.Physics3D;

public sealed class Projectile : MonoBehaviour
{
    public override void Start()
    {
        GetComponent<Rigidbody>()?.AddForce(transform.forward * 12, ForceMode.VelocityChange);
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"Hit {collision.gameObject.name}");
    }
}
```

## Terrain

Package: `com.bengine.terrain` (depends on Physics)

Namespace: `BEngine.Terrain`

Create a terrain with `GameObject > Terrain > Terrain`, or create only its data from the Project window's `Create > Terrain Data` menu. The editor writes a versioned `*.terrain.yaml` asset and stores only its Assets-relative path in the scene.

`TerrainData` provides height regions, interpolated heights, normals and slope queries. `TerrainMeshGenerator` creates the render mesh, `Terrain` draws it, and `TerrainCollider` exposes the height field to the physics world. Updating heights increments the data version and rebuilds the GPU cache on the next render.

```csharp
using BEngine;
using BEngine.Terrain;

var data = ScriptableObject.CreateInstance<TerrainData>();
data.SetHeightmapResolution(129);
data.size = new Vector3(100, 20, 100);
data.SetHeights(0, 0, heights);
data.Save("Assets/World.terrain.yaml");
```

## AI Navigation

Package: `com.bengine.navigation` (depends on Terrain and Physics)

Namespace: `BEngine.Navigation`

The API follows the current AI Navigation component model:

- `NavMeshSurface` owns agent settings and deterministic grid baking
- `NavMeshAgent` calculates paths and moves during runtime updates
- `NavMeshObstacle` supports box/capsule carving
- `NavMeshLink` adds one-way or bidirectional graph edges
- `NavMeshModifier` and `NavMeshModifierVolume` control walkability and areas
- `NavMesh.CalculatePath` and `NavMesh.SamplePosition` provide static queries

Create a surface from `GameObject > AI Navigation > NavMesh Surface`, set its volume and voxel size, then use the Inspector `Bake` button. Surfaces with `buildOnStart` also bake when Play begins.

```csharp
using BEngine;
using BEngine.Navigation;

public sealed class MoveToTarget : MonoBehaviour
{
    public Vector3 target { get; set; } = new(8, 0, 8);
    public override void Start() => GetComponent<NavMeshAgent>()?.SetDestination(target);
}
```

## Animation

Package: `com.bengine.animation`

Namespace: `BEngine.Animation`

Create `*.anim.yaml` and `*.controller.yaml` assets from `Create > Animation`. `AnimationCurve` uses fixed-point Hermite interpolation. `AnimationClip` binds curves to Transform axes or public fixed-point/integer component members. `AnimatorController` contains states, parameters, conditions and transitions; `Animator` supplies Unity-style `Play`, `CrossFade`, `SetFloat`, `SetInteger`, `SetBool` and trigger methods.

```csharp
using BEngine;
using BEngine.Animation;

var clip = new AnimationClip { name = "Move", wrapMode = WrapMode.Loop };
clip.SetCurve("", typeof(Transform), "localPosition.y",
    AnimationCurve.Linear(0, 0, 1, 2));
clip.Save("Assets/Move.anim.yaml");
```

Animation events call zero-argument methods or methods accepting `AnimationEvent`. Controller clip paths are relative to the project's `Assets` directory.

## Runtime Order

`ISceneRuntimeSystem` is the package extension point. Core discovers loaded implementations when Play starts. Physics runs in fixed update at order `-100`; Animation runs per frame at `50`; Navigation moves agents at `100`; user `LateUpdate` runs after package frame systems.
