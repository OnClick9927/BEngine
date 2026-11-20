using BEngine;

public sealed class PrefabProbe : Component
{
    public int Count;
    public BObject? Target;
    public PrefabNode? Graph;
}
public sealed class PrefabNode
{
    public string Name = "node";
    public PrefabNode? Next;
}
public sealed class PrefabRequired : Component { }
[RequireComponent(typeof(PrefabRequired))]
public sealed class PrefabDependent : Component { }
public sealed class PrefabAdded : Component { public int Value; }
public sealed class PrefabTracked : Component
{
    public static PrefabTracked? Last;
    public PrefabTracked() { Last = this; }
}
public sealed class PrefabThrowing : Component
{
    public static bool Fail;
    public PrefabThrowing() { if (Fail) throw new InvalidOperationException("Fixture constructor failure"); }
}
