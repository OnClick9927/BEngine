using BEngine;

public sealed class StateNode
{
    public int Value;
    public StateNode? Next;
    public BObject? Borrowed;
}

public sealed class StateAsset : BAsset
{
    public int Count;
    public StateNode? First;
    public StateNode? Second;
    public BObject? Borrowed;
    public List<StateNode> Items = [];
}

public sealed class StateComponent : MonoBehaviour
{
    public int Count;
    public BObject? Borrowed;
}

public sealed class BufferValue
{
    private readonly StateNode _privateNode;
    public BufferValue(StateNode node) => _privateNode = node;
    public StateNode ReadPrivate() => _privateNode;
    public int GetterCalls;
    public int Dangerous { get { GetterCalls++; throw new InvalidOperationException("Do not call a buffer getter"); } set { } }
    public Action? Callback;
    public BObject? Borrowed;
}

public sealed class ReboundSource { public int Count; public StateNode[] Nodes = []; public BObject? Borrowed; }
public sealed class ReboundTarget { public int Count; public ReboundNode[] Nodes = []; public BObject? Borrowed; }
public sealed class ReboundNode { public int Value; public ReboundNode? Next; public BObject? Borrowed; }
public readonly struct StateBox(StateNode node) { public readonly StateNode Node = node; }
