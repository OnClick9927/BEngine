# Cross-platform uniform submission

Desktop OpenGL and Android OpenGL ES share the internal Core
`GraphicsUniformValueCache`. It holds at most 64 values per program; the native
location lookup retains at most 256 names. Eviction only causes another lookup
or upload. No cache is shared between programs, devices, or contexts.

Matrix, vector, float and integer values are compared by their exact bits.
Signed zero, NaN payloads and integer values represented in a Vulkan float
staging buffer must not be collapsed by floating-point value equality. A value
is recorded after its native upload. Locations, rather than user-provided names,
keep aliases such as `colors` and `colors[0]` coherent.

Desktop OpenGL caches matrices as well as scalar/vector values. Its existing
`IGraphicsStateCache.InvalidateState` generation makes every saved value stale
after external native rendering. Applications which use raw OpenGL must call
that entry point before returning to engine rendering.

Android uploads a contiguous vector array with one `glUniform4fv` call. This
avoids the default interface's per-element names and calls. Array writes clear
cached values so a later single-element setter cannot skip a required upload.
Matrix uploads pin no retained float array: their synchronous native call reads
the numeric matrix directly. Android resources remain owned by their original
GL context and are recreated with that context, as before.

Vulkan keeps its existing frame-safe uniform upload versions. Dirty checks use
byte equality for scalar and vector-array staging data. Its C# span collection
expressions already avoid managed array allocations; they were retained.

These changes reduce redundant submission and bound retained CPU state. They
do not reduce shader precision, change gameplay math or guarantee a frame rate.
GPU readback checks must cover array aliases, array/single-element interleaving,
program switches and external-state invalidation. Short tests must distinguish
initial tiered JIT execution from warmed execution before comparing CPU time.
